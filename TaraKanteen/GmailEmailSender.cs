using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sends plain-text messages through the Gmail API using the refresh token
/// created by the local Google OAuth connection flow.
/// </summary>
public sealed class GmailEmailSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly GoogleRefreshTokenStore _tokenStore;
    private readonly ILogger<GmailEmailSender> _logger;

    public GmailEmailSender(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        GoogleRefreshTokenStore tokenStore,
        ILogger<GmailEmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task SendAsync(
        string recipientEmail,
        string subject,
        string plainTextBody,
        CancellationToken cancellationToken = default)
    {
        var senderEmail = _configuration["GoogleOAuth:SenderEmail"];
        if (string.IsNullOrWhiteSpace(senderEmail))
            throw new InvalidOperationException("GoogleOAuth:SenderEmail is not configured.");

        var sender = new MailAddress(senderEmail);
        var recipient = new MailAddress(recipientEmail);

        if (!string.Equals(sender.Address, senderEmail.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(recipient.Address, recipientEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A valid sender and recipient email address are required.");
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Contains('\r') || subject.Contains('\n'))
            throw new ArgumentException("A valid email subject is required.", nameof(subject));

        var accessToken = await GetAccessTokenAsync(cancellationToken);

        var encodedSubject = Convert.ToBase64String(Encoding.UTF8.GetBytes(subject));
        var encodedBody = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(plainTextBody ?? string.Empty),
            Base64FormattingOptions.InsertLineBreaks);

        var mimeMessage =
            $"From: \"TaraKanteen\" <{sender.Address}>\r\n" +
            $"To: {recipient.Address}\r\n" +
            $"Subject: =?UTF-8?B?{encodedSubject}?=\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: text/plain; charset=UTF-8\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\n" +
            encodedBody;

        var rawMessage = Convert.ToBase64String(Encoding.UTF8.GetBytes(mimeMessage))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { raw = rawMessage }),
            Encoding.UTF8,
            "application/json");

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Gmail API message send failed with HTTP {StatusCode}.",
                (int)response.StatusCode);
            throw new InvalidOperationException(
                $"Gmail API could not send the email (HTTP {(int)response.StatusCode}). Check that Gmail sending access is still authorized.");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var clientId = _configuration["GoogleOAuth:ClientId"];
        var clientSecret = _configuration["GoogleOAuth:ClientSecret"];
        var refreshToken = await _tokenStore.LoadAsync();

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException(
                "Google Gmail is not fully connected. Reconnect the sender account at /oauth/google/start.");
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };

        var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(form),
            cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var googleError = "unknown_error";
            try
            {
                using var document = JsonDocument.Parse(responseJson);
                if (document.RootElement.TryGetProperty("error", out var errorElement) &&
                    errorElement.ValueKind == JsonValueKind.String)
                {
                    googleError = errorElement.GetString() ?? googleError;
                }
            }
            catch (JsonException)
            {
                // Keep the generic error label if Google's response is not JSON.
            }

            _logger.LogError(
                "Google access-token refresh failed with HTTP {StatusCode}; Google error {GoogleError}.",
                (int)response.StatusCode,
                googleError);
            throw new InvalidOperationException(
                "Google could not refresh the Gmail authorization. Reconnect the sender account at /oauth/google/start.");
        }

        using var tokenDocument = JsonDocument.Parse(responseJson);
        if (!tokenDocument.RootElement.TryGetProperty("access_token", out var accessTokenElement) ||
            accessTokenElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(accessTokenElement.GetString()))
        {
            throw new InvalidOperationException("Google did not return an access token for Gmail sending.");
        }

        return accessTokenElement.GetString()!;
    }
}
