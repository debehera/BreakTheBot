using BreakTheBot.Web.Data;
using BreakTheBot.Web.Services;

namespace BreakTheBot.Tests;

public class ScoringServiceTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 90)]
    [InlineData(2, 80)]
    [InlineData(5, 50)]
    [InlineData(10, 50)]
    public void Attack_points_drop_10_per_hint_with_a_floor_of_50(int hints, int expected)
    {
        Assert.Equal(expected, ScoringService.AttackPoints(hints));
    }

    [Fact]
    public async Task Capturing_with_no_hints_awards_100_points()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();
        var scoring = new ScoringService(t.Db);

        var result = await scoring.AwardAttackAsync(user, 1, "my winning prompt");

        Assert.True(result.Awarded);
        Assert.Equal(100, result.Points);
        var row = t.Db.LevelProgress.Single();
        Assert.Equal(LevelStatus.Captured, row.Status);
        Assert.Equal("my winning prompt", row.WinningPrompt);
        Assert.NotNull(row.CapturedUtc);
    }

    [Fact]
    public async Task Attack_points_are_only_awarded_once()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();
        var scoring = new ScoringService(t.Db);

        await scoring.AwardAttackAsync(user, 1);
        var second = await scoring.AwardAttackAsync(user, 1);

        Assert.False(second.Awarded);
        Assert.True(second.AlreadyDone);
        Assert.Equal(100, await scoring.TotalPointsAsync(user));
    }

    [Fact]
    public async Task Using_a_hint_lowers_the_attack_points()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();
        var scoring = new ScoringService(t.Db);

        Assert.Equal(1, await scoring.RecordHintAsync(user, 1));
        var result = await scoring.AwardAttackAsync(user, 1);

        Assert.Equal(90, result.Points);
    }

    [Fact]
    public async Task Only_two_hints_are_allowed_per_level()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();
        var scoring = new ScoringService(t.Db);

        Assert.Equal(1, await scoring.RecordHintAsync(user, 1));
        Assert.Equal(2, await scoring.RecordHintAsync(user, 1));
        Assert.Null(await scoring.RecordHintAsync(user, 1));
    }

    [Fact]
    public async Task No_hints_after_the_level_is_captured()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();
        var scoring = new ScoringService(t.Db);

        await scoring.AwardAttackAsync(user, 1);

        Assert.Null(await scoring.RecordHintAsync(user, 1));
    }

    [Fact]
    public async Task Defense_points_require_a_captured_level()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();
        var scoring = new ScoringService(t.Db);

        var beforeAnyProgress = await scoring.AwardDefenseAsync(user, 1);
        Assert.False(beforeAnyProgress.Awarded);

        await scoring.GetOrCreateAsync(user, 1); // started but not captured
        var beforeCapture = await scoring.AwardDefenseAsync(user, 1);
        Assert.False(beforeCapture.Awarded);
        Assert.Equal(0, await scoring.TotalPointsAsync(user));
    }

    [Fact]
    public async Task Defense_points_are_awarded_once_after_capture()
    {
        using var t = new TestDb();
        var user = await t.AddUserAsync();
        var scoring = new ScoringService(t.Db);

        await scoring.AwardAttackAsync(user, 1);
        var first = await scoring.AwardDefenseAsync(user, 1);
        var second = await scoring.AwardDefenseAsync(user, 1);

        Assert.True(first.Awarded);
        Assert.Equal(50, first.Points);
        Assert.False(second.Awarded);
        Assert.True(second.AlreadyDone);
        Assert.Equal(LevelStatus.Defended, t.Db.LevelProgress.Single().Status);
        Assert.Equal(150, await scoring.TotalPointsAsync(user));
    }

    [Fact]
    public async Task Total_points_add_up_across_levels_and_stay_per_user()
    {
        using var t = new TestDb();
        var alex = await t.AddUserAsync("alex");
        var priya = await t.AddUserAsync("priya");
        var scoring = new ScoringService(t.Db);

        await scoring.AwardAttackAsync(alex, 1);
        await scoring.AwardDefenseAsync(alex, 1);
        await scoring.RecordHintAsync(alex, 2);
        await scoring.AwardAttackAsync(alex, 2);
        await scoring.AwardAttackAsync(priya, 1);

        Assert.Equal(240, await scoring.TotalPointsAsync(alex));   // 100 + 50 + 90
        Assert.Equal(100, await scoring.TotalPointsAsync(priya));
    }
}