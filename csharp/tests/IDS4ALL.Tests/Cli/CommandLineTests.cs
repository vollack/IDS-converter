using IDS4ALL.Cli;
using IDS4ALL.IfcTester;

namespace IDS4ALL.Tests.Cli;

public sealed class CommandLineTests
{
    [Fact]
    public void NoArguments_UseTheDefaultsOfThePythonVersion()
    {
        var result = CommandLine.Parse([]);

        Assert.Equal(CliOptions.Defaults, result.Options);
        Assert.Equal("./Excel-files/", result.Options!.ExcelPath);
        Assert.Equal("Requirements-openBIM-building-permit-City-of-Vienna-extended", result.Options.ExcelName);
        Assert.Equal(".xlsx", result.Options.ExcelFormat);
        Assert.Equal("./output/", result.Options.OutputPath);
    }

    [Fact]
    public void AllOptions_AreRead()
    {
        var result = CommandLine.Parse(["--excel_path", "in", "--excel_name", "book", "--excel_format", ".xlsm", "--output_path", "out"]);

        Assert.Equal(new CliOptions("in", "book", ".xlsm", "out"), result.Options);
    }

    [Fact]
    public void EqualsSign_AndShortenedNames_AreAccepted()
    {
        var result = CommandLine.Parse(["--excel_path=in", "--excel_na", "book", "--output=out"]);

        Assert.Equal(CliOptions.Defaults with { ExcelPath = "in", ExcelName = "book", OutputPath = "out" }, result.Options);
    }

    [Fact]
    public void RepeatedOption_LastValueWins()
    {
        Assert.Equal("b", CommandLine.Parse(["--output_path", "a", "--output_path", "b"]).Options!.OutputPath);
    }

    [Fact]
    public void Value_CanBeEmptyDashOrNegativeNumber()
    {
        var result = CommandLine.Parse(["--excel_path=", "--excel_name", "-", "--output_path", "-1"]);

        Assert.Equal(CliOptions.Defaults with { ExcelPath = "", ExcelName = "-", OutputPath = "-1" }, result.Options);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("--he")]
    public void Help_StopsTheParsing(string argument)
    {
        var result = CommandLine.Parse(["--excel_path", "in", argument, "--bogus"]);

        Assert.True(result.ShowHelp);
        Assert.Null(result.Options);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(new[] { "--excel" }, "ambiguous option: --excel could match --excel_path, --excel_name, --excel_format")]
    [InlineData(new[] { "--excel_path" }, "argument --excel_path: expected one argument")]
    [InlineData(new[] { "--excel_path", "--output_path", "out" }, "argument --excel_path: expected one argument")]
    [InlineData(new[] { "--bogus" }, "unrecognized arguments: --bogus")]
    [InlineData(new[] { "file.xlsx", "-x" }, "unrecognized arguments: file.xlsx -x")]
    [InlineData(new[] { "--help=1" }, "argument -h/--help: ignored explicit argument '1'")]
    public void InvalidArguments_GiveAnError(string[] arguments, string message)
    {
        var result = CommandLine.Parse(arguments);

        Assert.Null(result.Options);
        Assert.False(result.ShowHelp);
        Assert.Equal(message, result.Error);
    }

    [Fact]
    public void Run_Help_WritesTheHelpAndReturnsZero()
    {
        var (code, output, error) = RunWithoutPython("-h");

        Assert.Equal(0, code);
        Assert.StartsWith("usage: IDS4ALL [-h]", output);
        Assert.Contains("--output_path OUTPUT_PATH", output);
        Assert.Equal("", error);
    }

    [Fact]
    public void Run_InvalidArguments_WriteUsageAndReturnTwo()
    {
        var (code, output, error) = RunWithoutPython("--bogus");

        Assert.Equal(2, code);
        Assert.Equal("", output);
        Assert.StartsWith("usage: IDS4ALL", error);
        Assert.EndsWith("IDS4ALL: error: unrecognized arguments: --bogus" + Environment.NewLine, error);
    }

    [Fact]
    public void Run_WithoutArguments_PrintsTheHintOfThePythonVersion()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = Converter.Run([], output, error, () => throw new PythonSetupException("No Python."), new DateOnly(2026, 1, 1));

        Assert.Equal(1, code);
        Assert.Equal("Default values were used, check -h for override parameters" + Environment.NewLine, output.ToString());
        Assert.Equal("Error: No Python." + Environment.NewLine, error.ToString());
    }

    private static (int Code, string Output, string Error) RunWithoutPython(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Converter.Run(arguments, output, error, () => throw new InvalidOperationException("Python must not start."), new DateOnly(2026, 1, 1));
        return (code, output.ToString(), error.ToString());
    }
}
