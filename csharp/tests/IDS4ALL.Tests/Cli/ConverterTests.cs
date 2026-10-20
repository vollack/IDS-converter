using System.Text.RegularExpressions;
using IDS4ALL.Cli;
using IDS4ALL.IfcTester;
using static IDS4ALL.Tests.Import.MetadataReaderTests;

namespace IDS4ALL.Tests.Cli;

/// <summary>Runs the converter like the command line does. These tests need Python with ifctester.</summary>
public sealed partial class ConverterTests
{
    private const string ViennaName = "Requirements-openBIM-building-permit-City-of-Vienna-extended";
    private static readonly DateOnly Today = new(2026, 10, 19);

    [PythonFact]
    public void ViennaWorkbook_WritesTheExampleFile_AndTheMessagesOfThePythonVersion()
    {
        using var directory = new TempDirectory();
        var outputPath = Path.Combine(directory.Path, "out", "nested");

        var (code, output, error) = Run("--excel_path", Path.Combine(RepositoryRoot.Path, "Excel-files"), "--excel_name", ViennaName, "--output_path", outputPath);

        var fileName = ViennaName + "_Specifications.ids";
        var target = Path.Combine(outputPath, fileName);
        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Equal(
            [
                $"Excel file path: {Path.Combine(RepositoryRoot.Path, "Excel-files", ViennaName + ".xlsx")}",
                $"Output path: {outputPath}",
                "",
                "XML created",
                $"Output file: {target}",
            ],
            output.TrimEnd().Split(Environment.NewLine));

        var written = File.ReadAllText(target);
        Assert.Contains($"<date>{Today:yyyy-MM-dd}</date>", written);
        Assert.Equal(WithoutDate(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "Example-IDS-files", fileName))), WithoutDate(written));
    }

    [PythonFact]
    public void FileThatCannotBeWritten_IsReported_AndTheRunContinues()
    {
        using var directory = new TempDirectory();
        var blocked = Path.Combine(directory.Path, "blocked");
        File.WriteAllText(blocked, "a file, not a folder");

        var (code, output, error) = Run("--excel_path", Path.Combine(RepositoryRoot.Path, "Excel-files"), "--excel_name", ViennaName, "--output_path", blocked);

        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("Error:  ", output);
        Assert.DoesNotContain("XML created", output);
    }

    [PythonFact]
    public void MissingWorkbook_ReturnsOne()
    {
        using var directory = new TempDirectory();

        var (code, _, error) = Run("--excel_path", directory.Path, "--excel_name", "missing", "--output_path", directory.Path);

        Assert.Equal(1, code);
        Assert.StartsWith("Error: ", error);
    }

    [PythonFact]
    public void WorkbookWithoutMetadataSheet_ReturnsOne()
    {
        using var directory = new TempDirectory();
        new Excel.XlsxBuilder().Sheet("Data", [[Excel.TestCell.Text("A.Entity")]]).Save(Path.Combine(directory.Path, "book.xlsx"));

        var (code, output, error) = Run("--excel_path", directory.Path, "--excel_name", "book", "--output_path", directory.Path);

        Assert.Equal(1, code);
        Assert.StartsWith("Excel file path: ", output);
        Assert.Contains("IDS4ALL", error);
    }

    internal static (int Code, string Output, string Error) Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Converter.Run(arguments, output, error, () => new IdsWriter(PythonRuntimeOptions.FromEnvironment()), Today);
        return (code, output.ToString(), error.ToString());
    }

    private static string WithoutDate(string xml) => DateElement().Replace(xml, "<date/>").Replace("\r\n", "\n");

    [GeneratedRegex("<date>[^<]*</date>")]
    private static partial Regex DateElement();
}
