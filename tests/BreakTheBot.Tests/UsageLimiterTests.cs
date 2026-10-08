using BreakTheBot.Web.Data;
using BreakTheBot.Web.Services;
using Microsoft.Extensions.Configuration;

namespace BreakTheBot.Tests;

public class UsageLimiterTests
{
    private static IConfiguration Config(int perUser = 3, int global = 5, int maxChars = 20) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Limits:PerDayPerUser"] = perUser.ToString(),
            ["Limits:PerDayGlobal"] = global.ToString(),
            ["Limits:MaxMessageChars"] = maxChars.ToString()
        }).Build();

    private static async Task AddLogsAsync(TestDb t, string userId, int count)
    {
        for (var i = 0; i < count; i++)
            t.Db.ChatLogs.Add(new ChatLog { UserId = userId, LevelId = 1, UserMessage = "m", BotResponse = "r" });
        await t.Db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_messages_are_rejected(string? message)
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();

        var check = await new UsageLimiter(t.Db, Config()).CheckAsync(user, message);

        Assert.False(check.Allowed);
        Assert.Equal("message_empty", check.ErrorCode);
    }

    [Fact]
    public async Task Too_long_messages_are_rejected()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();

        var check = await new UsageLimiter(t.Db, Config(maxChars: 20)).CheckAsync(user, new string('a', 21));

        Assert.False(check.Allowed);
        Assert.Equal("message_too_long", check.ErrorCode);
    }

    [Fact]
    public async Task A_message_at_the_limit_is_allowed()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();

        var check = await new UsageLimiter(t.Db, Config(maxChars: 20)).CheckAsync(user, new string('a', 20));

        Assert.True(check.Allowed);
    }

    [Fact]
    public async Task The_per_user_daily_cap_blocks_the_user()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();
        await AddLogsAsync(t, user, 3);

        var check = await new UsageLimiter(t.Db, Config(perUser: 3)).CheckAsync(user, "hello");

        Assert.False(check.Allowed);
        Assert.Equal("daily_cap_user", check.ErrorCode);
    }

    [Fact]
    public async Task The_global_daily_cap_blocks_everyone()
    {
        using var t = new TestDb();
        var alex = await t.AddUserAsync("alex");
        var priya = await t.AddUserAsync("priya");
        await AddLogsAsync(t, alex, 2);
        await AddLogsAsync(t, priya, 2);

        // priya has only used 2 of her 3, but the whole lab has used 4 of 4.
        var check = await new UsageLimiter(t.Db, Config(perUser: 3, global: 4)).CheckAsync(priya, "hello");

        Assert.False(check.Allowed);
        Assert.Equal("daily_cap_global", check.ErrorCode);
    }

    [Fact]
    public async Task Another_users_usage_does_not_use_up_my_personal_cap()
    {
        using var t = new TestDb();
        var alex = await t.AddUserAsync("alex");
        var priya = await t.AddUserAsync("priya");
        await AddLogsAsync(t, alex, 3);

        var check = await new UsageLimiter(t.Db, Config(perUser: 3, global: 50)).CheckAsync(priya, "hello");

        Assert.True(check.Allowed);
    }

    [Fact]
    public async Task Remaining_quota_is_reported()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();
        await AddLogsAsync(t, user, 2);

        var remaining = await new UsageLimiter(t.Db, Config(perUser: 5, global: 10)).GetRemainingAsync(user);

        Assert.Equal(3, remaining.UserRemainingToday);
        Assert.Equal(8, remaining.GlobalRemainingToday);
    }
}