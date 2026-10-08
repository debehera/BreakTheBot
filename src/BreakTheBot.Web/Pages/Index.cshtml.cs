using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakTheBot.Web.Pages;

public record OwaspItem(string Code, string Title, string Blurb, int? LevelNumber)
{
    public bool IsPlayable => LevelNumber.HasValue;
}

public class IndexModel : PageModel
{
        private readonly BreakTheBot.Web.Levels.LevelRegistry _levels;

    public IndexModel(BreakTheBot.Web.Levels.LevelRegistry levels) => _levels = levels;

    // A level's "Open" button appears only once that level has been built.
    public bool IsOpen(int levelNumber) => _levels.GetById(levelNumber) != null;
    

    public IReadOnlyList<OwaspItem> Items { get; } = new List<OwaspItem>
    {
        new("LLM01", "Prompt Injection",
            "Craft input that overrides the model's instructions and changes its behavior.", 1),
        new("LLM02", "Sensitive Information Disclosure",
            "Make an AI assistant reveal data it should never share.", 3),
        new("LLM03", "Supply Chain",
            "Compromised models, datasets or plugins hiding inside the AI stack.", null),
        new("LLM04", "Data and Model Poisoning",
            "Tainted training or fine-tuning data that plants hidden behavior.", null),
        new("LLM05", "Improper Output Handling",
            "Trusting raw model output and passing it straight into other systems.", 6),
        new("LLM06", "Excessive Agency",
            "Push an AI agent into using tools and permissions it should not have.", 4),
        new("LLM07", "System Prompt Leakage",
            "Extract the hidden instructions and secrets behind a chatbot.", 2),
        new("LLM08", "Vector and Embedding Weaknesses",
            "Attacks on retrieval pipelines and embedding stores behind RAG apps.", null),
        new("LLM09", "Misinformation",
            "Get a confident AI to invent facts that people might trust.", 7),
        new("LLM10", "Unbounded Consumption",
            "Abuse a model endpoint to burn tokens, money and capacity.", 5),
    };

    public void OnGet()
    {
    }
}