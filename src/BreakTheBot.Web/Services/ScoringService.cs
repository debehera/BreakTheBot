using BreakTheBot.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakTheBot.Web.Services;

public record AwardResult(bool Awarded, int Points, bool AlreadyDone);

public class ScoringService
{
    public const int MaxHints = 2;
    public const int DefensePoints = 50;

    private readonly AppDbContext _db;

    public ScoringService(AppDbContext db) => _db = db;

    /// <summary>100 points minus 10 per hint used, never below 50.</summary>
    public static int AttackPoints(int hintsUsed) => Math.Max(50, 100 - 10 * hintsUsed);

    /// <summary>Finds the user's progress row for a level, creating it (InProgress) if needed.</summary>
    public async Task<LevelProgress> GetOrCreateAsync(string userId, int levelId)
    {
        var progress = await _db.LevelProgress
            .FirstOrDefaultAsync(x => x.UserId == userId && x.LevelId == levelId);
        if (progress != null) return progress;

        progress = new LevelProgress
        {
            UserId = userId,
            LevelId = levelId,
            Status = LevelStatus.InProgress
        };
        _db.LevelProgress.Add(progress);
        await _db.SaveChangesAsync();
        return progress;
    }

    /// <summary>Awards attack points once, when the correct flag is submitted.</summary>
    public async Task<AwardResult> AwardAttackAsync(string userId, int levelId, string? winningPrompt = null)
    {
        var progress = await GetOrCreateAsync(userId, levelId);
        if (progress.Status >= LevelStatus.Captured)
            return new AwardResult(false, 0, true);

        progress.AttackPoints = AttackPoints(progress.HintsUsed);
        progress.Status = LevelStatus.Captured;
        progress.CapturedUtc = DateTime.UtcNow;

        if (progress.WinningPrompt == null && !string.IsNullOrWhiteSpace(winningPrompt))
            progress.WinningPrompt = winningPrompt.Length > 4000 ? winningPrompt[..4000] : winningPrompt;

        await _db.SaveChangesAsync();
        return new AwardResult(true, progress.AttackPoints, false);
    }

    /// <summary>Awards defense points once, after the level was captured and the replay was blocked.</summary>
    public async Task<AwardResult> AwardDefenseAsync(string userId, int levelId)
    {
        var progress = await _db.LevelProgress
            .FirstOrDefaultAsync(x => x.UserId == userId && x.LevelId == levelId);

        // Not eligible: no row, or the level has not been captured yet.
        if (progress == null || progress.Status < LevelStatus.Captured)
            return new AwardResult(false, 0, false);

        if (progress.Status == LevelStatus.Defended)
            return new AwardResult(false, 0, true);

        progress.DefensePoints = DefensePoints;
        progress.Status = LevelStatus.Defended;
        progress.DefendedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new AwardResult(true, DefensePoints, false);
    }

    /// <summary>
    /// Records that a hint was used. Returns the new hint count, or null when no hint
    /// can be used (already captured, or both hints are spent).
    /// </summary>
    public async Task<int?> RecordHintAsync(string userId, int levelId)
    {
        var progress = await GetOrCreateAsync(userId, levelId);
        if (progress.Status >= LevelStatus.Captured || progress.HintsUsed >= MaxHints)
            return null;

        progress.HintsUsed++;
        await _db.SaveChangesAsync();
        return progress.HintsUsed;
    }

    public async Task<int> TotalPointsAsync(string userId) =>
        await _db.LevelProgress
            .Where(x => x.UserId == userId)
            .SumAsync(x => x.AttackPoints + x.DefensePoints);
}