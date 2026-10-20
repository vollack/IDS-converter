using System.Text.RegularExpressions;
using IDS4ALL.Tests.Excel;
using IDS4ALL.Tests.Import;
using IDS4ALL.Tests.Merging;
using static IDS4ALL.Tests.Import.MetadataReaderTests;

namespace IDS4ALL.Tests.Cli;

/// <summary>
/// Differential tests of the whole converter: IDS4ALL-main.py and the C# converter get the same workbook and arguments. They must print
/// the same messages and write the same files.
/// </summary>
public sealed partial class ConverterEquivalenceTests
{
    private const string Code = """
        import contextlib, io, json, os, runpy, sys

        def convert(root, excel_path, excel_name, output_path):
            if root not in sys.path:
                sys.path.insert(0, root)
            buffer = io.StringIO()
            saved = sys.argv
            sys.argv = ["IDS4ALL-main.py", "--excel_path", excel_path, "--excel_name", excel_name, "--output_path", output_path]
            error = None
            try:
                with contextlib.redirect_stdout(buffer):
                    runpy.run_path(os.path.join(root, "IDS4ALL-main.py"), run_name="__main__")
            except BaseException as e:
                error = type(e).__name__ + ": " + str(e)
            finally:
                sys.argv = saved
            return json.dumps({"stdout": buffer.getvalue(), "error": error})
        """;

    private static readonly PythonScript Script = new(Code);

    [PythonTheory]
    [MemberData(nameof(ExampleWorkbooks))]
    public void ExampleWorkbook_GivesTheSameResult(string workbook)
    {
        Assert.True(AssertSameResult(Path.Combine(RepositoryRoot.Path, "Excel-files"), Path.GetFileNameWithoutExtension(workbook)));
    }

    public static TheoryData<string> ExampleWorkbooks() => MetadataReaderTests.ExampleWorkbooks();

    [PythonFact]
    public void RandomWorkbooks_GiveTheSameResult()
    {
        var random = new Random(int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_SEED"), out var seed) ? seed : 20261019);
        var runs = int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_RUNS"), out var count) ? count / 3 : 50;
        using var directory = new TempDirectory();
        var written = 0;
        for (var run = 0; run < runs; run++)
        {
            var (rows, columns, separateBy) = ValidSheetGenerator.Generate(random);
            new XlsxBuilder().Sheet("IDS4ALL", Metadata(separateBy, random.Next(5) == 0)).Sheet("Data", rows).Save(Path.Combine(directory.Path, "book.xlsx"));
            try
            {
                if (AssertSameResult(directory.Path, "book")) written++;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Random workbook {run} differs ({string.Join(", ", columns)}): {ex.Message}", ex);
            }
        }

        Assert.True(written >= runs / 2, $"Only {written} of {runs} random workbooks gave IDS files in Python.");
    }

    private static TestCell?[][] Metadata(string[] separateBy, bool entityBased)
    {
        var rows = new List<TestCell?[]>
        {
            new[] { TestCell.Text("Entry"), TestCell.Text("Value") },
            new[] { TestCell.Text("Sheet name"), TestCell.Text("Data") },
            new[] { TestCell.Text("IFC version"), TestCell.Text("IFC4, IFC2X3") },
            new[] { TestCell.Text("Title"), TestCell.Text("Random workbook") },
            new[] { TestCell.Text("Author"), TestCell.Text("author@example.org") },
        };
        if (separateBy.Length > 0) rows.Add([TestCell.Text("File separators"), TestCell.Text(string.Join(", ", separateBy))]);
        if (entityBased) rows.Add([TestCell.Text("Entity-based applicability"), TestCell.Text("yes")]);
        return [.. rows];
    }

    /// <returns>True if Python wrote at least one file.</returns>
    private static bool AssertSameResult(string excelPath, string excelName)
    {
        using var directory = new TempDirectory();
        var pythonOutput = Path.Combine(directory.Path, "python");
        var csharpOutput = Path.Combine(directory.Path, "csharp");

        using var document = Script.Call("convert", RepositoryRoot.Path, excelPath, excelName, pythonOutput);
        var pythonError = document.RootElement.GetProperty("error").GetString();
        var pythonMessages = Normalize(document.RootElement.GetProperty("stdout").GetString()!, pythonOutput);

        var (code, messages, error) = ConverterTests.Run("--excel_path", excelPath, "--excel_name", excelName, "--output_path", csharpOutput);

        if (pythonError is null)
            Assert.True(code == 0, $"C# failed ({error}) but Python did not.");
        else
            Assert.True(code == 1, $"Python failed ({pythonError}) but C# returned {code}.");
        Assert.Equal(pythonMessages, Normalize(messages, csharpOutput));

        var pythonFiles = ReadFiles(pythonOutput);
        var csharpFiles = ReadFiles(csharpOutput);
        Assert.Equal(pythonFiles.Keys, csharpFiles.Keys);
        foreach (var (name, text) in pythonFiles)
            Assert.True(text == csharpFiles[name], $"File '{name}' differs.\nPython:\n{text}\nC#:\n{csharpFiles[name]}");
        return pythonFiles.Count > 0;
    }

    /// <summary>Makes the messages equal: the output path, the line breaks and the text of an error differ.</summary>
    private static string Normalize(string messages, string outputPath) =>
        ErrorText().Replace(messages.Replace("\r\n", "\n").Replace(outputPath, "<output>"), "Error:");

    private static SortedDictionary<string, string> ReadFiles(string directory) =>
        new(Directory.Exists(directory)
            ? Directory.GetFiles(directory).ToDictionary(file => Path.GetFileName(file), file => File.ReadAllText(file).Replace("\r\n", "\n"))
            : []);

    [GeneratedRegex("^Error: .*$", RegexOptions.Multiline)]
    private static partial Regex ErrorText();
}
