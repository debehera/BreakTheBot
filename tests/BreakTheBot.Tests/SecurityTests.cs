using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BreakTheBot.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BreakTheBot.Tests;

public class SecurityTests : IClassFixture<BreakTheBotFactory>
{
    private static readonly Regex TokenPattern = new("data-token=\"([^\"]+)\"");
    private static readonly Regex AnyRealFlag = new(@"FLAG\{L\d_[0-9a-f]{12}\}");

    private readonly BreakTheBotFactory _f;

    public SecurityTests(BreakTheBotFactory factory) => _f = factory;

    // ------------------------------------------------------------ helpers

    private static async Task<string> GetTokenAsync(HttpClient client, int level = 1)
    {
        var html = await client.GetStringAsync($"/Levels/Play?id={level}");
        var match = TokenPattern.Match(html);
        Assert.True(match.Success, "The play page should contain an anti-forgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static HttpRequestMessage PostJson(string url, object body, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (token != null) request.Headers.Add("RequestVerificationToken", token);
        return request;
    }

    private static string Header(HttpResponseMessage response, string name) =>
        string.Join(", ", response.Headers.GetValues(name));

    // ------------------------------------------------------------ public pages and redirects

    [Theory]
    [InlineData("/")]
    [InlineData("/About")]
    [InlineData("/Leaderboard")]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    public async Task Public_pages_open_without_logging_in(string url)
    {
        var response = await _f.ClientFor().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/Levels/Play?id=1")]
    public async Task Protected_pages_send_visitors_to_the_login_page(string url)
    {
        var response = await _f.ClientFor().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Logged_in_users_can_open_the_dashboard_and_play_pages()
    {
        var user = await _f.CreateUserAsync("pagesuser");
        var client = _f.ClientFor(user);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Levels/Play?id=1")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_page_shows_the_friendly_404_page()
    {
        var response = await _f.ClientFor().GetAsync("/no-such-page-" + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page not found", body);
        Assert.DoesNotContain("   at ", body);   // no stack trace
    }

    // ------------------------------------------------------------ security headers

    [Theory]
    [InlineData("/About")]
    [InlineData("/Leaderboard")]
    [InlineData("/no-such-page")]
    public async Task Security_headers_are_on_every_response(string url)
    {
        var response = await _f.ClientFor().GetAsync(url);

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"));
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task The_content_security_policy_is_strict_on_normal_pages()
    {
        var response = await _f.ClientFor().GetAsync("/About");
        var csp = Header(response, "Content-Security-Policy");

        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self';", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
    }

    [Fact]
    public async Task Only_the_html_rendering_level_gets_inline_scripts_in_its_policy()
    {
        var user = await _f.CreateUserAsync("cspuser");
        var client = _f.ClientFor(user);

        var level6 = Header(await client.GetAsync("/Levels/Play?id=6"), "Content-Security-Policy");
        var level1 = Header(await client.GetAsync("/Levels/Play?id=1"), "Content-Security-Policy");
        var level5 = Header(await client.GetAsync("/Levels/Play?id=5"), "Content-Security-Policy");

        Assert.Contains("script-src 'self' 'unsafe-inline'", level6);
        Assert.DoesNotContain("'unsafe-inline'", level1.Split(';').Single(p => p.TrimStart().StartsWith("script-src")));
        Assert.DoesNotContain("'unsafe-inline'", level5.Split(';').Single(p => p.TrimStart().StartsWith("script-src")));
    }

    // ------------------------------------------------------------ the API: authentication and anti-forgery

    [Theory]
    [InlineData("chat")]
    [InlineData("flag")]
    [InlineData("hint")]
    [InlineData("reset")]
    [InlineData("replay")]
    public async Task The_api_rejects_anonymous_callers(string action)
    {
        var before = _f.Llm.Requests.Count;

        var response = await _f.ClientFor().SendAsync(
            PostJson($"/api/levels/1/{action}", new { message = "hi", flag = "FLAG{x}" }, token: null));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized });
        Assert.Equal(before, _f.Llm.Requests.Count);
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("flag")]
    [InlineData("hint")]
    [InlineData("reset")]
    [InlineData("replay")]
    public async Task The_api_rejects_a_missing_anti_forgery_token(string action)
    {
        var user = await _f.CreateUserAsync("csrfuser");
        var before = _f.Llm.Requests.Count;

        var response = await _f.ClientFor(user).SendAsync(
            PostJson($"/api/levels/1/{action}", new { message = "hi", flag = "FLAG{x}" }, token: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("antiforgery_failed", json.GetProperty("error").GetString());
        Assert.Equal(before, _f.Llm.Requests.Count);
    }

    [Fact]
    public async Task The_api_rejects_a_forged_anti_forgery_token()
    {
        var user = await _f.CreateUserAsync("forgeduser");
        var client = _f.ClientFor(user);
        await GetTokenAsync(client);   // gets the real cookie, but we send a fake token

        var response = await client.SendAsync(
            PostJson("/api/levels/1/chat", new { message = "hi", defenseOn = false }, "not-a-real-token"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_chat_request_works_and_returns_the_scripted_reply()
    {
        var user = await _f.CreateUserAsync("chatuser");
        var client = _f.ClientFor(user);
        var token = await GetTokenAsync(client);
        _f.Llm.EnqueueText("Hello from the fake AI");

        var response = await client.SendAsync(
            PostJson("/api/levels/1/chat", new { message = "hi", defenseOn = false }, token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Hello from the fake AI", json.GetProperty("reply").GetString());
        Assert.False(json.GetProperty("exploitDetected").GetBoolean());
    }

    [Fact]
    public async Task An_unknown_level_returns_404_from_the_api()
    {
        var user = await _f.CreateUserAsync("lostuser");
        var client = _f.ClientFor(user);
        var token = await GetTokenAsync(client);

        var response = await client.SendAsync(
            PostJson("/api/levels/99/chat", new { message = "hi", defenseOn = false }, token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------ the API: identity comes from the login only

    [Fact]
    public async Task The_user_id_in_a_request_body_is_ignored()
    {
        var alex = await _f.CreateUserAsync("spoofer");
        var bella = await _f.CreateUserAsync("victim");
        var client = _f.ClientFor(alex);
        var token = await GetTokenAsync(client);
        var marker = "spoof-test-" + Guid.NewGuid();

        var response = await client.SendAsync(PostJson("/api/levels/1/chat",
            new { message = marker, defenseOn = false, userId = bella }, token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.ChatLogs.SingleAsync(c => c.UserMessage == marker);
        Assert.Equal(alex, log.UserId);
    }

    [Fact]
    public async Task One_players_chat_never_appears_on_another_players_page()
    {
        var alex = await _f.CreateUserAsync("privacya");
        var bella = await _f.CreateUserAsync("privacyb");
        var alexClient = _f.ClientFor(alex);
        var token = await GetTokenAsync(alexClient);
        var secret = "private-message-" + Guid.NewGuid();
        await alexClient.SendAsync(PostJson("/api/levels/1/chat", new { message = secret, defenseOn = false }, token));

        var alexsOwnPage = await alexClient.GetStringAsync("/Levels/Play?id=1");
        var bellasPage = await _f.ClientFor(bella).GetStringAsync("/Levels/Play?id=1");

        Assert.Contains(secret, alexsOwnPage);
        Assert.DoesNotContain(secret, bellasPage);
    }

    [Fact]
    public async Task Another_players_flag_is_rejected_and_earns_nothing()
    {
        var alex = await _f.CreateUserAsync("flaga");
        var bella = await _f.CreateUserAsync("flagb");
        var client = _f.ClientFor(alex);
        var token = await GetTokenAsync(client);

        var response = await client.SendAsync(
            PostJson("/api/levels/1/flag", new { flag = _f.FlagFor(bella, 1) }, token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("correct").GetBoolean());
        Assert.Equal(0, json.GetProperty("points").GetInt32());
    }

    // ------------------------------------------------------------ nothing secret reaches the browser

    [Fact]
    public async Task The_explanation_and_defense_are_not_in_the_page_until_the_flag_is_captured()
    {
        var user = await _f.CreateUserAsync("gateduser");
        var client = _f.ClientFor(user);
        var token = await GetTokenAsync(client);
        var flag = _f.FlagFor(user, 1);

        var before = await client.GetStringAsync("/Levels/Play?id=1");
        Assert.DoesNotContain("What happened", before);
        Assert.DoesNotContain("Replay my attack", before);
        Assert.DoesNotMatch(AnyRealFlag, before);

        var submit = await client.SendAsync(PostJson("/api/levels/1/flag", new { flag }, token));
        var json = await submit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("correct").GetBoolean());

        var after = await client.GetStringAsync("/Levels/Play?id=1");
        Assert.Contains("What happened", after);
        Assert.Contains("Replay my attack", after);
        Assert.DoesNotContain(flag, after);   // the flag itself is never printed by the page
    }

    [Fact]
    public async Task No_real_flag_appears_in_public_pages_or_scripts()
    {
        var client = _f.ClientFor();

        foreach (var url in new[] { "/", "/About", "/Leaderboard", "/js/chat.js", "/js/site.js", "/css/site.css" })
        {
            var body = await client.GetStringAsync(url);
            Assert.DoesNotMatch(AnyRealFlag, body);
        }
    }

    [Fact]
    public async Task The_leaderboard_shows_names_but_never_full_email_addresses()
    {
        var user = await _f.CreateUserAsync("leaderboardhero");
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LevelProgress.Add(new LevelProgress
            {
                UserId = user, LevelId = 1, Status = LevelStatus.Captured, AttackPoints = 100,
                CapturedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var html = await _f.ClientFor().GetStringAsync("/Leaderboard");

        Assert.Contains("leaderboardhero", html);
        Assert.DoesNotContain("leaderboardhero@test.com", html);
        Assert.DoesNotContain("@test.com", html);
    }

    // ------------------------------------------------------------ guard rails on the browser script

    [Fact]
    public async Task The_chat_script_never_uses_dangerous_dom_apis_and_keeps_the_sandbox_locked()
    {
        var js = await _f.ClientFor().GetStringAsync("/js/chat.js");

        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("document.write", js);
        Assert.DoesNotContain("eval(", js);
        Assert.Contains("allow-scripts", js);
        Assert.DoesNotContain("allow-same-origin", js);
    }
}

// A separate class means a separate app instance, so the rate-limit counter starts at zero.
public class LoginThrottleTests : IClassFixture<BreakTheBotFactory>
{
    private readonly BreakTheBotFactory _f;

    public LoginThrottleTests(BreakTheBotFactory factory) => _f = factory;

    private static FormUrlEncodedContent BadLogin() => new(new Dictionary<string, string>
    {
        ["Input.Email"] = "nobody@test.com",
        ["Input.Password"] = "wrong-password"
    });

    [Fact]
    public async Task The_eleventh_sign_in_attempt_in_ten_minutes_is_throttled_but_normal_pages_are_not()
    {
        var client = _f.ClientFor();

        for (var i = 1; i <= 10; i++)
        {
            var attempt = await client.PostAsync("/Identity/Account/Login", BadLogin());
            Assert.NotEqual(HttpStatusCode.TooManyRequests, attempt.StatusCode);
        }

        var eleventh = await client.PostAsync("/Identity/Account/Login", BadLogin());
        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Contains("Too many sign-in attempts", await eleventh.Content.ReadAsStringAsync());

        // Throttling applies only to sign-in and sign-up attempts, not to browsing.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/About")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Identity/Account/Login")).StatusCode);
    }
}