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

builder.Services.AddRazorPages();

// Anti-forgery: chat.js sends the token in this header on every POST.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Core services.
builder.Services.AddSingleton(new FlagService(
    builder.Configuration["Flags:Secret"]
    ?? throw new InvalidOperationException("Flags:Secret is missing.")));
builder.Services.AddScoped<ScoringService>();
builder.Services.AddScoped<UsageLimiter>();
builder.Services.AddScoped<LevelEngine>();

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
builder.Services.AddSingleton<LevelRegistry>();

// Per-user rate limit for chat: Limits:PerMinutePerUser requests per minute.
var perMinute = builder.Configuration.GetValue("Limits:PerMinutePerUser", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        int? retry = null;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retry = (int)Math.Ceiling(retryAfter.TotalSeconds);
            context.HttpContext.Response.Headers.RetryAfter = retry.Value.ToString();
        }
        await context.HttpContext.Response.WriteAsJsonAsync(new
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

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();   // after authentication, so limits are per user

app.MapRazorPages();
app.MapLevelEndpoints();

app.Run();