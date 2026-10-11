using System.Security.Claims;
using BreakTheBot.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakTheBot.Web.Pages;

public class LeaderboardModel : PageModel
{
    public const int TopCount = 10;

    private readonly LeaderboardService _leaderboard;

    public LeaderboardModel(LeaderboardService leaderboard) => _leaderboard = leaderboard;

    public List<LeaderboardRow> Top { get; private set; } = new();
    public LeaderboardRow? Me { get; private set; }
    public int TotalPlayers { get; private set; }

    public async Task OnGetAsync()
    {
        var userId = User.Identity?.IsAuthenticated == true
            ? User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        var all = await _leaderboard.RankAsync(userId);
        TotalPlayers = all.Count;
        Top = all.Take(TopCount).ToList();
        Me = all.FirstOrDefault(r => r.IsCurrentUser);
    }
}