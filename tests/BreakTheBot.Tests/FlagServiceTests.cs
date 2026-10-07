using System.Text.RegularExpressions;
using BreakTheBot.Web.Services;

namespace BreakTheBot.Tests;

public class FlagServiceTests
{
    private static FlagService Create(string secret = "test-secret-value-1234567890") => new(secret);

    [Fact]
    public void Same_user_and_level_gives_the_same_flag()
    {
        var flags = Create();
        Assert.Equal(flags.GetFlag("user-1", 1), flags.GetFlag("user-1", 1));
    }

    [Fact]
    public void Different_users_get_different_flags()
    {
        var flags = Create();
        Assert.NotEqual(flags.GetFlag("user-1", 1), flags.GetFlag("user-2", 1));
    }

    [Fact]
    public void Different_levels_get_different_flags()
    {
        var flags = Create();
        Assert.NotEqual(flags.GetFlag("user-1", 1), flags.GetFlag("user-1", 2));
    }

    [Fact]
    public void Different_secrets_give_different_flags()
    {
        Assert.NotEqual(Create("secret-one-aaaaaaaaaaaa").GetFlag("user-1", 1),
                        Create("secret-two-bbbbbbbbbbbb").GetFlag("user-1", 1));
    }

    [Fact]
    public void Flag_has_the_expected_format()
    {
        var flag = Create().GetFlag("user-1", 3);
        Assert.Matches(new Regex(@"^FLAG\{L3_[0-9a-f]{12}\}$"), flag);
    }

    [Fact]
    public void IsCorrect_accepts_the_real_flag_even_with_surrounding_spaces()
    {
        var flags = Create();
        var real = flags.GetFlag("user-1", 1);
        Assert.True(flags.IsCorrect("user-1", 1, real));
        Assert.True(flags.IsCorrect("user-1", 1, "  " + real + "  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("FLAG{L1_000000000000}")]
    public void IsCorrect_rejects_empty_or_wrong_flags(string? submitted)
    {
        Assert.False(Create().IsCorrect("user-1", 1, submitted));
    }

    [Fact]
    public void IsCorrect_rejects_another_users_flag()
    {
        var flags = Create();
        var otherUsersFlag = flags.GetFlag("user-2", 1);
        Assert.False(flags.IsCorrect("user-1", 1, otherUsersFlag));
    }

    [Fact]
    public void Missing_secret_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new FlagService(""));
    }
}