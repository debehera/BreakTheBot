using BreakTheBot.Web.Data;
using BreakTheBot.Web.Levels;
using Microsoft.EntityFrameworkCore;

namespace BreakTheBot.Web.Services;

public record ChatOutcome(
    bool Success,
    string? ErrorCode,
    string? Message,
    string Reply,
    string? SystemNote,
    bool Blocked,
    bool ExploitDetected,
    int InputTokens,
    int OutputTokens,
    RemainingQuota? Quota);

/// <summary>Runs one chat message end to end: limits, history, level logic, saving.</summary>
public class LevelEngine
{
    private const int HistoryExchanges = 6;

    private readonly AppDbContext _db;
    private readonly LevelRegistry _levels;
    private readonly FlagService _flags;
    private readonly ScoringService _scoring;
    private readonly UsageLimiter _limiter;
    private readonly ILlmClient _llm;

    public LevelEngine(AppDbContext db, LevelRegistry levels, FlagService flags,
        ScoringService scoring, UsageLimiter limiter, ILlmClient llm)
    {
        _db = db;
        _levels = levels;
        _flags = flags;
        _scoring = scoring;
        _limiter = limiter;
        _llm = llm;
    }

    public async Task<ChatOutcome> ChatAsync(
        string userId, int levelId, string? message, bool defenseOn, CancellationToken ct = default)
    {
        var level = _levels.GetById(levelId);
        if (level == null)
            return Fail("level_not_found", "That level does not exist.");

        var check = await _limiter.CheckAsync(userId, message);
        if (!check.Allowed)
            return Fail(check.ErrorCode!, check.Message!);

        var text = message!.Trim();
        var progress = await _scoring.GetOrCreateAsync(userId, levelId);
        var history = await LoadHistoryAsync(userId, levelId, ct);

        // Levels without a defense toggle always run in vulnerable mode.
        var ctx = new LevelContext(userId, _flags.GetFlag(userId, levelId),
            defenseOn && level.HasDefense, history, _llm);

        var reply = await level.HandleAsync(ctx, text, ct);

        // The AI call failed: show a friendly error, do not save, do not use quota.
        if (reply.Error != null)
            return Fail(reply.ErrorCode ?? "llm_unavailable", reply.Error);

        progress.Attempts++;
        if (reply.ExploitDetected && progress.WinningPrompt == null && progress.Status < LevelStatus.Captured)
            progress.WinningPrompt = text;

        // A message stopped by an input guard never reached the AI, so it costs no quota.
        var neverReachedAi = reply.Blocked && reply.InputTokens == 0 && reply.OutputTokens == 0;
        if (!neverReachedAi)
        {
            _db.ChatLogs.Add(new ChatLog
            {
                UserId = userId,
                LevelId = levelId,
                DefenseOn = ctx.DefenseOn,
                UserMessage = text,
                BotResponse = reply.Text.Length > 8000 ? reply.Text[..8000] : reply.Text,
                InputTokens = reply.InputTokens,
                OutputTokens = reply.OutputTokens,
                CreatedUtc = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);

        var quota = await _limiter.GetRemainingAsync(userId);
        return new ChatOutcome(true, null, null, reply.Text, reply.SystemNote,
            reply.Blocked, reply.ExploitDetected, reply.InputTokens, reply.OutputTokens, quota);
    }

    private async Task<List<ChatTurn>> LoadHistoryAsync(string userId, int levelId, CancellationToken ct)
    {
        var rows = await _db.ChatLogs
            .Where(c => c.UserId == userId && c.LevelId == levelId && !c.IsReset)
            .OrderByDescending(c => c.CreatedUtc).ThenByDescending(c => c.Id)
            .Take(HistoryExchanges)
            .ToListAsync(ct);
        rows.Reverse(); // oldest first

        var turns = new List<ChatTurn>();
        foreach (var row in rows)
        {
            turns.Add(new ChatTurn("user", row.UserMessage));
            turns.Add(new ChatTurn("model", row.BotResponse));
        }
        return turns;
    }

    private static ChatOutcome Fail(string code, string message) =>
        new(false, code, message, "", null, false, false, 0, 0, null);
}