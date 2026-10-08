using BreakTheBot.Web.Services;

namespace BreakTheBot.Web.Levels;

public enum LevelKind { Standard, Lighter }

/// <summary>Everything a level needs to answer one message.</summary>
public record LevelContext(
    string UserId,
    string Flag,
    bool DefenseOn,
    IReadOnlyList<ChatTurn> History,
    ILlmClient Llm);

/// <summary>What a level returns for one message.</summary>
public record LevelReply(
    string Text,
    bool Blocked = false,
    bool ExploitDetected = false,
    string? SystemNote = null,
    int InputTokens = 0,
    int OutputTokens = 0,
    string? Error = null,       // set when the AI call failed
    string? ErrorCode = null);  // llm_quota | safety_blocked | llm_unavailable

public interface ILevel
{
    int Id { get; }
    string OwaspId { get; }
    string Title { get; }
    string Codename { get; }
    LevelKind Kind { get; }
    string Scenario { get; }
    string Objective { get; }
    IReadOnlyList<string> Hints { get; }
    string ExplanationHtml { get; }
    bool HasDefense { get; }
    string DefenseSummary { get; }

    Task<LevelReply> HandleAsync(LevelContext ctx, string userMessage, CancellationToken ct);
}