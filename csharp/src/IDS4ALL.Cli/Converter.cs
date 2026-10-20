using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Ids;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Merging;
using IDS4ALL.IfcTester;

namespace IDS4ALL.Cli;

/// <summary>Port of main, process_excel_data and create_ids_files in IDS4ALL-main.py.</summary>
public static class Converter
{
    public const string CreatorComment =
        " Created with the IDS4ALL Converter developed by Simon Fischer, Harald Urban, Konstantin Höbart, and Christian Schranz of TU Wien Research Unit Digital Building Process (https://www.tuwien.at/en/cee/ibb/zdb). ";

    /// <summary>Starts the program: parses the arguments, converts the workbook and writes the IDS files.</summary>
    /// <param name="createWriter">Creates the IDS writer. It starts Python, so it runs only after the arguments are valid.</param>
    /// <returns>The exit code: 0 for success, 1 for an error in the conversion, 2 for invalid arguments.</returns>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error, Func<IdsWriter> createWriter, DateOnly today)
    {
        var parsed = CommandLine.Parse(arguments);
        if (parsed.ShowHelp)
        {
            output.WriteLine(CommandLine.HelpText);
            return 0;
        }
        if (parsed.Options is not { } options)
        {
            error.WriteLine(CommandLine.Usage);
            error.WriteLine($"{CommandLine.ProgramName}: error: {parsed.Error}");
            return 2;
        }

        if (options == CliOptions.Defaults) output.WriteLine("Default values were used, check -h for override parameters");

        try
        {
            var writer = createWriter();
            Convert(options, output, writer, today);
            return 0;
        }
        catch (Exception ex) when (ex is ImportException or XlsxException or PythonSetupException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    /// <exception cref="ImportException">The workbook has an error or a facet cannot be created.</exception>
    /// <exception cref="XlsxException">The workbook cannot be read.</exception>
    public static void Convert(CliOptions options, TextWriter output, IdsWriter writer, DateOnly today)
    {
        var excelFile = Path.Combine(options.ExcelPath, options.ExcelName + options.ExcelFormat);
        output.WriteLine($"Excel file path: {excelFile}");
        output.WriteLine($"Output path: {options.OutputPath}");
        output.WriteLine();

        Ids4AllSettings settings;
        SpecificationList list;
        using (var workbook = XlsxWorkbook.Open(excelFile))
        {
            settings = MetadataReader.Read(workbook);
            list = SpecificationBuilder.Build(SheetImporter.Import(workbook, settings), settings);
        }

        foreach (var (key, group) in SpecificationFactory.Separate(list.Specifications))
        {
            var file = IdsFileBuilder.Build(group, settings, today, list.Descriptions);
            try
            {
                Directory.CreateDirectory(options.OutputPath);
                var target = Path.Combine(options.OutputPath, options.ExcelName + "_" + settings.SheetName + key.Replace('/', '-') + ".ids");
                writer.Write(file, target, CreatorComment);
                output.WriteLine("XML created");
                output.WriteLine($"Output file: {target}");
            }
            catch (Exception ex)
            {
                output.WriteLine("Error:  " + ex.Message);
            }
        }
    }
}
