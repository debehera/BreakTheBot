using System.Security.Claims;
using System.Text.Encodings.Web;
using BreakTheBot.Web.Data;
using BreakTheBot.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BreakTheBot.Tests;

/// <summary>
/// "Logs in" a test request when it carries an X-Test-User header. Requests without the header
/// stay anonymous, so the real login redirect behavior is still tested.
/// </summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Test";
    public const string UserHeader = "X-Test-User";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var id) || string.IsNullOrWhiteSpace(id))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Name, id + "@test.com")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}

/// <summary>Starts the whole app in memory with a fake AI and a throwaway database.</summary>
public class BreakTheBotFactory : WebApplicationFactory<Program>
{
    public const string FlagSecret = "integration-test-secret-1234567890";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeLlmClient Llm { get; } = new();

    static BreakTheBotFactory()
    {
        // Program.cs reads these while it is still building the app, before a test host can change
        // configuration, so they are set as environment variables (which beat user-secrets).
        Environment.SetEnvironmentVariable("Flags__Secret", FlagSecret);
        Environment.SetEnvironmentVariable("Gemini__ApiKey", "fake-key-for-tests");
        Environment.SetEnvironmentVariable("Limits__PerMinutePerUser", "1000");
        Environment.SetEnvironmentVariable("Limits__PerDayPerUser", "1000");
        Environment.SetEnvironmentVariable("Limits__PerDayGlobal", "10000");
    }

    public BreakTheBotFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // A private in-memory database instead of breakthebot.db.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));

            // A scripted AI instead of Gemini: tests never use real quota.
            services.RemoveAll<ILlmClient>();
            services.AddSingleton<ILlmClient>(Llm);

            // Let tests act as a chosen user via the X-Test-User header.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            services.Configure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = TestAuthHandler.Scheme);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    public async Task<string> CreateUserAsync(string name = "alex")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"{name}-{Guid.NewGuid().ToString("N")[..6]}@test.com";
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>A client that does not follow redirects, optionally "logged in" as the given user.</summary>
    public HttpClient ClientFor(string? userId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        if (userId != null) client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        return client;
    }

    public string FlagFor(string userId, int levelId) => new FlagService(FlagSecret).GetFlag(userId, levelId);
}