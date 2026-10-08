namespace BreakTheBot.Web.Services;

/// <summary>One turn of conversation. Role is "user" or "model".</summary>
public record ChatTurn(string Role, string Text);

public record LlmRequest(
    string SystemPrompt,
    IReadOnlyList<ChatTurn> History,
    string UserMessage,
    int MaxOutputTokens = 512,
    double Temperature = 0.7);

public record LlmResult(
    bool Success,
    string Text,
    int InputTokens,
    int OutputTokens,
    string? Error = null,
    bool RateLimited = false,
    bool SafetyBlocked = false);

/// <summary>The only thing levels know about the AI. GeminiClient implements it.</summary>
public interface ILlmClient
{
    Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken ct = default);
}