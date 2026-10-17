using System.Text.Json;
using IDS4ALL.IfcTester;
using Python.Runtime;

namespace IDS4ALL.Tests.Excel;

/// <summary>Result of a pandas call: the data, or the error text of the Python exception.</summary>
public sealed record PandasResult<T>(T? Data, string? Error);

/// <summary>Runs pandas.read_excel through Python.NET. It is the reference for the differential tests.</summary>
internal static class PandasReference
{
    private const string Code = """
        import json
        import pandas as pd

        def _error(e):
            return json.dumps({"error": type(e).__name__ + ": " + str(e)})

        def read_columns(path, sheet, skip):
            try:
                df = pd.read_excel(path, sheet_name=sheet, skiprows=skip, nrows=0)
                return json.dumps({"columns": [str(c) for c in df.columns], "allText": all(isinstance(c, str) for c in df.columns)})
            except Exception as e:
                return _error(e)

        def read_strings(path, sheet, skip, usecols):
            try:
                df = pd.read_excel(path, sheet_name=sheet, skiprows=skip, usecols=list(usecols), dtype=str)
                return json.dumps({
                    "columns": [str(c) for c in df.columns],
                    "rows": [[None if pd.isna(v) else v for v in row] for row in df.itertuples(index=False, name=None)],
                })
            except Exception as e:
                return _error(e)

        def _load_main(root):
            import importlib.util, os, sys
            if root not in sys.path:
                sys.path.insert(0, root)
            spec = importlib.util.spec_from_file_location("ids4all_main", os.path.join(root, "IDS4ALL-main.py"))
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            return module

        def get_metadata(root, path):
            import os
            try:
                main = _load_main(root)
                directory, name = os.path.split(path)
                stem, extension = os.path.splitext(name)
                sheet_name, ifc_version, separate_by, skipped_rows, entity_based, data = main.get_metadata(directory, stem, extension, 'IDS4ALL')
                def text(key):
                    return str(data[key]) if key in data else None
                return json.dumps({
                    "sheetName": str(sheet_name), "ifcVersion": ifc_version, "separateBy": separate_by,
                    "skippedRows": str(skipped_rows), "entityBased": entity_based,
                    "title": data['Title'] if 'Title' in data else 'Not Defined',
                    "copyright": text('Copyright'), "version": text('Version'),
                    "description": text('Description'), "author": text('Author'),
                })
            except Exception as e:
                return _error(e)

        def import_frame(root, path, sheet, skip, ifc_versions, entity_based):
            import sys
            try:
                _load_main(root)
                import custom_functions as cf
                original = pd.DataFrame.fillna

                class Captured(Exception):
                    pass

                def capture(self, value=None, *args, **kwargs):
                    result = original(self, value, *args, **kwargs)
                    if isinstance(value, str) and value == cf.KEYWORD_MISSING:
                        raise Captured(result)
                    return result

                pd.DataFrame.fillna = capture
                try:
                    cf.excel_to_spec_list(path, sheet, [], skip, ifc_versions, entity_based)
                except Captured as captured:
                    df = captured.args[0]
                finally:
                    pd.DataFrame.fillna = original
                return json.dumps({
                    "columns": [str(c) for c in df.columns],
                    "rows": [[str(v) for v in row] for row in df.itertuples(index=False, name=None)],
                })
            except Exception as e:
                return _error(e)

        def read_pairs(path, sheet, skip):
            try:
                df = pd.read_excel(path, sheet_name=sheet, usecols=[0, 1], header=None, skiprows=skip)
                def cell(v):
                    return None if pd.isna(v) else [type(v).__name__, str(v)]
                return json.dumps({"rows": [[cell(a), cell(b)] for a, b in zip(df[0], df[1])]})
            except Exception as e:
                return _error(e)
        """;

    private static readonly PythonScript Script = new(Code);

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PythonRuntimeOptions.PythonDllVariable))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PYTHONNET_PYDLL"));

    /// <param name="AllText">False when a header cell is not text, for example a number. pandas then keeps its type.</param>
    public sealed record ColumnsResult(IReadOnlyList<string>? Columns, bool AllText, string? Error);

    public static ColumnsResult ReadColumns(string path, string sheet, int skipRows)
    {
        using var document = Call("read_columns", path, sheet, skipRows);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error)) return new(null, true, error.GetString());
        return new(root.GetProperty("columns").EnumerateArray().Select(e => e.GetString()!).ToList(),
            root.GetProperty("allText").GetBoolean(), null);
    }

    public static PandasResult<(IReadOnlyList<string> Columns, List<string?[]> Rows)> ReadStrings(
        string path, string sheet, int skipRows, IReadOnlyCollection<string> useColumns)
    {
        using var document = Call("read_strings", path, sheet, skipRows, useColumns);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error)) return new(default, error.GetString());
        var columns = root.GetProperty("columns").EnumerateArray().Select(e => e.GetString()!).ToList();
        var rows = root.GetProperty("rows").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Null ? null : c.GetString()).ToArray())
            .ToList();
        return new((columns, rows), null);
    }

    public sealed record MetadataResult(
        string SheetName, string IfcVersion, IReadOnlyList<string> SeparateBy, string SkippedRows, bool EntityBased,
        string Title, string? Copyright, string? Version, string? Description, string? Author);

    /// <summary>The metadata that the Python function get_metadata in IDS4ALL-main.py returns.</summary>
    public static PandasResult<MetadataResult> GetMetadata(string path)
    {
        using var document = Call("get_metadata", RepositoryRoot.Path, path);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error)) return new(null, error.GetString());
        string? Optional(string name) => root.GetProperty(name).ValueKind == JsonValueKind.Null ? null : root.GetProperty(name).GetString();
        return new(new MetadataResult(
            root.GetProperty("sheetName").GetString()!, root.GetProperty("ifcVersion").GetString()!,
            root.GetProperty("separateBy").EnumerateArray().Select(e => e.GetString()!).ToList(),
            root.GetProperty("skippedRows").GetString()!, root.GetProperty("entityBased").GetBoolean(),
            root.GetProperty("title").GetString()!, Optional("copyright"), Optional("version"),
            Optional("description"), Optional("author")), null);
    }

    /// <summary>The dataframe df_filled that excel_to_spec_list builds before the first grouping.</summary>
    public static PandasResult<(IReadOnlyList<string> Columns, List<string[]> Rows)> ImportFrame(
        string path, string sheet, int skipRows, string ifcVersions, bool entityBased)
    {
        using var document = Call("import_frame", RepositoryRoot.Path, path, sheet, skipRows, ifcVersions, entityBased);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error)) return new(default, error.GetString());
        var columns = root.GetProperty("columns").EnumerateArray().Select(e => e.GetString()!).ToList();
        var rows = root.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateArray().Select(c => c.GetString()!).ToArray()).ToList();
        return new((columns, rows), null);
    }

    /// <summary>Each pair holds (type name, text) per cell, or null for a missing value.</summary>
    public static PandasResult<List<((string Type, string Text)? First, (string Type, string Text)? Second)>> ReadPairs(
        string path, string sheet, int skipRows)
    {
        using var document = Call("read_pairs", path, sheet, skipRows);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error)) return new(null, error.GetString());

        (string Type, string Text)? Cell(JsonElement e) =>
            e.ValueKind == JsonValueKind.Null ? null : (e[0].GetString()!, e[1].GetString()!);
        return new(root.GetProperty("rows").EnumerateArray().Select(r => (Cell(r[0]), Cell(r[1]))).ToList(), null);
    }

    private static JsonDocument Call(string function, params object[] arguments) => Script.Call(function, arguments);
}
