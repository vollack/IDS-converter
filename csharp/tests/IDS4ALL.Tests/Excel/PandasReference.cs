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

        def read_pairs(path, sheet, skip):
            try:
                df = pd.read_excel(path, sheet_name=sheet, usecols=[0, 1], header=None, skiprows=skip)
                def cell(v):
                    return None if pd.isna(v) else [type(v).__name__, str(v)]
                return json.dumps({"rows": [[cell(a), cell(b)] for a, b in zip(df[0], df[1])]})
            except Exception as e:
                return _error(e)
        """;

    private static readonly object Gate = new();
    private static PyModule? _scope;

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

    private static JsonDocument Call(string function, params object[] arguments)
    {
        lock (Gate)
        {
            PythonRuntime.EnsureInitialized(PythonRuntimeOptions.FromEnvironment());
            using (Py.GIL())
            {
                if (_scope is null)
                {
                    _scope = Py.CreateScope();
                    _scope.Exec(Code);
                }

                var converted = arguments.Select(ToPython).ToArray();
                try
                {
                    using var callable = _scope.Get(function);
                    using var result = callable.Invoke(converted);
                    return JsonDocument.Parse(result.As<string>());
                }
                finally
                {
                    foreach (var item in converted) item.Dispose();
                }
            }
        }
    }

    private static PyObject ToPython(object value) => value switch
    {
        string text => new PyString(text),
        int number => new PyInt(number),
        IEnumerable<string> list => ToList(list),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static PyList ToList(IEnumerable<string> items)
    {
        var list = new PyList();
        foreach (var item in items)
        {
            using var text = new PyString(item);
            list.Append(text);
        }
        return list;
    }
}
