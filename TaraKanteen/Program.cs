using System.Security.Cryptography;
using System.Security.Claims;
using System.Data.Common;
using System.Net.Mail;
using Microsoft.AspNetCore.Authentication; 
using Microsoft.AspNetCore.Authentication.Cookies; 
using Microsoft.AspNetCore.Identity; 
using Microsoft.EntityFrameworkCore;

public partial class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // SQLite database file, stored outside wwwroot.
        var databasePath = Path.Combine(
            builder.Environment.ContentRootPath,
            "app.db"
        );

        // Register the database connection.
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}")
        );

        // Register password hashing.
        builder.Services.AddScoped<
            IPasswordHasher<AppUser>,
            PasswordHasher<AppUser>
        >();

        // Register cookie authentication.
        builder.Services
            .AddAuthentication(
                CookieAuthenticationDefaults.AuthenticationScheme
            )
            .AddCookie(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.ExpireTimeSpan = TimeSpan.FromHours(2);

                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = 401;
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = 403;
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddAuthorization(options =>
{
        options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
});

        var app = builder.Build();

        // Create the database and Users table on the first run.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            await db.Database.EnsureCreatedAsync();

            // Keep the SQLite schema compatible with the current models.
            // EnsureCreatedAsync does not add columns to an existing database.
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();

            try
            {
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS Products (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Description TEXT NULL,
                        Price TEXT NOT NULL DEFAULT '0',
                        ImageUrl TEXT NULL,
                        Category TEXT NOT NULL DEFAULT 'Uncategorized',
                        Icon TEXT NULL,
                        Stock INTEGER NOT NULL DEFAULT 0,
                        IsAvailable INTEGER NOT NULL DEFAULT 1,
                        CreatedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z'
                    );

                    CREATE TABLE IF NOT EXISTS Orders (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        UserId INTEGER NOT NULL,
                        StudentUsername TEXT NOT NULL,
                        Total REAL NOT NULL,
                        Status TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        FOREIGN KEY (UserId) REFERENCES Users (Id)
                    );

                    CREATE TABLE IF NOT EXISTS OrderItems (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        OrderId INTEGER NOT NULL,
                        ProductId INTEGER NOT NULL,
                        ProductName TEXT NOT NULL,
                        Quantity INTEGER NOT NULL,
                        UnitPrice REAL NOT NULL,
                        LineTotal REAL NOT NULL,
                        FOREIGN KEY (OrderId) REFERENCES Orders (Id),
                        FOREIGN KEY (ProductId) REFERENCES Products (Id)
                    );

                    CREATE INDEX IF NOT EXISTS IX_Orders_CreatedAt
                        ON Orders (CreatedAt);

                    CREATE INDEX IF NOT EXISTS IX_OrderItems_OrderId
                        ON OrderItems (OrderId);
                    """);

                // Add columns that may be missing from older copies of app.db.
                await EnsureSqliteColumnAsync(
                    connection, "Users", "Role",
                    "Role TEXT NOT NULL DEFAULT 'User'");

                // Email addresses are nullable so existing accounts remain valid
                // until each user adds and verifies a school email address.
                await EnsureSqliteColumnAsync(
                    connection, "Users", "Email",
                    "Email TEXT NULL");
                await EnsureSqliteColumnAsync(
                    connection, "Users", "EmailVerified",
                    "EmailVerified INTEGER NOT NULL DEFAULT 0");
                await EnsureSqliteColumnAsync(
                    connection, "Users", "PendingEmail",
                    "PendingEmail TEXT NULL");
                await EnsureSqliteColumnAsync(
                    connection, "Users", "EmailVerificationCodeHash",
                    "EmailVerificationCodeHash TEXT NULL");
                await EnsureSqliteColumnAsync(
                    connection, "Users", "EmailVerificationCodeExpiresAt",
                    "EmailVerificationCodeExpiresAt TEXT NULL");
                await EnsureSqliteColumnAsync(
                    connection, "Users", "EmailVerificationCodeAttempts",
                    "EmailVerificationCodeAttempts INTEGER NOT NULL DEFAULT 0");
                await EnsureSqliteColumnAsync(
                    connection, "Users", "EmailVerificationRequestedAt",
                    "EmailVerificationRequestedAt TEXT NULL");

                await EnsureSqliteColumnAsync(
                    connection, "Users", "PasswordResetCodeHash",
                    "PasswordResetCodeHash TEXT NULL");
                await EnsureSqliteColumnAsync(
                    connection, "Users", "PasswordResetCodeExpiresAt",
                    "PasswordResetCodeExpiresAt TEXT NULL");
                await EnsureSqliteColumnAsync(
                    connection, "Users", "PasswordResetCodeAttempts",
                    "PasswordResetCodeAttempts INTEGER NOT NULL DEFAULT 0");
                await EnsureSqliteColumnAsync(
                    connection, "Users", "PasswordResetRequestedAt",
                    "PasswordResetRequestedAt TEXT NULL");

                // A verified email address can belong to only one account.
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Email
                    ON Users (Email)
                    WHERE Email IS NOT NULL AND Email <> '';
                    """);

                await EnsureSqliteColumnAsync(
                    connection, "Products", "Description",
                    "Description TEXT NULL");

                await EnsureSqliteColumnAsync(
                    connection, "Products", "Price",
                    "Price TEXT NOT NULL DEFAULT '0'");

                await EnsureSqliteColumnAsync(
                    connection, "Products", "ImageUrl",
                    "ImageUrl TEXT NULL");

                await EnsureSqliteColumnAsync(
                    connection, "Products", "Category",
                    "Category TEXT NOT NULL DEFAULT 'Uncategorized'");

                await EnsureSqliteColumnAsync(
                    connection, "Products", "Icon",
                    "Icon TEXT NULL");

                await EnsureSqliteColumnAsync(
                    connection, "Products", "Stock",
                    "Stock INTEGER NOT NULL DEFAULT 0");

                await EnsureSqliteColumnAsync(
                    connection, "Products", "IsAvailable",
                    "IsAvailable INTEGER NOT NULL DEFAULT 1");

                await EnsureSqliteColumnAsync(
                    connection, "Products", "CreatedAt",
                    "CreatedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z'");
            }
            finally
            {
                await connection.CloseAsync();
            }

            if (!await db.Products.AnyAsync())
            {
                db.Products.AddRange(
                    new Product { Name = "Chicken Adobo Bowl", Description = "Tender adobo, fluffy rice, happy lunch.", Price = 89, Category = "meals", Icon = "🍛", Stock = 24 },
                    new Product { Name = "Cheesy Chicken Melt", Description = "Toasty, cheesy, and made to go.", Price = 65, Category = "snacks", Icon = "🥪", Stock = 18 },
                    new Product { Name = "Creamy Veggie Pasta", Description = "A cozy bowl with a garden-fresh twist.", Price = 75, Category = "meals", Icon = "🍝", Stock = 16 },
                    new Product { Name = "Mango Cooldown", Description = "Real mango flavor for sunny afternoons.", Price = 35, Category = "drinks", Icon = "🥭", Stock = 30 },
                    new Product { Name = "Savory Pancit", Description = "Classic noodles tossed with tasty toppings.", Price = 60, Category = "meals", Icon = "🍜", Stock = 20 },
                    new Product { Name = "Choco Chip Cookie", Description = "Soft-baked and just the right amount sweet.", Price = 25, Category = "snacks", Icon = "🍪", Stock = 35 },
                    new Product { Name = "Chilled Choco Milk", Description = "A cold, creamy pick-me-up between classes.", Price = 30, Category = "drinks", Icon = "🥛", Stock = 28 },
                    new Product { Name = "Fresh Fruit Cup", Description = "A colorful mix, cut fresh this morning.", Price = 40, Category = "snacks", Icon = "🍓", Stock = 22 }
                );
                await db.SaveChangesAsync();
            }

            // Create a sample account for local development only.
            if (app.Environment.IsDevelopment() &&
                !await db.Users.AnyAsync(u => u.Username == "lester"))
            {
                var hasher = scope.ServiceProvider
                    .GetRequiredService<IPasswordHasher<AppUser>>();

                var user = new AppUser
                {
                    Username = "lester"
                };

                user.PasswordHash = hasher.HashPassword(
                    user,
                    "PracticeOnly!123"
                );

                db.Users.Add(user);

                await db.SaveChangesAsync();
            }
        }
            

        app.UseHttpsRedirection();

        // Opens wwwroot/index.html at the root URL.
        app.UseDefaultFiles();

        // Serves CSS, JavaScript, images, and other public files.
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        // LOGIN ENDPOINT
        app.MapPost("/api/login", async (
            LoginRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> hasher,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new
                {
                    message = "Enter your username and password."
                });
            }

            var username = request.Username.Trim();

            // Find the user in the Users table.
            var user = await db.Users.SingleOrDefaultAsync(
                u => u.Username == username
            );

            if (user is null)
            {
                return Results.Json(
                    new
                    {
                        message = "Invalid username or password."
                    },
                    statusCode: 401
                );
            }

            // Compare the submitted password with the saved hash.
            var result = hasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password
            );

            if (result == PasswordVerificationResult.Failed)
            {
                return Results.Json(
                    new
                    {
                        message = "Invalid username or password."
                    },
                    statusCode: 401
                );
            }

            // Upgrade the password hash when needed.
            if (result ==
                PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = hasher.HashPassword(
                    user,
                    request.Password
                );

                await db.SaveChangesAsync();
            }

            // Information stored in the authentication cookie.
            var claims = new[]
            {
        new Claim(
            ClaimTypes.NameIdentifier,
            user.Id.ToString()
        ),
        new Claim(
            ClaimTypes.Name,
            user.Username
        ),
        new Claim(
            ClaimTypes.Role,
            user.Role
        )
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme
            );

            // Sign in the user by creating an authentication cookie.
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity)
            );

            return Results.Ok(new
            {
                message = "Login successful!",
                role = user.Role
            });
        });

        // REGISTER ENDPOINT
        app.MapPost("/api/register", async (
            RegisterRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> hasher) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Email))
            {
                return Results.BadRequest(new
                {
                    message = "Enter a username, school email, and password."
                });
            }

            var username = request.Username.Trim();
            var email = NormalizeSchoolEmail(request.Email);
            if (email is null)
            {
                return Results.BadRequest(new
                {
                    message = "Use a valid school email ending in @lifeinstitute.edu.ph."
                });
            }

            if (username.Length < 3 || username.Length > 32)
            {
                return Results.BadRequest(new
                {
                    message = "Your username must be between 3 and 32 characters."
                });
            }

            if (request.Password.Length < 8)
            {
                return Results.BadRequest(new
                {
                    message = "Your password must be at least 8 characters."
                });
            }

            if (await db.Users.AnyAsync(user => user.Username == username))
            {
                return Results.Conflict(new
                {
                    message = "That username is already taken. Try another one."
                });
            }

            if (await db.Users.AnyAsync(user =>
                (user.Email != null && user.Email.ToLower() == email) ||
                (user.PendingEmail != null && user.PendingEmail.ToLower() == email)))
            {
                return Results.Conflict(new
                {
                    message = "That school email is already linked to or pending verification on another account."
                });
            }

            var user = new AppUser
            {
                Username = username,
                Role = "User",
                PendingEmail = email
            };

            user.PasswordHash = hasher.HashPassword(user, request.Password);
            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (await db.Users.AnyAsync(existing => existing.Username == username))
                {
                    return Results.Conflict(new
                    {
                        message = "That username is already taken. Try another one."
                    });
                }

                if (await db.Users.AnyAsync(existing =>
                    (existing.Email != null && existing.Email.ToLower() == email) ||
                    (existing.PendingEmail != null && existing.PendingEmail.ToLower() == email)))
                {
                    return Results.Conflict(new
                    {
                        message = "That school email is already linked to or pending verification on another account."
                    });
                }

                throw;
            }

            return Results.Created("/login.html", new
            {
                message = "Your account is ready. Log in, then verify your school email before using password recovery."
            });
        });
        // EMAIL: CURRENT ACCOUNT EMAIL STATUS
        app.MapGet("/api/account/email-status", async (
            AppDbContext db,
            HttpContext context) =>
        {
            var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdValue, out var userId))
                return Results.Unauthorized();

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return Results.Unauthorized();

            return Results.Ok(new
            {
                email = user.Email,
                emailVerified = user.EmailVerified,
                pendingEmail = user.PendingEmail
            });
        }).RequireAuthorization();

        // EMAIL: REQUEST A VERIFICATION CODE
        // Existing accounts use this after signing in to attach a school email.
        // This stage is development-only: the code is logged to the server terminal.
        app.MapPost("/api/account/email/request-verification", async (
            EmailVerificationRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> hasher,
            HttpContext context) =>
        {
            if (!app.Environment.IsDevelopment())
            {
                return Results.Problem(
                    "Email delivery is not configured. Email verification is available only in local development for now.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var email = NormalizeSchoolEmail(request.Email);
            if (email is null)
            {
                return Results.BadRequest(new
                {
                    message = "Enter a valid school email ending in @lifeinstitute.edu.ph."
                });
            }

            var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdValue, out var userId))
                return Results.Unauthorized();

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return Results.Unauthorized();

            if (string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase) && user.EmailVerified)
            {
                return Results.Ok(new
                {
                    message = "This school email is already verified on your account."
                });
            }

            var emailInUse = await db.Users.AnyAsync(u =>
                u.Id != userId &&
                ((u.Email != null && u.Email.ToLower() == email) ||
                 (u.PendingEmail != null && u.PendingEmail.ToLower() == email)));
            if (emailInUse)
            {
                return Results.Conflict(new
                {
                    message = "That school email is already linked to another account."
                });
            }

            var now = DateTime.UtcNow;
            if (user.EmailVerificationRequestedAt.HasValue &&
                now - user.EmailVerificationRequestedAt.Value < TimeSpan.FromSeconds(60))
            {
                return Results.Ok(new
                {
                    message = "A verification request was recently made. Please wait one minute before requesting another code."
                });
            }

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            user.PendingEmail = email;
            user.EmailVerificationCodeHash = hasher.HashPassword(user, code);
            user.EmailVerificationCodeExpiresAt = now.AddMinutes(10);
            user.EmailVerificationCodeAttempts = 0;
            user.EmailVerificationRequestedAt = now;

            await db.SaveChangesAsync();

            app.Logger.LogWarning(
                "DEVELOPMENT EMAIL VERIFICATION CODE for {Username} ({Email}): {Code}",
                user.Username,
                email,
                code);

            return Results.Ok(new
            {
                message = "A verification code was generated. In local development, check the VS Code terminal. The code expires in 10 minutes."
            });
        }).RequireAuthorization();

        // EMAIL: VERIFY THE SCHOOL EMAIL ADDRESS
        app.MapPost("/api/account/email/verify", async (
            EmailVerificationCompleteRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> hasher,
            HttpContext context) =>
        {
            if (!app.Environment.IsDevelopment())
            {
                return Results.Problem(
                    "Email delivery is not configured. Email verification is available only in local development for now.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length != 6 ||
                !request.Code.Trim().All(char.IsDigit))
            {
                return Results.BadRequest(new { message = "Enter the six-digit verification code." });
            }

            var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdValue, out var userId))
                return Results.Unauthorized();

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return Results.Unauthorized();

            var now = DateTime.UtcNow;
            if (string.IsNullOrEmpty(user.PendingEmail) ||
                string.IsNullOrEmpty(user.EmailVerificationCodeHash) ||
                !user.EmailVerificationCodeExpiresAt.HasValue ||
                user.EmailVerificationCodeExpiresAt.Value <= now ||
                user.EmailVerificationCodeAttempts >= 5)
            {
                user.EmailVerificationCodeHash = null;
                user.EmailVerificationCodeExpiresAt = null;
                user.EmailVerificationCodeAttempts = 0;
                await db.SaveChangesAsync();
                return Results.BadRequest(new { message = "The code is invalid or expired. Request a new one." });
            }

            var result = hasher.VerifyHashedPassword(
                user,
                user.EmailVerificationCodeHash,
                request.Code.Trim());

            if (result == PasswordVerificationResult.Failed)
            {
                user.EmailVerificationCodeAttempts++;
                if (user.EmailVerificationCodeAttempts >= 5)
                {
                    user.EmailVerificationCodeHash = null;
                    user.EmailVerificationCodeExpiresAt = null;
                }
                await db.SaveChangesAsync();
                return Results.BadRequest(new { message = "Incorrect code. Check it and try again." });
            }

            var emailInUse = await db.Users.AnyAsync(u =>
                u.Id != userId &&
                ((u.Email != null && u.Email.ToLower() == user.PendingEmail) ||
                 (u.PendingEmail != null && u.PendingEmail.ToLower() == user.PendingEmail)));
            if (emailInUse)
            {
                user.EmailVerificationCodeHash = null;
                user.EmailVerificationCodeExpiresAt = null;
                await db.SaveChangesAsync();
                return Results.Conflict(new { message = "That school email is already linked to another account." });
            }

            user.Email = user.PendingEmail;
            user.EmailVerified = true;
            user.PendingEmail = null;
            user.EmailVerificationCodeHash = null;
            user.EmailVerificationCodeExpiresAt = null;
            user.EmailVerificationCodeAttempts = 0;
            user.EmailVerificationRequestedAt = null;

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                message = "Your school email has been verified and linked to your account.",
                email = user.Email,
                emailVerified = user.EmailVerified
            });
        }).RequireAuthorization();

        // PASSWORD RESET: REQUEST A CODE USING A VERIFIED SCHOOL EMAIL
        // In local development, the code is printed to the VS Code terminal.
        app.MapPost("/api/password-reset/request", async (
            PasswordResetRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> hasher) =>
        {
            if (!app.Environment.IsDevelopment())
            {
                return Results.Problem(
                    "Password-reset email delivery is not configured. This development endpoint is disabled outside local development.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var email = NormalizeSchoolEmail(request.Email);
            const string genericMessage =
                "If a verified account matches that school email, a reset code has been generated. Check the server terminal in development mode.";

            if (email is null)
                return Results.Ok(new { message = genericMessage });

            var user = await db.Users.SingleOrDefaultAsync(u =>
                u.Email != null && u.Email.ToLower() == email && u.EmailVerified);

            // Do not reveal whether an account or verified email exists.
            if (user is null)
                return Results.Ok(new { message = genericMessage });

            var now = DateTime.UtcNow;
            if (user.PasswordResetRequestedAt.HasValue &&
                now - user.PasswordResetRequestedAt.Value < TimeSpan.FromSeconds(60))
            {
                return Results.Ok(new { message = genericMessage });
            }

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            user.PasswordResetCodeHash = hasher.HashPassword(user, code);
            user.PasswordResetCodeExpiresAt = now.AddMinutes(10);
            user.PasswordResetCodeAttempts = 0;
            user.PasswordResetRequestedAt = now;
            await db.SaveChangesAsync();

            app.Logger.LogWarning(
                "DEVELOPMENT PASSWORD RESET CODE for {Username} ({Email}): {Code}",
                user.Username,
                email,
                code);

            return Results.Ok(new { message = genericMessage });
        });

        // PASSWORD RESET: COMPLETE USING A VERIFIED SCHOOL EMAIL
        app.MapPost("/api/password-reset/complete", async (
            PasswordResetCompleteRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> hasher,
            HttpContext context) =>
        {
            if (!app.Environment.IsDevelopment())
            {
                return Results.Problem(
                    "Password-reset email delivery is not configured. This development endpoint is disabled outside local development.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var email = NormalizeSchoolEmail(request.Email);
            if (email is null || string.IsNullOrWhiteSpace(request.Code) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return Results.BadRequest(new { message = "Complete all fields using your verified school email." });
            }

            if (request.NewPassword.Length < 8 || request.NewPassword.Length > 128)
            {
                return Results.BadRequest(new { message = "Your new password must be between 8 and 128 characters." });
            }

            var user = await db.Users.SingleOrDefaultAsync(u =>
                u.Email != null && u.Email.ToLower() == email && u.EmailVerified);

            if (user is null)
                return Results.BadRequest(new { message = "Invalid or expired code. Request a new one." });

            var now = DateTime.UtcNow;
            if (string.IsNullOrEmpty(user.PasswordResetCodeHash) ||
                !user.PasswordResetCodeExpiresAt.HasValue ||
                user.PasswordResetCodeExpiresAt.Value <= now ||
                user.PasswordResetCodeAttempts >= 5)
            {
                user.PasswordResetCodeHash = null;
                user.PasswordResetCodeExpiresAt = null;
                user.PasswordResetCodeAttempts = 0;
                await db.SaveChangesAsync();
                return Results.BadRequest(new { message = "Invalid or expired code. Request a new one." });
            }

            var verification = hasher.VerifyHashedPassword(
                user,
                user.PasswordResetCodeHash,
                request.Code.Trim());

            if (verification == PasswordVerificationResult.Failed)
            {
                user.PasswordResetCodeAttempts++;
                if (user.PasswordResetCodeAttempts >= 5)
                {
                    user.PasswordResetCodeHash = null;
                    user.PasswordResetCodeExpiresAt = null;
                }
                await db.SaveChangesAsync();
                return Results.BadRequest(new { message = "Invalid or expired code. Check your code and try again." });
            }

            user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
            user.PasswordResetCodeHash = null;
            user.PasswordResetCodeExpiresAt = null;
            user.PasswordResetCodeAttempts = 0;
            user.PasswordResetRequestedAt = null;
            await db.SaveChangesAsync();

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return Results.Ok(new
            {
                message = "Your password has been changed successfully. You can now log in."
            });
        });

        // CURRENT USER ENDPOINT
        // Accessible only to signed-in users.
        app.MapGet("/api/me", (HttpContext context) =>
        {
            return Results.Ok(new
            {
                username = context.User.Identity!.Name,
                role = context.User.FindFirstValue(ClaimTypes.Role)
            });
        }).RequireAuthorization();

        // LOGOUT ENDPOINT
        app.MapPost("/api/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme
            );

            return Results.Ok(new
            {
                message = "Logged out."
            });
        }).RequireAuthorization();

        app.MapGet("/api/products", async (AppDbContext db) =>
            Results.Ok(await db.Products
                .Where(product => product.IsAvailable && product.Stock > 0)
                .OrderBy(product => product.Category)
                .ThenBy(product => product.Name)
                .ToListAsync()))
            .RequireAuthorization();

        app.MapGet("/api/orders/mine", async (
            HttpContext context,
            AppDbContext db) =>
        {
            var userId = int.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var orders = await db.Orders
                .Where(order => order.UserId == userId)
                .OrderByDescending(order => order.CreatedAt)
                .Select(order => new
                {
                    order.Id,
                    order.Total,
                    order.Status,
                    order.CreatedAt,
                    Items = db.OrderItems.Where(item => item.OrderId == order.Id)
                        .Select(item => new { item.ProductName, item.Quantity, item.UnitPrice })
                        .ToList()
                })
                .ToListAsync();
            return Results.Ok(orders);
        })
            .RequireAuthorization();

        app.MapPost("/api/orders", async (
            OrderRequest request,
            HttpContext context,
            AppDbContext db) =>
        {
            if (request.Items is null || request.Items.Count == 0 || request.Items.Count > 50 ||
                request.Items.Any(item => item is null || item.ProductId <= 0 || item.Quantity <= 0 || item.Quantity > 99))
            {
                return Results.BadRequest(new { message = "Choose valid items and quantities for your order." });
            }

            var requestedItems = request.Items
                .GroupBy(item => item!.ProductId)
                .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
                .ToList();
            if (requestedItems.Any(item => item.Quantity > 99))
            {
                return Results.BadRequest(new { message = "An item quantity cannot exceed 99." });
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var productIds = requestedItems.Select(item => item.ProductId).ToList();
            var products = await db.Products
                .Where(product => productIds.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id);

            if (products.Count != requestedItems.Count ||
                requestedItems.Any(item => !products[item.ProductId].IsAvailable || products[item.ProductId].Stock < item.Quantity))
            {
                return Results.Conflict(new { message = "Some items are unavailable or no longer have enough stock. Refresh the menu and try again." });
            }

            var order = new FoodOrder
            {
                UserId = int.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
                StudentUsername = context.User.Identity!.Name!,
                Status = "Preparing",
                CreatedAt = DateTime.UtcNow,
                Items = requestedItems.Select(item =>
                {
                    var product = products[item.ProductId];
                    product.Stock -= item.Quantity;
                    return new FoodOrderItem
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Quantity = item.Quantity,
                        UnitPrice = (double)product.Price,
                        LineTotal = (double)(product.Price * item.Quantity)
                    };
                }).ToList()
            };
            order.Total = order.Items.Sum(item => item.LineTotal);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Created($"/api/orders/{order.Id}", new
            {
                order.Id,
                order.Total,
                order.Status,
                order.CreatedAt
            });
        })
            .RequireAuthorization();

        app.MapGet("/api/admin/dashboard", async (AppDbContext db) =>
        {
            var today = DateTime.UtcNow.Date;
            var orders = await db.Orders
                .OrderByDescending(order => order.CreatedAt)
                .Select(order => new
                {
                    order.Id,
                    order.StudentUsername,
                    order.Total,
                    order.Status,
                    order.CreatedAt,
                    Items = db.OrderItems.Where(item => item.OrderId == order.Id)
                        .Select(item => new { item.ProductName, item.Quantity, item.UnitPrice })
                        .ToList()
                })
                .ToListAsync();
            var products = await db.Products.OrderBy(product => product.Name).ToListAsync();

            return Results.Ok(new
            {
                products,
                orders,
                metrics = new
                {
                    todaySales = orders.Where(order => order.CreatedAt.Date == today && order.Status != "Cancelled").Sum(order => order.Total),
                    todayOrders = orders.Count(order => order.CreatedAt.Date == today && order.Status != "Cancelled"),
                    activeProducts = products.Count(product => product.IsAvailable),
                    lowStock = products.Count(product => product.Stock <= 5)
                }
            });
        })
            .RequireAuthorization("AdminOnly");

        app.MapPost("/api/admin/products", async (
            ProductRequest request,
            AppDbContext db) =>
        {
            var error = ValidateProduct(request);
            if (error is not null) return Results.BadRequest(new { message = error });

            var product = new Product();
            ApplyProduct(product, request);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/products/{product.Id}", product);
        })
            .RequireAuthorization("AdminOnly");

        app.MapPut("/api/admin/products/{id:int}", async (
            int id,
            ProductRequest request,
            AppDbContext db) =>
        {
            var error = ValidateProduct(request);
            if (error is not null) return Results.BadRequest(new { message = error });

            var product = await db.Products.FindAsync(id);
            if (product is null) return Results.NotFound(new { message = "Product not found." });
            ApplyProduct(product, request);
            await db.SaveChangesAsync();
            return Results.Ok(product);
        })
            .RequireAuthorization("AdminOnly");

        app.MapPost("/api/admin/orders", async (
            AdminOrderRequest request,
            HttpContext context,
            AppDbContext db) =>
        {
            if (request.ProductId <= 0 || request.Quantity <= 0 || request.Quantity > 99)
                return Results.BadRequest(new { message = "Choose a product and a quantity from 1 to 99." });
            if (request.CustomerName?.Trim().Length > 80)
                return Results.BadRequest(new { message = "Customer name must be 80 characters or fewer." });

            await using var transaction = await db.Database.BeginTransactionAsync();
            var product = await db.Products.FindAsync(request.ProductId);
            if (product is null || !product.IsAvailable || product.Stock < request.Quantity)
                return Results.Conflict(new { message = "That product is unavailable or does not have enough stock." });

            product.Stock -= request.Quantity;
            var order = new FoodOrder
            {
                UserId = int.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
                StudentUsername = string.IsNullOrWhiteSpace(request.CustomerName)
                    ? "Walk-in"
                    : request.CustomerName.Trim(),
                Total = (double)(product.Price * request.Quantity),
                Status = "Completed",
                CreatedAt = DateTime.UtcNow,
                Items =
                [
                    new FoodOrderItem
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Quantity = request.Quantity,
                        UnitPrice = (double)product.Price,
                        LineTotal = (double)(product.Price * request.Quantity)
                    }
                ]
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Created($"/api/orders/{order.Id}", new
            {
                order.Id,
                order.Total,
                order.Status,
                order.CreatedAt
            });
        })
            .RequireAuthorization("AdminOnly");

        app.MapPut("/api/admin/orders/{id:int}/status", async (
            int id,
            OrderStatusRequest request,
            AppDbContext db) =>
        {
            var allowedStatuses = new[] { "Preparing", "Ready", "Completed", "Cancelled" };
            if (request.Status is null || !allowedStatuses.Contains(request.Status))
                return Results.BadRequest(new { message = "Choose a valid order status." });

            var order = await db.Orders.Include(entry => entry.Items)
                .SingleOrDefaultAsync(entry => entry.Id == id);
            if (order is null) return Results.NotFound(new { message = "Order not found." });
            if (order.Status == "Cancelled" && request.Status != "Cancelled")
                return Results.Conflict(new { message = "Cancelled orders cannot be reopened." });

            if (request.Status == "Cancelled" && order.Status != "Cancelled")
            {
                foreach (var item in order.Items)
                {
                    var product = await db.Products.FindAsync(item.ProductId);
                    if (product is not null) product.Stock += item.Quantity;
                }
            }
            order.Status = request.Status;
            await db.SaveChangesAsync();
            return Results.Ok(new { order.Id, order.Status });
        })
            .RequireAuthorization("AdminOnly");

        app.Run();
    }

    private static string? NormalizeSchoolEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToLowerInvariant();
        try
        {
            var parsed = new MailAddress(normalized);
            if (!string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
                return null;
        }
        catch (FormatException)
        {
            return null;
        }

        return normalized.EndsWith("@lifeinstitute.edu.ph", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : null;
    }

    private static string? ValidateProduct(ProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80)
            return "Product name is required and must be 80 characters or fewer.";
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 240)
            return "Description is required and must be 240 characters or fewer.";
        if (request.Price <= 0 || request.Price > 100000)
            return "Enter a valid price greater than zero.";
        if (request.Category is not ("meals" or "snacks" or "drinks"))
            return "Choose meals, snacks, or drinks as the category.";
        if (string.IsNullOrWhiteSpace(request.Icon) || request.Icon.Length > 8)
            return "Enter a product icon.";
        if (request.Stock < 0 || request.Stock > 100000)
            return "Stock must be between 0 and 100000.";
        return null;
    }

    private static void ApplyProduct(Product product, ProductRequest request)
    {
        product.Name = request.Name!.Trim();
        product.Description = request.Description!.Trim();
        product.Price = decimal.Round(request.Price, 2);
        product.Category = request.Category!;
        product.Icon = request.Icon!.Trim();
        product.Stock = request.Stock;
        product.IsAvailable = request.IsAvailable;
    }

// Adds a SQLite column only when that column is not already present.
private static async Task EnsureSqliteColumnAsync(
    DbConnection connection,
    string tableName,
    string columnName,
    string columnDefinition)
{
    var columnExists = false;

    await using (var checkCommand = connection.CreateCommand())
    {
        checkCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await checkCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (string.Equals(
                reader.GetString(1),
                columnName,
                StringComparison.OrdinalIgnoreCase))
            {
                columnExists = true;
                break;
            }
        }
    }

    if (!columnExists)
    {
        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText =
            $"ALTER TABLE {tableName} ADD COLUMN {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync();
    }
}

}

// Defines the login data received from JavaScript.
public record LoginRequest(
    string? Username,
    string? Password
);

public record RegisterRequest(
    string? Username,
    string? Password,
    string? Email
);

public record EmailVerificationRequest(string? Email);

public record EmailVerificationCompleteRequest(string? Code);

public record PasswordResetRequest(string? Email);

public record PasswordResetCompleteRequest(
    string? Email,
    string? Code,
    string? NewPassword
);

public record OrderLineRequest(int ProductId, int Quantity);
public record OrderRequest(List<OrderLineRequest>? Items);
public record AdminOrderRequest(int ProductId, int Quantity, string? CustomerName);
public record OrderStatusRequest(string? Status);
public record ProductRequest(
    string? Name,
    string? Description,
    decimal Price,
    string? Category,
    string? Icon,
    int Stock,
    bool IsAvailable
);

// Defines the columns in the Users table.
public class AppUser
{
    public int Id { get; set; }

    public string Username { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string Role { get; set; } = "User";

    public string? Email { get; set; }

    public bool EmailVerified { get; set; } = false;

    public string? PendingEmail { get; set; }

    public string? EmailVerificationCodeHash { get; set; }

    public DateTime? EmailVerificationCodeExpiresAt { get; set; }

    public int EmailVerificationCodeAttempts { get; set; } = 0;

    public DateTime? EmailVerificationRequestedAt { get; set; }

    public string? PasswordResetCodeHash { get; set; }

    public DateTime? PasswordResetCodeExpiresAt { get; set; }

    public int PasswordResetCodeAttempts { get; set; } = 0;

    public DateTime? PasswordResetRequestedAt { get; set; }
}

// Defines the database and its tables.
public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options
    ) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<FoodOrder> Orders => Set<FoodOrder>();
    public DbSet<FoodOrderItem> OrderItems => Set<FoodOrderItem>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        // Prevent duplicate usernames.
        modelBuilder.Entity<AppUser>()
            .HasIndex(user => user.Username)
            .IsUnique();
        modelBuilder.Entity<AppUser>()
            .HasIndex(user => user.Email)
            .IsUnique()
            .HasFilter("\"Email\" IS NOT NULL AND \"Email\" <> ''");
        modelBuilder.Entity<FoodOrder>()
            .HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(item => item.OrderId);
    }
}

public class FoodOrder
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string StudentUsername { get; set; } = "";
    public double Total { get; set; }
    public string Status { get; set; } = "Preparing";
    public DateTime CreatedAt { get; set; }
    public List<FoodOrderItem> Items { get; set; } = [];
}

public class FoodOrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public double UnitPrice { get; set; }
    public double LineTotal { get; set; }
}

