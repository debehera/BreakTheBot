namespace BreakTheBot.Web.Data;

public enum LevelStatus
{
    NotStarted = 0,
    InProgress = 1,
    Captured = 2,
    Defended = 3
}

public class LevelProgress
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int LevelId { get; set; }
    public LevelStatus Status { get; set; } = LevelStatus.NotStarted;
    public int HintsUsed { get; set; }
    public int AttackPoints { get; set; }
    public int DefensePoints { get; set; }
    public string? WinningPrompt { get; set; }
    public int Attempts { get; set; }
    public DateTime? CapturedUtc { get; set; }
    public DateTime? DefendedUtc { get; set; }
}