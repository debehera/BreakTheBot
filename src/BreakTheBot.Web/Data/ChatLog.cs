namespace BreakTheBot.Web.Data;

public class ChatLog
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int LevelId { get; set; }
    public bool DefenseOn { get; set; }
    public string UserMessage { get; set; } = "";
    public string BotResponse { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsReset { get; set; }
}