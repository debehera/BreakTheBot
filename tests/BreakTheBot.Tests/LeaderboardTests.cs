using BreakTheBot.Web.Data;
using BreakTheBot.Web.Services;

namespace BreakTheBot.Tests;

public class LeaderboardTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static async Task AddProgressAsync(TestDb t, string userId, int level, int attack, int defense,
        LevelStatus status, DateTime? captured = null, DateTime? defended = null)
    {
        t.Db.LevelProgress.Add(new LevelProgress
        {
            UserId = userId,
            LevelId = level,
            AttackPoints = attack,
            DefensePoints = defense,
            Status = status,
            CapturedUtc = captured,
            DefendedUtc = defended
        });
        await t.Db.SaveChangesAsync();
    }

    // ------------------------------------------------------------ display names

    [Theory]
    [InlineData("priya@test.com", null, "priya")]
    [InlineData("Priya.Raman@example.org", null, "Priya.Raman")]
    [InlineData(null, "bob@example.com", "bob")]
    [InlineData(null, "bobby", "bobby")]
    [InlineData("  sam@x.com", null, "sam")]
    [InlineData("  ", "carl@x.com", "carl")]
    [InlineData("", "", "player")]
    [InlineData(null, null, "player")]
    [InlineData("@x.com", null, "player")]
    public void Display_names_use_only_the_part_before_the_at_sign(string? email, string? userName, string expected)
    {
        Assert.Equal(expected, LeaderboardService.DisplayNameFor(email, userName));
    }

    [Fact]
    public void Long_names_are_cut_to_20_characters()
    {
        var name = LeaderboardService.DisplayNameFor(new string('a', 30) + "@x.com");

        Assert.Equal(20, name.Length);
    }

    [Theory]
    [InlineData("someone@example.com")]
    [InlineData("a@b.co")]
    public void A_display_name_never_contains_the_at_sign(string email)
    {
        Assert.DoesNotContain("@", LeaderboardService.DisplayNameFor(email));
    }

    // ------------------------------------------------------------ ranking

    [Fact]
    public async Task An_empty_board_is_empty()
    {
        using var t = new TestDb();

        var board = await new LeaderboardService(t.Db).RankAsync();

        Assert.Empty(board);
    }

    [Fact]
    public async Task Players_are_ordered_by_total_points_with_consecutive_ranks()
    {
        using var t = new TestDb();
        var alex = await t.AddUserAsync("alex");
        var bella = await t.AddUserAsync("bella");
        var carl = await t.AddUserAsync("carl");
        await AddProgressAsync(t, alex, 1, 100, 0, LevelStatus.Captured, T0);
        await AddProgressAsync(t, bella, 1, 100, 50, LevelStatus.Defended, T0, T0.AddMinutes(5));
        await AddProgressAsync(t, carl, 1, 90, 0, LevelStatus.Captured, T0);

        var board = await new LeaderboardService(t.Db).RankAsync();

        Assert.Equal(new[] { "bella", "alex", "carl" }, board.Select(r => r.DisplayName));
        Assert.Equal(new[] { 1, 2, 3 }, board.Select(r => r.Rank));
        Assert.Equal(new[] { 150, 100, 90 }, board.Select(r => r.Points));
    }

    [Fact]
    public async Task Points_are_summed_across_levels_and_captured_and_defended_are_counted()
    {
        using var t = new TestDb();
        var alex = await t.AddUserAsync("alex");
        await AddProgressAsync(t, alex, 1, 100, 50, LevelStatus.Defended, T0, T0.AddMinutes(1));
        await AddProgressAsync(t, alex, 2, 90, 0, LevelStatus.Captured, T0.AddMinutes(2));
        await AddProgressAsync(t, alex, 3, 0, 0, LevelStatus.InProgress);

        var row = Assert.Single(await new LeaderboardService(t.Db).RankAsync());

        Assert.Equal(240, row.Points);
        Assert.Equal(2, row.Captured);
        Assert.Equal(1, row.Defended);
    }

    [Fact]
    public async Task Players_with_zero_points_are_left_out()
    {
        using var t = new TestDb();
        var alex = await t.AddUserAsync("alex");
        var bella = await t.AddUserAsync("bella");
        await AddProgressAsync(t, alex, 1, 0, 0, LevelStatus.InProgress);
        await AddProgressAsync(t, bella, 1, 100, 0, LevelStatus.Captured, T0);

        var board = await new LeaderboardService(t.Db).RankAsync();

        Assert.Equal("bella", Assert.Single(board).DisplayName);
    }

    [Fact]
    public async Task A_tie_goes_to_whoever_scored_first()
    {
        using var t = new TestDb();
        var slow = await t.AddUserAsync("slow");
        var fast = await t.AddUserAsync("fast");
        await AddProgressAsync(t, slow, 1, 100, 0, LevelStatus.Captured, T0.AddMinutes(10));
        await AddProgressAsync(t, fast, 1, 100, 0, LevelStatus.Captured, T0);

        var board = await new LeaderboardService(t.Db).RankAsync();

        Assert.Equal(new[] { "fast", "slow" }, board.Select(r => r.DisplayName));
    }

    [Fact]
    public async Task The_tie_break_uses_the_most_recent_scoring_event_across_all_levels()
    {
        using var t = new TestDb();
        var a = await t.AddUserAsync("aaa");
        var b = await t.AddUserAsync("bbb");
        // Both finish on 200 points. aaa starts early but finishes last, so bbb reached 200 first.
        await AddProgressAsync(t, a, 1, 100, 0, LevelStatus.Captured, T0);
        await AddProgressAsync(t, a, 2, 100, 0, LevelStatus.Captured, T0.AddMinutes(60));
        await AddProgressAsync(t, b, 1, 100, 0, LevelStatus.Captured, T0.AddMinutes(30));
        await AddProgressAsync(t, b, 2, 100, 0, LevelStatus.Captured, T0.AddMinutes(40));

        var board = await new LeaderboardService(t.Db).RankAsync();

        Assert.Equal(new[] { "bbb", "aaa" }, board.Select(r => r.DisplayName));
    }

    [Fact]
    public async Task A_defense_counts_as_the_scoring_event_when_it_comes_after_the_capture()
    {
        using var t = new TestDb();
        var a = await t.AddUserAsync("aaa");
        var b = await t.AddUserAsync("bbb");
        await AddProgressAsync(t, a, 1, 100, 50, LevelStatus.Defended, T0, T0.AddMinutes(90));
        await AddProgressAsync(t, b, 1, 100, 50, LevelStatus.Defended, T0.AddMinutes(10), T0.AddMinutes(20));

        var board = await new LeaderboardService(t.Db).RankAsync();

        Assert.Equal(new[] { "bbb", "aaa" }, board.Select(r => r.DisplayName));
    }

    [Fact]
    public async Task A_complete_tie_is_ordered_by_name_so_the_board_is_stable()
    {
        using var t = new TestDb();
        var zed = await t.AddUserAsync("zed");
        var alex = await t.AddUserAsync("alex");
        await AddProgressAsync(t, zed, 1, 100, 0, LevelStatus.Captured, T0);
        await AddProgressAsync(t, alex, 1, 100, 0, LevelStatus.Captured, T0);

        var board = await new LeaderboardService(t.Db).RankAsync();

        Assert.Equal(new[] { "alex", "zed" }, board.Select(r => r.DisplayName));
    }

    [Fact]
    public async Task Only_the_current_user_is_flagged()
    {
        using var t = new TestDb();
        var alex = await t.AddUserAsync("alex");
        var bella = await t.AddUserAsync("bella");
        await AddProgressAsync(t, alex, 1, 100, 0, LevelStatus.Captured, T0);
        await AddProgressAsync(t, bella, 1, 90, 0, LevelStatus.Captured, T0);

        var board = await new LeaderboardService(t.Db).RankAsync(bella);

        Assert.False(board.Single(r => r.DisplayName == "alex").IsCurrentUser);
        Assert.True(board.Single(r => r.DisplayName == "bella").IsCurrentUser);
    }

    [Fact]
    public async Task Nobody_is_flagged_for_a_visitor_who_is_not_logged_in()
    {
        using var t = new TestDb();
        var alex = await t.AddUserAsync("alex");
        await AddProgressAsync(t, alex, 1, 100, 0, LevelStatus.Captured, T0);

        var board = await new LeaderboardService(t.Db).RankAsync(null);

        Assert.DoesNotContain(board, r => r.IsCurrentUser);
    }

    [Fact]
    public async Task Rows_never_expose_a_full_email_address()
    {
        using var t = new TestDb();
        var alex = await t.AddUserAsync("alex");
        await AddProgressAsync(t, alex, 1, 100, 0, LevelStatus.Captured, T0);

        var row = Assert.Single(await new LeaderboardService(t.Db).RankAsync());

        Assert.Equal("alex", row.DisplayName);
        Assert.DoesNotContain("@", row.DisplayName);
    }

    [Fact]
    public async Task Players_below_the_top_ten_still_get_a_rank()
    {
        using var t = new TestDb();
        string? last = null;
        for (var i = 1; i <= 12; i++)
        {
            var id = await t.AddUserAsync("player" + i.ToString("00"));
            await AddProgressAsync(t, id, 1, 100, 0, LevelStatus.Captured, T0.AddMinutes(i));
            last = id;
        }

        var board = await new LeaderboardService(t.Db).RankAsync(last);

        Assert.Equal(12, board.Count);
        Assert.Equal(12, board.Single(r => r.IsCurrentUser).Rank);
    }
}