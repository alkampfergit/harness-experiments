namespace Polifemo.Cli;

/// <summary>Locates the repository root (the folder holding <c>global.json</c>) and its <c>.index/</c> area.</summary>
public static class RepositoryPaths
{
    public static string Root()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "global.json"))) return dir.FullName;
        }
        throw new UsageException("Run from inside the repository (global.json was not found).");
    }

    public static string WorkRoot() => Path.Combine(Root(), ".index", "work");
}
