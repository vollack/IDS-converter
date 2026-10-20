using System.Globalization;

namespace IDS4ALL.Cli;

/// <summary>The command-line arguments. The defaults are those of IDS4ALL-main.py.</summary>
public sealed record CliOptions(string ExcelPath, string ExcelName, string ExcelFormat, string OutputPath)
{
    public static CliOptions Defaults { get; } = new(
        ExcelPath: "./Excel-files/",
        ExcelName: "Requirements-openBIM-building-permit-City-of-Vienna-extended",
        ExcelFormat: ".xlsx",
        OutputPath: "./output/");
}

/// <summary>The result of <see cref="CommandLine.Parse"/>: the options, the request for help, or an error message.</summary>
public sealed record ParseResult(CliOptions? Options, bool ShowHelp, string? Error);

/// <summary>Port of parse_arguments in IDS4ALL-main.py. It follows argparse: "--name value", "--name=value", short forms of long names.</summary>
public static class CommandLine
{
    public const string ProgramName = "IDS4ALL";

    private sealed record Option(string Name, string Meta, string Help);

    private static readonly Option[] Options =
    [
        new("--excel_path", "EXCEL_PATH", "Path to the directory containing the Excel file."),
        new("--excel_name", "EXCEL_NAME", "Name of the Excel file (without extension)."),
        new("--excel_format", "EXCEL_FORMAT", "Format of the Excel file (e.g., .xlsx)."),
        new("--output_path", "OUTPUT_PATH", "Path to the output directory for IDS files."),
    ];

    private const string HelpName = "--help";

    public const string Usage = """
        usage: IDS4ALL [-h] [--excel_path EXCEL_PATH] [--excel_name EXCEL_NAME]
                       [--excel_format EXCEL_FORMAT] [--output_path OUTPUT_PATH]
        """;

    public static string HelpText { get; } = string.Join('\n',
        [
            Usage,
            "",
            "Generate IDS files from Excel data.",
            "",
            "options:",
            "  -h, --help            show this help message and exit",
            .. Options.SelectMany(option => new[] { $"  {option.Name} {option.Meta}", "                        " + option.Help }),
        ]);

    public static ParseResult Parse(IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string>();
        var unrecognized = new List<string>();

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (argument == "--")
            {
                unrecognized.AddRange(arguments.Skip(i + 1));
                break;
            }
            if (!argument.StartsWith('-') || argument == "-" || IsNegativeNumber(argument))
            {
                unrecognized.Add(argument);
                continue;
            }
            if (argument == "-h") return new ParseResult(null, true, null);

            var separator = argument.IndexOf('=');
            var name = argument.StartsWith("--") ? (separator < 0 ? argument : argument[..separator]) : argument;
            var matches = argument.StartsWith("--") ? Match(name) : [];
            if (matches.Count > 1)
                return Failure($"ambiguous option: {name} could match {string.Join(", ", matches)}");
            if (matches.Count == 0)
            {
                unrecognized.Add(argument);
                continue;
            }
            if (matches[0] == HelpName)
            {
                if (separator >= 0) return Failure($"argument -h/--help: ignored explicit argument '{argument[(separator + 1)..]}'");
                return new ParseResult(null, true, null);
            }

            string value;
            if (separator >= 0)
            {
                value = argument[(separator + 1)..];
            }
            else if (i + 1 < arguments.Count && !IsOptionLike(arguments[i + 1]))
            {
                value = arguments[++i];
            }
            else
            {
                return Failure($"argument {matches[0]}: expected one argument");
            }
            values[matches[0]] = value;
        }

        if (unrecognized.Count > 0) return Failure("unrecognized arguments: " + string.Join(' ', unrecognized));

        var defaults = CliOptions.Defaults;
        return new ParseResult(new CliOptions(
            values.GetValueOrDefault("--excel_path", defaults.ExcelPath),
            values.GetValueOrDefault("--excel_name", defaults.ExcelName),
            values.GetValueOrDefault("--excel_format", defaults.ExcelFormat),
            values.GetValueOrDefault("--output_path", defaults.OutputPath)), false, null);
    }

    private static ParseResult Failure(string message) => new(null, false, message);

    /// <summary>The long option that equals the text, or all long options that start with it.</summary>
    private static List<string> Match(string name)
    {
        var names = Options.Select(option => option.Name).Append(HelpName).ToList();
        if (names.Contains(name)) return [name];
        return names.Where(candidate => candidate.StartsWith(name, StringComparison.Ordinal)).ToList();
    }

    private static bool IsNegativeNumber(string text) =>
        text.StartsWith('-') && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool IsOptionLike(string text) => text.StartsWith('-') && text != "-" && !IsNegativeNumber(text);
}
