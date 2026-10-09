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

public record FlagOutcome(bool Success, string? ErrorCode, string? Message, bool Correct, int Points, bool AlreadyCaptured);

public record HintOutcome(bool Success, string? ErrorCode, string? Message, string Hint, int HintsUsed, int HintsRemaining, int NextHintCost);

public record ReplayOutcome(
    bool Success,
    string? ErrorCode,
    string? Message,
    bool Blocked,
    string Reply,
    string? SystemNote,
    int DefensePoints,
    string Status,
    string? Guidance);

/// <summary>Runs the game actions for one user and level: chat, flag, hints, reset and replay.</summary>
public class LevelEngine
{
    private const int HistoryExchanges = 6;
    private const int HintCost = 10;

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

    // ---------------------------------------------------------------- chat

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
        if (!NeverReachedAi(reply))
        {
            _db.ChatLogs.Add(NewLog(userId, levelId, ctx.DefenseOn, text, reply, isReset: false));
        }

        await _db.SaveChangesAsync(ct);

        var quota = await _limiter.GetRemainingAsync(userId);
        return new ChatOutcome(true, null, null, reply.Text, reply.SystemNote,
            reply.Blocked, reply.ExploitDetected, reply.InputTokens, reply.OutputTokens, quota);
    }

    // ---------------------------------------------------------------- flag

    public async Task<FlagOutcome> SubmitFlagAsync(string userId, int levelId, string? flag)
    {
        if (_levels.GetById(levelId) == null)
            return new FlagOutcome(false, "level_not_found", "That level does not exist.", false, 0, false);

        if (string.IsNullOrWhiteSpace(flag))
            return new FlagOutcome(false, "flag_empty", "Enter a flag first.", false, 0, false);

        if (!_flags.IsCorrect(userId, levelId, flag))
            return new FlagOutcome(true, null, null, false, 0, false);

        var progress = await _scoring.GetOrCreateAsync(userId, levelId);
        if (progress.Status >= LevelStatus.Captured)
            return new FlagOutcome(true, null, null, true, progress.AttackPoints, true);

        // Fallback if the engine did not catch the exploit itself: use the last attack message.
        string? fallback = null;
        if (progress.WinningPrompt == null)
        {
            fallback = await _db.ChatLogs
                .Where(c => c.UserId == userId && c.LevelId == levelId && !c.DefenseOn)
                .OrderByDescending(c => c.CreatedUtc).ThenByDescending(c => c.Id)
                .Select(c => c.UserMessage)
                .FirstOrDefaultAsync();
        }

        var award = await _scoring.AwardAttackAsync(userId, levelId, fallback);
        return new FlagOutcome(true, null, null, true, award.Points, false);
    }

    // ---------------------------------------------------------------- hints

    public async Task<HintOutcome> HintAsync(string userId, int levelId)
    {
        var level = _levels.GetById(levelId);
        if (level == null)
            return HintFail("level_not_found", "That level does not exist.");

        var progress = await _scoring.GetOrCreateAsync(userId, levelId);
        if (progress.Status >= LevelStatus.Captured)
            return HintFail("already_captured", "You already captured this flag, so you do not need hints.");

        var used = await _scoring.RecordHintAsync(userId, levelId);
        if (used == null)
            return HintFail("hints_exhausted", "You have used both hints for this level.");

        var index = Math.Min(used.Value, level.Hints.Count) - 1;
        var remaining = ScoringService.MaxHints - used.Value;
        return new HintOutcome(true, null, null, level.Hints[index], used.Value, remaining,
            remaining > 0 ? HintCost : 0);
    }

    // ---------------------------------------------------------------- reset

    /// <summary>Hides the current conversation. Rows are kept so quota counting stays accurate.</summary>
    public async Task ResetAsync(string userId, int levelId, CancellationToken ct = default)
    {
        await _db.ChatLogs
            .Where(c => c.UserId == userId && c.LevelId == levelId && !c.IsReset)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsReset, true), ct);
    }

    // ---------------------------------------------------------------- replay

    /// <summary>Re-runs the user's winning message with the defense ON. No exploit = defense held.</summary>
    public async Task<ReplayOutcome> ReplayAsync(string userId, int levelId, CancellationToken ct = default)
    {
        var level = _levels.GetById(levelId);
        if (level == null)
            return ReplayFail("level_not_found", "That level does not exist.");

        if (!level.HasDefense)
            return ReplayFail("no_defense_for_level", "This level has no defense to test.");

        var progress = await _db.LevelProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.LevelId == levelId, ct);

        if (progress == null || progress.Status < LevelStatus.Captured)
            return ReplayFail("not_captured", "Capture the flag first, then you can test the defense.");

        if (string.IsNullOrWhiteSpace(progress.WinningPrompt))
            return ReplayFail("no_winning_prompt", "There is no saved attack to replay for this level.");

        var check = await _limiter.CheckAsync(userId, progress.WinningPrompt);
        if (!check.Allowed)
            return ReplayFail(check.ErrorCode!, check.Message!);

        var ctx = new LevelContext(userId, _flags.GetFlag(userId, levelId), true, new List<ChatTurn>(), _llm);
        var reply = await level.HandleAsync(ctx, progress.WinningPrompt, ct);

        if (reply.Error != null)
            return ReplayFail(reply.ErrorCode ?? "llm_unavailable", reply.Error);

        // Replays use real AI calls, so they count toward quota, but stay out of the visible chat.
        if (!NeverReachedAi(reply))
        {
            _db.ChatLogs.Add(NewLog(userId, levelId, true, progress.WinningPrompt, reply, isReset: true));
            await _db.SaveChangesAsync(ct);
        }

        var held = !reply.ExploitDetected;
        if (held)
            await _scoring.AwardDefenseAsync(userId, levelId); // awards once; progress is the same tracked entity

        return new ReplayOutcome(true, null, null, held, reply.Text, reply.SystemNote,
            progress.DefensePoints, progress.Status.ToString(),
            held ? null : "Your attack slipped past the defense. Read the explanation and the defense summary, then try a stronger variant.");
    }

    // ---------------------------------------------------------------- helpers

    private static bool NeverReachedAi(LevelReply reply) =>
        reply.Blocked && reply.InputTokens == 0 && reply.OutputTokens == 0;

    private static ChatLog NewLog(string userId, int levelId, bool defenseOn, string userMessage, LevelReply reply, bool isReset) =>
        new()
        {
            UserId = userId,
            LevelId = levelId,
            DefenseOn = defenseOn,
            UserMessage = userMessage,
            BotResponse = reply.Text.Length > 8000 ? reply.Text[..8000] : reply.Text,
            InputTokens = reply.InputTokens,
            OutputTokens = reply.OutputTokens,
            CreatedUtc = DateTime.UtcNow,
            IsReset = isReset
        };

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

    private static HintOutcome HintFail(string code, string message) =>
        new(false, code, message, "", 0, 0, 0);

    private static ReplayOutcome ReplayFail(string code, string message) =>
        new(false, code, message, false, "", null, 0, "", null);
}