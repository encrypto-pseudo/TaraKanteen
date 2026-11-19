using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Adds the local development OAuth flow used to authorize TaraKanteen's
/// dedicated Gmail sender account. This does not authorize student accounts.
/// </summary>
public static class GoogleGmailOAuthExtensions
{
    private const string GmailSendScope = "https://www.googleapis.com/auth/gmail.send";
    private const string StateCookieName = "TaraKanteen.GoogleOAuth.State";
    private const string StateCookiePath = "/oauth/google";

    public static void MapGoogleGmailOAuth(this WebApplication app)
    {
        app.MapGet("/oauth/google/start", (
            HttpContext context,
            IConfiguration configuration) =>
        {
            if (!app.Environment.IsDevelopment())
                return Results.NotFound();

            var clientId = configuration["GoogleOAuth:ClientId"];
            var redirectUri = configuration["GoogleOAuth:RedirectUri"];

            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(redirectUri))
            {
                return Results.Problem(
                    "Google OAuth ClientId or RedirectUri is missing from development configuration.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var state = CreateRandomState();
            context.Response.Cookies.Append(
                StateCookieName,
                state,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromMinutes(10),
                    Path = StateCookiePath
                });

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = GmailSendScope,
                ["access_type"] = "offline",
                ["prompt"] = "consent",
                ["include_granted_scopes"] = "true",
                ["state"] = state
            };

            var query = string.Join(
                "&",
                parameters.Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

            return Results.Redirect(
                "https://accounts.google.com/o/oauth2/v2/auth?" + query);
        });

        app.MapGet("/oauth/google/callback", async (
            HttpContext context,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            GoogleRefreshTokenStore tokenStore) =>
        {
            if (!app.Environment.IsDevelopment())
                return Results.NotFound();

            // Prevent this one-time authorization response from being cached or
            // sent as a referrer to another page.
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'";

            var expectedState = context.Request.Cookies[StateCookieName];
            var receivedState = context.Request.Query["state"].ToString();
            context.Response.Cookies.Delete(
                StateCookieName,
                new CookieOptions
                {
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Path = StateCookiePath
                });

            if (!StatesMatch(expectedState, receivedState))
            {
                return Results.Problem(
                    "Google authorization could not be verified. Close this page and start the connection again.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

           var authorizationError = context.Request.Query["error"].ToString();
if (!string.IsNullOrWhiteSpace(authorizationError))
            {
                return Results.Problem(
                    "Google authorization was cancelled or denied. You can close this page and try again.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var code = context.Request.Query["code"].ToString();
            var clientId = configuration["GoogleOAuth:ClientId"];
            var clientSecret = configuration["GoogleOAuth:ClientSecret"];
            var redirectUri = configuration["GoogleOAuth:RedirectUri"];

            if (string.IsNullOrWhiteSpace(code) ||
                string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret) ||
                string.IsNullOrWhiteSpace(redirectUri))
            {
                return Results.Problem(
                    "The Google authorization response or server configuration is incomplete.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var form = new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            };

            using var client = httpClientFactory.CreateClient();
            using var tokenResponse = await client.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(form));

            if (!tokenResponse.IsSuccessStatusCode)
            {
                // Log only Google's safe error fields. Never log the authorization code,
                // client secret, access token, refresh token, or the full response body.
                var googleError = "unknown_error";
                var googleErrorDescription = "No error description was returned.";

                var errorBody = await tokenResponse.Content.ReadAsStringAsync();
                try
                {
                    using var errorDocument = JsonDocument.Parse(errorBody);
                    var errorRoot = errorDocument.RootElement;

                    if (errorRoot.TryGetProperty("error", out var errorElement) &&
                        errorElement.ValueKind == JsonValueKind.String)
                    {
                        googleError = errorElement.GetString() ?? googleError;
                    }

                    if (errorRoot.TryGetProperty("error_description", out var descriptionElement) &&
                        descriptionElement.ValueKind == JsonValueKind.String)
                    {
                        googleErrorDescription = descriptionElement.GetString() ?? googleErrorDescription;
                    }
                }
                catch (JsonException)
                {
                    // Keep the generic values if Google did not return JSON.
                }

                app.Logger.LogError(
                    "Google OAuth token exchange failed. HTTP {StatusCode}; Google error {GoogleError}; description: {GoogleErrorDescription}",
                    (int)tokenResponse.StatusCode,
                    googleError,
                    googleErrorDescription);

                return Results.Problem(
                    app.Environment.IsDevelopment()
                        ? $"Google token exchange failed ({googleError}). Check the VS Code terminal for details, then start a fresh authorization attempt."
                        : "Google authorization could not be completed. Please try again.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var tokens = await tokenResponse.Content.ReadFromJsonAsync<GoogleOAuthTokenResponse>();
            if (string.IsNullOrWhiteSpace(tokens?.RefreshToken))
            {
                return Results.Problem(
                    "Google did not return a refresh token. Remove TaraKanteen's access from the sender Google Account's third-party connections, then start authorization again.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            await tokenStore.SaveAsync(tokens.RefreshToken);

            var senderEmail = WebUtility.HtmlEncode(
                configuration["GoogleOAuth:SenderEmail"] ?? "your configured Gmail account");

            var html = $"""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>TaraKanteen Gmail connected</title>
                </head>
                <body style="font-family:Arial,sans-serif;max-width:620px;margin:48px auto;padding:0 20px;line-height:1.5">
                  <h1>Gmail account connected</h1>
                  <p>TaraKanteen has been authorized to send email using <strong>{senderEmail}</strong>.</p>
                  <p>Your refresh token has been stored locally in protected form. No token needs to be copied or shared.</p>
                  <p>You can close this page and return to VS Code.</p>
                </body>
                </html>
                """;

            return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8);
        });
    }

    private static string CreateRandomState()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool StatesMatch(string? expected, string? received)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(received))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var receivedBytes = Encoding.UTF8.GetBytes(received);

        return expectedBytes.Length == receivedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
    }
}

/// <summary>
/// Stores the Gmail refresh token outside the project folder and protects it
/// with ASP.NET Core Data Protection so it is not saved as plain text.
/// </summary>
public sealed class GoogleRefreshTokenStore
{
    private readonly IDataProtector _protector;
    private readonly string _filePath;

    public GoogleRefreshTokenStore(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(
            "TaraKanteen.GoogleOAuth.RefreshToken.v1");

        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            throw new InvalidOperationException(
                "The local application data folder is not available for secure token storage.");

        var folder = Path.Combine(localData, "TaraKanteen", "PrivateData");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "google-refresh-token.dat");
    }

    public async Task SaveAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("A refresh token is required.", nameof(refreshToken));

        var protectedToken = _protector.Protect(refreshToken);
        var temporaryPath = _filePath + ".tmp";

        await File.WriteAllTextAsync(temporaryPath, protectedToken, Encoding.UTF8);
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    public async Task<string?> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return null;

        var protectedToken = await File.ReadAllTextAsync(_filePath, Encoding.UTF8);
        return _protector.Unprotect(protectedToken);
    }
}

internal sealed class GoogleOAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}
