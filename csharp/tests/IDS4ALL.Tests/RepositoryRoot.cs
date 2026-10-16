namespace IDS4ALL.Tests;

/// <summary>Finds the root of the repository: the folder with custom_functions.py and the example workbooks.</summary>
internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    public static string ExcelFile(string name) => System.IO.Path.Combine(Path, "Excel-files", name);

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "custom_functions.py")))
                return directory.FullName;
        throw new InvalidOperationException("The repository root with custom_functions.py was not found.");
    }
}
