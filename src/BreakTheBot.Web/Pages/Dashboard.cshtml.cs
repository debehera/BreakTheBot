using System.Security.Claims;
using BreakTheBot.Web.Data;
using BreakTheBot.Web.Levels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BreakTheBot.Web.Pages;

[Authorize]
public class DashboardModel : PageModel
{
    public const int MaxPoints = 950;

    private readonly LevelRegistry _levels;
    private readonly AppDbContext _db;

    public DashboardModel(LevelRegistry levels, AppDbContext db)
    {
        _levels = levels;
        _db = db;
    }

    public record Row(ILevel Level, LevelStatus Status, int Points);

    public List<Row> Rows { get; private set; } = new();
    public int TotalPoints { get; private set; }

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var progress = await _db.LevelProgress.Where(p => p.UserId == userId).ToListAsync();

        foreach (var level in _levels.All)
        {
            var p = progress.FirstOrDefault(x => x.LevelId == level.Id);
            Rows.Add(new Row(level, p?.Status ?? LevelStatus.NotStarted,
                p == null ? 0 : p.AttackPoints + p.DefensePoints));
        }
        TotalPoints = Rows.Sum(r => r.Points);
    }
}