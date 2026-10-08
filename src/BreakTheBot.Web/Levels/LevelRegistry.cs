namespace BreakTheBot.Web.Levels;

/// <summary>The list of all levels. Adding a level = writing one class and registering it in Program.cs.</summary>
public class LevelRegistry
{
    public IReadOnlyList<ILevel> All { get; }

    public LevelRegistry(IEnumerable<ILevel> levels)
    {
        All = levels.OrderBy(l => l.Id).ToList();
    }

    public ILevel? GetById(int id) => All.FirstOrDefault(l => l.Id == id);
}