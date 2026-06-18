namespace ChuvadiReader.Core.Documents;

/// <summary>A document category: a named, coloured folder under Chuvadi Documents.</summary>
public sealed record DocCategory(string Name, string Color);

/// <summary>
/// The default category folders that live under the Chuvadi Documents base
/// folder. Each is a colour; a PDF inside a category folder is auto-tagged with
/// that category (see <see cref="CategoryFor"/>). The folders are created on
/// startup so the storage map always has its ten buckets.
/// </summary>
public sealed class CategoryService
{
    private readonly SaveFolderService _saveFolders;

    public CategoryService(SaveFolderService saveFolders) => _saveFolders = saveFolders;

    /// <summary>The ten default categories, in display order.</summary>
    public static readonly IReadOnlyList<DocCategory> Defaults = new[]
    {
        new DocCategory("Finance", "#3f9e8e"),
        new DocCategory("Legal", "#5b7fb0"),
        new DocCategory("Clinical", "#b0577f"),
        new DocCategory("Admin", "#c98a3a"),
        new DocCategory("HR", "#6b9e5b"),
        new DocCategory("Invoices", "#8a6bb0"),
        new DocCategory("Reports", "#b59a3c"),
        new DocCategory("Licensing", "#4f8a99"),
        new DocCategory("Personal", "#9b7b5b"),
        new DocCategory("Insurance", "#c25f43"),
    };

    public IReadOnlyList<DocCategory> Categories => Defaults;

    public string BaseFolder => _saveFolders.BaseFolder;

    /// <summary>Creates the base folder and the ten category subfolders if missing.</summary>
    public void EnsureFolders()
    {
        try
        {
            Directory.CreateDirectory(BaseFolder);
            foreach (var category in Categories)
            {
                Directory.CreateDirectory(Path.Combine(BaseFolder, category.Name));
            }
        }
        catch
        {
            // best effort — a locked/!writable folder shouldn't crash startup
        }
    }

    /// <summary>The category a file belongs to, derived from the first path segment
    /// under the base folder. Null if the file isn't inside a known category.</summary>
    public DocCategory? CategoryFor(string path)
    {
        try
        {
            var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(BaseFolder));
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var rel = Path.GetRelativePath(baseFull, full);
            var top = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            return Categories.FirstOrDefault(c => string.Equals(c.Name, top, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    public string ColorFor(string categoryName)
        => Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase))?.Color
           ?? "#8a7c64";
}
