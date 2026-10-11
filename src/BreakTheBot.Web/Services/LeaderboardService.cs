using BreakTheBot.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakTheBot.Web.Services;

public record LeaderboardRow(
    int Rank,
    string DisplayName,
    int Points,
    int Captured,
    int Defended,
    DateTime? LastScoreUtc,
    bool IsCurrentUser);

/// <summary>Ranks players by total points. Ties go to whoever scored first.</summary>
public class LeaderboardService
{
    public const int MaxNameLength = 20;

    private readonly AppDbContext _db;

    public LeaderboardService(AppDbContext db) => _db = db;

    /// <summary>The public name: the part of the email before the @ (never the full address).</summary>
    public static string DisplayNameFor(string? email, string? userName = null)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        if (string.IsNullOrWhiteSpace(source)) return "player";

        var at = source.IndexOf('@');
        var name = (at >= 0 ? source[..at] : source).Trim();
        if (name.Length == 0) return "player";

        return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
    }

    /// <summary>The full ranking. The page shows the top 10 and uses the rest to find "your rank".</summary>
    public async Task<List<LeaderboardRow>> RankAsync(string? currentUserId = null, CancellationToken ct = default)
    {
        var rows = await (
                from p in _db.LevelProgress
                join u in _db.Users on p.UserId equals u.Id
                where p.AttackPoints + p.DefensePoints > 0
                select new
                {
                    p.UserId,
                    u.Email,
                    u.UserName,
                    p.AttackPoints,
                    p.DefensePoints,
                    p.Status,
                    p.CapturedUtc,
                    p.DefendedUtc
                })
            .AsNoTracking()
            .ToListAsync(ct);

        var ranked = rows
            .GroupBy(r => r.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Name = DisplayNameFor(g.First().Email, g.First().UserName),
                Points = g.Sum(r => r.AttackPoints + r.DefensePoints),
                Captured = g.Count(r => r.Status >= LevelStatus.Captured),
                Defended = g.Count(r => r.Status == LevelStatus.Defended),
                Last = g.Max(r => r.DefendedUtc ?? r.CapturedUtc)
            })
            .OrderByDescending(x => x.Points)
            .ThenBy(x => x.Last ?? DateTime.MaxValue)                 // earlier finisher wins a tie
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)    // then a stable, predictable order
            .ThenBy(x => x.UserId, StringComparer.Ordinal)
            .ToList();

        return ranked
            .Select((x, i) => new LeaderboardRow(
                i + 1, x.Name, x.Points, x.Captured, x.Defended, x.Last,
                currentUserId != null && x.UserId == currentUserId))
            .ToList();
    }
}