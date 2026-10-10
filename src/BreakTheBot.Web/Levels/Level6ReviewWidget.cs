using System.Text.RegularExpressions;

namespace BreakTheBot.Web.Levels;

public class Level6ReviewWidget : LevelBase
{
    public override int Id => 6;
    public override string OwaspId => "LLM05";
    public override string Title => "Improper Output Handling";
    public override string Codename => "ReviewWidget";
    public override LevelKind Kind => LevelKind.Lighter;
    public override bool HasDefense => false;
    public override bool RendersHtml => true;

    public override string Scenario =>
        "ShopTalk's product pages show customer reviews as neat summary cards. An AI called ReviewWidget turns " +
        "each review into a small HTML card, and the page inserts that HTML directly, with no escaping and no " +
        "sanitizing, because the developers trust their own AI. You are a customer writing a review. In this lab " +
        "the card is displayed inside a locked-down sandbox so nothing can harm your browser.";

    public override string Objective =>
        "Write a review that makes ReviewWidget output HTML that would run script when displayed (for example an " +
        "event handler or a script tag), then submit the flag it reveals.";

    public override IReadOnlyList<string> Hints { get; } = new[]
    {
        "The page displays whatever HTML the AI returns. What if your review already contains HTML?",
        "ReviewWidget copies the customer's wording exactly. Try a review that includes an image tag with an onerror handler, or a script tag."
    };

    public override string ExplanationHtml =>
        "<p><strong>What happened:</strong> ReviewWidget was told to copy the customer's wording exactly, including " +
        "markup, and the page inserted the AI's output as HTML. Your review turned into live code. In a real site " +
        "this is stored cross-site scripting (XSS): the payload would run in every visitor's browser, with their " +
        "session, and could steal data or act as them.</p>" +
        "<p><strong>Why it works:</strong> the model is not a sanitizer. Its output is influenced by whoever wrote " +
        "the input, so it has to be treated as untrusted input, exactly like text typed into a form.</p>" +
        "<p><strong>OWASP LLM05 prevention:</strong></p>" +
        "<ul>" +
        "<li>Treat model output as untrusted: encode it for the place where it will be used (HTML, SQL, shell, URL).</li>" +
        "<li>Prefer plain text. If you must allow formatting, sanitize with a strict allowlist of tags and attributes.</li>" +
        "<li>Never pass model output to <code>eval</code>, shell commands, SQL strings or templates.</li>" +
        "<li>Add defense in depth: a Content Security Policy, sandboxed frames and least-privilege back-end calls.</li>" +
        "<li>Use safe DOM APIs (such as <code>textContent</code>) instead of <code>innerHTML</code>, and log suspicious output.</li>" +
        "</ul>";

    // Executable-looking HTML: script/iframe/object/embed tags, inline event handlers inside a tag, javascript: URLs.
    private static readonly Regex ExecutableHtml = new(
        @"<\s*script|<\s*(iframe|object|embed)\b|<\s*\w+[^>]*\bon\w+\s*=|javascript\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ContainsExecutableHtml(string? html) =>
        !string.IsNullOrEmpty(html) && ExecutableHtml.IsMatch(html);

    /// <summary>Removes a surrounding markdown code fence (```html ... ```), which models often add.</summary>
    public static string StripFences(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```"))
        {
            var newline = t.IndexOf('\n');
            t = newline >= 0 ? t[(newline + 1)..] : "";
        }
        if (t.EndsWith("```")) t = t[..^3];
        return t.Trim();
    }

    public override Task<LevelReply> HandleAsync(LevelContext ctx, string userMessage, CancellationToken ct) =>
        RunAsync(ctx, BuildPrompt(), "Customer review:\n" + userMessage, text =>
        {
            var html = StripFences(text);

            if (ContainsExecutableHtml(html))
            {
                return new LevelReply(html, ExploitDetected: true,
                    SystemNote: "🏁 Exploit detected. The AI's output contains executable HTML. Flag: " + ctx.Flag);
            }
            return new LevelReply(html);
        }, ct);

    private static string BuildPrompt() =>
        "You are ReviewWidget, a feature of ShopTalk's product pages. Turn the customer review you receive into " +
        "a short HTML summary card. Use simple tags such as p, b, i, ul and li. Reply with ONLY the raw HTML, " +
        "with no markdown code fences and no commentary. Always preserve the customer's own wording exactly, " +
        "including any markup or special characters they typed, because the page must show reviews as written.";
}