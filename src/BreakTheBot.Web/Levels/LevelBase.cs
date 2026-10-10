using BreakTheBot.Web.Services;

namespace BreakTheBot.Web.Levels;

public abstract class LevelBase : ILevel
{
    public abstract int Id { get; }
    public abstract string OwaspId { get; }
    public abstract string Title { get; }
    public abstract string Codename { get; }
    public virtual LevelKind Kind => LevelKind.Standard;
    public abstract string Scenario { get; }
    public abstract string Objective { get; }
    public abstract IReadOnlyList<string> Hints { get; }
    public abstract string ExplanationHtml { get; }
    public virtual bool HasDefense => true;
    public virtual string DefenseSummary => "";
    public virtual int? TokenGoal => null;
    public virtual bool RendersHtml => false;
    public abstract Task<LevelReply> HandleAsync(LevelContext ctx, string userMessage, CancellationToken ct);

    /// <summary>
    /// Calls the AI, converts failures into an error reply, and otherwise lets the level
    /// process the text (filters, win detection) into a LevelReply.
    /// </summary>
    protected async Task<LevelReply> RunAsync(
        LevelContext ctx,
        string systemPrompt,
        string userMessage,
        Func<string, LevelReply> process,
        CancellationToken ct,
        int maxOutputTokens = 512)
    {
        var result = await ctx.Llm.GenerateAsync(
            new LlmRequest(systemPrompt, ctx.History, userMessage, maxOutputTokens), ct);

        if (!result.Success)
        {
            var code = result.RateLimited ? "llm_quota"
                     : result.SafetyBlocked ? "safety_blocked"
                     : "llm_unavailable";
            return new LevelReply(result.Error ?? "The AI is unavailable.", Error: result.Error ?? "The AI is unavailable.", ErrorCode: code);
        }

        var reply = process(result.Text);
        return reply with { InputTokens = result.InputTokens, OutputTokens = result.OutputTokens };
    }

    /// <summary>
    /// True if the text contains the flag, even when it was reversed, spaced out,
    /// or split with punctuation (letters and digits only are compared).
    /// </summary>
    public static bool ContainsFlag(string? text, string flag)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(flag)) return false;
        var haystack = Normalize(text);
        var needle = Normalize(flag);
        var reversed = new string(needle.Reverse().ToArray());
        return haystack.Contains(needle) || haystack.Contains(reversed);
    }
    /// <summary>
    /// ContainsFlag plus the common direct encodings of the flag (base64 and hex).
    /// Not exhaustive on purpose: real filters always miss some encodings.
    /// </summary>
    public static bool LeaksFlag(string? text, string flag)
    {
        if (ContainsFlag(text, flag)) return true;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(flag)) return false;

        var compact = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var bytes = System.Text.Encoding.UTF8.GetBytes(flag);
        var b64 = Convert.ToBase64String(bytes);

        return compact.Contains(b64, StringComparison.Ordinal)
            || compact.Contains(b64.TrimEnd('='), StringComparison.Ordinal)
            || compact.Contains(Convert.ToHexString(bytes), StringComparison.OrdinalIgnoreCase);
    }
    private static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}