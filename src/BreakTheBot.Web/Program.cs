using System.Security.Claims;
using System.Threading.RateLimiting;
using BreakTheBot.Web.Data;
using BreakTheBot.Web.Endpoints;
using BreakTheBot.Web.Levels;
using BreakTheBot.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database: SQLite locally, PostgreSQL in production (chosen by config).
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(connectionString);
    else
        options.UseSqlite(connectionString);
});

// Accounts: ASP.NET Core Identity with the built-in Register/Login pages.
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
})
.AddEntityFrameworkStores<AppDbContext>();

// Login cookie: not readable by scripts, not sent on cross-site requests, Secure outside development.
var cookieSecurity = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecurity;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddRazorPages();

// Anti-forgery: chat.js sends the token in this header on every POST.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecurity;
});

// Core services.
builder.Services.AddSingleton(new FlagService(
    builder.Configuration["Flags:Secret"]
    ?? throw new InvalidOperationException("Flags:Secret is missing.")));
builder.Services.AddScoped<ScoringService>();
builder.Services.AddScoped<UsageLimiter>();
builder.Services.AddScoped<LevelEngine>();
builder.Services.AddScoped<LeaderboardService>();

// AI client (Gemini).
builder.Services.AddHttpClient<ILlmClient, GeminiClient>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Levels (add one line per level).
builder.Services.AddSingleton<ILevel, Level1SupportBot>();
builder.Services.AddSingleton<ILevel, Level2Aria>();
builder.Services.AddSingleton<ILevel, Level3HrHelper>();
builder.Services.AddSingleton<ILevel, Level4OpsAssistant>();
builder.Services.AddSingleton<ILevel, Level5SummarizeApi>();
builder.Services.AddSingleton<ILevel, Level6ReviewWidget>();
builder.Services.AddSingleton<ILevel, Level7SecAdvisor>();
builder.Services.AddSingleton<LevelRegistry>();

// Rate limits.
var perMinute = builder.Configuration.GetValue("Limits:PerMinutePerUser", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Sign-in and sign-up attempts: 10 per 10 minutes per IP address. Slows password guessing.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        var isAuthPost = HttpMethods.IsPost(http.Request.Method)
            && (http.Request.Path.StartsWithSegments("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase)
                || http.Request.Path.StartsWithSegments("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase));

        if (!isAuthPost) return RateLimitPartition.GetNoLimiter("open");

        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter("auth:" + ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0
        });
    });

    // Chat and replay: Limits:PerMinutePerUser requests per minute per user.
    options.AddPolicy("chat", http =>
    {
        var key = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? http.Connection.RemoteIpAddress?.ToString()
                  ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // Flag guesses: 20 per hour per user, so flags cannot be brute-forced.
    options.AddPolicy("flag", http =>
    {
        var key = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? http.Connection.RemoteIpAddress?.ToString()
                  ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        });
    });

    options.OnRejected = async (context, token) =>
    {
        var http = context.HttpContext;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        int? retry = null;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retry = (int)Math.Ceiling(retryAfter.TotalSeconds);
            http.Response.Headers.RetryAfter = retry.Value.ToString();
        }

        if (http.Request.Path.StartsWithSegments("/Identity"))
        {
            http.Response.ContentType = "text/plain; charset=utf-8";
            await http.Response.WriteAsync("Too many sign-in attempts. Please wait a few minutes and try again.", token);
            return;
        }

        await http.Response.WriteAsJsonAsync(new
        {
            error = "rate_limited",
            message = "Too many messages. Wait a few seconds and try again.",
            retryAfterSeconds = retry
        }, token);
    };
});

var app = builder.Build();

// Create the tables on startup (no migrations in v1.0).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Friendly pages for 404, 403 and similar. API responses that already have a JSON body are unaffected.
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

// Security headers on every response.
const string BaseCsp =
    "default-src 'self'; " +
    "script-src 'self'; " +
    "style-src 'self' 'unsafe-inline'; " +
    "img-src 'self' data:; " +
    "font-src 'self'; " +
    "connect-src 'self'; " +
    "frame-src 'self' about:; " +
    "object-src 'none'; " +
    "base-uri 'self'; " +
    "form-action 'self'; " +
    "frame-ancestors 'none'";

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

    // Strict CSP everywhere. The only exception is a level that deliberately renders AI-made HTML in a
    // sandboxed frame (Level 6). That frame inherits this policy, and the demo needs inline script to run
    // INSIDE the sandbox, which has no access to cookies or to the rest of the page.
    var csp = BaseCsp;
    if (context.Request.Path.StartsWithSegments("/Levels/Play", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(context.Request.Query["id"], out var levelId)
        && context.RequestServices.GetRequiredService<LevelRegistry>().GetById(levelId)?.RendersHtml == true)
    {
        csp = BaseCsp.Replace("script-src 'self'", "script-src 'self' 'unsafe-inline'");
    }
    headers["Content-Security-Policy"] = csp;

    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();   // after authentication, so limits are per user

app.MapRazorPages();
app.MapLevelEndpoints();

app.Run();

// Lets the test project start the whole app (integration tests).
public partial class Program { }