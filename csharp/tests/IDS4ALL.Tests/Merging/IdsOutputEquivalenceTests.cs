using System.Text.Json;
using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Ids;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Merging;
using IDS4ALL.IfcTester;
using IDS4ALL.Tests.Excel;
using IDS4ALL.Tests.Import;
using static IDS4ALL.Tests.Import.MetadataReaderTests;

namespace IDS4ALL.Tests.Merging;

/// <summary>
/// Differential tests: the IDS files that C# writes through the bridge have the same text as the files of the Python version
/// (separate_specs_by_generaldata, create_ids_specifications and to_xml).
/// </summary>
public sealed class IdsOutputEquivalenceTests
{
    private const string Code = """
        import json, os, sys, importlib

        def _cf(root):
            if root not in sys.path:
                sys.path.insert(0, root)
            return importlib.import_module("custom_functions")

        def _failure(e):
            return {"error": type(e).__name__ + ": " + str(e)}

        def files(root, path, sheet, skip, ifc, entity_based, separate_by_json, out_dir, meta_json):
            try:
                cf = _cf(root)
                meta = json.loads(meta_json)
                from ifctester import ids
                spec_list = cf.excel_to_spec_list(path, sheet, json.loads(separate_by_json), skip, ifc, bool(entity_based))
                separated = cf.separate_specs_by_generaldata(spec_list)
                out = []
                for number, (key, sep) in enumerate(separated.items()):
                    try:
                        milestone = ''
                        purpose = ''
                        if len(sep['general']['Phase']) != 0: milestone += ', '.join(sep['general']['Phase'])
                        if len(sep['general']['Role']) != 0: purpose += 'Role: ' + ', '.join(sep['general']['Role']) + '; '
                        if len(sep['general']['Usecase']) != 0: purpose += 'Usecase: ' + ', '.join(sep['general']['Usecase']) + '; '
                        purpose = purpose[0:len(purpose)-2]
                        my_ids = ids.Ids(title=meta['title'], copyright=meta['copyright'], version=meta['version'], description=meta['description'], author=meta['author'], date='2026-01-01',
                                         purpose=purpose if purpose != '' else None, milestone=milestone if milestone != '' else None)
                        cf.create_ids_specifications(my_ids, sep['specs'])
                        file = os.path.join(out_dir, "python%d.ids" % number)
                        my_ids.to_xml(file)
                        with open(file, encoding="utf-8", newline="") as handle:
                            out.append([key, {"xml": handle.read()}])
                    except Exception as e:
                        out.append([key, _failure(e)])
                return json.dumps({"ok": out})
            except Exception as e:
                return json.dumps(_failure(e))
        """;

    private static readonly PythonScript Script = new(Code);

    private static readonly List<string> Errors = [];

    [PythonTheory]
    [MemberData(nameof(ExampleWorkbooks))]
    public void ExampleWorkbook_WritesTheSameFiles(string workbook)
    {
        var path = RepositoryRoot.ExcelFile(workbook);
        Ids4AllSettings settings;
        using (var opened = XlsxWorkbook.Open(path)) settings = MetadataReader.Read(opened);

        using var directory = new TempDirectory();
        AssertSameFiles(path, settings, directory.Path);
    }

    public static TheoryData<string> ExampleWorkbooks() => MetadataReaderTests.ExampleWorkbooks();

    [PythonFact]
    public void RandomSheets_WriteTheSameFiles()
    {
        var random = new Random(int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_SEED"), out var seed) ? seed : 20261019);
        var runs = int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_RUNS"), out var count) ? count / 3 : 50;
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "sheet.xlsx");
        var written = 0;
        for (var run = 0; run < runs; run++)
        {
            var (columns, separateBy) = ValidSheetGenerator.Write(random, path);
            try
            {
                written += AssertSameFiles(path, MergingEquivalenceTests.Settings(separateBy, random.Next(5) == 0), directory.Path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Random sheet {run} differs ({string.Join(", ", columns)}): {ex.Message}", ex);
            }
        }

        Assert.True(written >= runs / 2, $"Only {written} IDS files were written in {runs} runs. Python errors: " + string.Join(" // ", Errors.GroupBy(e => e).OrderByDescending(g => g.Count()).Select(g => g.Count() + "x" + g.Key)));
    }

    /// <returns>The number of files that both versions wrote.</returns>
    private static int AssertSameFiles(string path, Ids4AllSettings settings, string directory)
    {
        using var document = Script.Call("files", RepositoryRoot.Path, path, settings.SheetName, settings.SkippedRows, settings.IfcVersion,
            settings.IsEntityBasedApplicability, JsonSerializer.Serialize(settings.SeparateBy), directory,
            JsonSerializer.Serialize(new { title = settings.Title, copyright = settings.Copyright, version = settings.Version, description = settings.Description, author = settings.Author }));
        var root = document.RootElement;

        List<(string Key, string? Xml, string? Error)> actual;
        string? importError = null;
        try
        {
            actual = WriteWithCSharp(path, settings, directory);
        }
        catch (Exception ex) when (ex is ImportException or XlsxException)
        {
            actual = [];
            importError = ex.Message;
        }

        if (root.TryGetProperty("error", out var pythonError))
        {
            Errors.Add(pythonError.GetString()!.Split((char)10)[0]);
            Assert.True(importError is not null, $"Python failed ({pythonError.GetString()}) but C# wrote files.");
            return 0;
        }

        Assert.True(importError is null, $"C# failed ({importError}) but Python wrote files.");
        var expected = root.GetProperty("ok").EnumerateArray().ToList();
        Assert.Equal(expected.Count, actual.Count);
        var same = 0;
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i][0].GetString(), actual[i].Key);
            var result = expected[i][1];
            if (result.TryGetProperty("error", out var error))
            {
                Errors.Add(error.GetString()!.Split((char)10)[0]);
                Assert.True(actual[i].Error is not null, $"File '{actual[i].Key}': Python failed ({error.GetString()}) but C# wrote a file.");
                continue;
            }

            Assert.True(actual[i].Error is null, $"File '{actual[i].Key}': C# failed ({actual[i].Error}) but Python wrote a file.");
            var pythonText = result.GetProperty("xml").GetString()!.Replace("\r\n", "\n");
            var csharpText = actual[i].Xml!.Replace("\r\n", "\n");
            Assert.True(pythonText == csharpText, $"File '{actual[i].Key}' differs. {FirstDifference(pythonText, csharpText)}");
            same++;
        }
        return same;
    }

    private static string FirstDifference(string python, string csharp)
    {
        var a = python.Split('\n');
        var b = csharp.Split('\n');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) return $"Line {i + 1}:\nPython: {a[i]}\nC#:     {b[i]}\n--- context ---\n{string.Join('\n', a.Skip(Math.Max(0, i - 3)).Take(5))}";
        return $"Python has {a.Length} lines, C# has {b.Length}.";
    }

    private static List<(string Key, string? Xml, string? Error)> WriteWithCSharp(string path, Ids4AllSettings settings, string directory)
    {
        using var workbook = XlsxWorkbook.Open(path);
        var list = SpecificationBuilder.Build(SheetImporter.Import(workbook, settings), settings);
        var writer = new IdsWriter(PythonRuntimeOptions.FromEnvironment());

        var result = new List<(string, string?, string?)>();
        var number = 0;
        foreach (var (key, group) in SpecificationFactory.Separate(list.Specifications))
        {
            try
            {
                var file = IdsFileBuilder.Build(group, settings, new DateOnly(2026, 1, 1), list.Descriptions);
                var target = Path.Combine(directory, $"csharp{number}.ids");
                writer.Write(file, target);
                result.Add((key, File.ReadAllText(target), null));
            }
            catch (Exception ex) when (ex is ImportException or IdsWriteException)
            {
                result.Add((key, null, ex.Message));
            }
            number++;
        }
        return result;
    }
}
