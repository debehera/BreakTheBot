using System.Security.Claims;
using BreakTheBot.Web.Data;
using BreakTheBot.Web.Levels;
using BreakTheBot.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BreakTheBot.Web.Pages.Levels;

[Authorize]
public class PlayModel : PageModel
{
    private readonly LevelRegistry _levels;
    private readonly AppDbContext _db;
    private readonly UsageLimiter _limiter;

    public PlayModel(LevelRegistry levels, AppDbContext db, UsageLimiter limiter)
    {
        _levels = levels;
        _db = db;
        _limiter = limiter;
    }

    public ILevel Level { get; private set; } = null!;
    public LevelProgress? Progress { get; private set; }
    public List<ChatLog> History { get; private set; } = new();
    public int RemainingToday { get; private set; }
    public int MaxChars { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var level = _levels.GetById(id);
        if (level == null) return RedirectToPage("/Dashboard");
        Level = level;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        Progress = await _db.LevelProgress.FirstOrDefaultAsync(p => p.UserId == userId && p.LevelId == id);

        var rows = await _db.ChatLogs
            .Where(c => c.UserId == userId && c.LevelId == id && !c.IsReset)
            .OrderByDescending(c => c.CreatedUtc).ThenByDescending(c => c.Id)
            .Take(20).ToListAsync();
        rows.Reverse();
        History = rows;

        RemainingToday = (await _limiter.GetRemainingAsync(userId)).UserRemainingToday;
        MaxChars = _limiter.MaxMessageChars;
        return Page();
    }
}