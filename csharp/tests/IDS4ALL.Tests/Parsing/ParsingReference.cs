using System.Text.Json;
using IDS4ALL.Core.Parsing;

namespace IDS4ALL.Tests.Parsing;

/// <summary>
/// Runs the Python functions of custom_functions.py through Python.NET. It is the reference for the differential tests.
/// Results are trees: a dictionary is a list of [key, value] pairs, a list is a list. See <see cref="Tree"/>.
/// </summary>
internal static class ParsingReference
{
    private const string Code = """
        import json, sys, importlib
        import pandas as pd

        class _Captured(Exception):
            pass

        def _cf(root):
            if root not in sys.path:
                sys.path.insert(0, root)
            return importlib.import_module("custom_functions")

        def _shape(value):
            if isinstance(value, dict):
                return [[k, _shape(v)] for k, v in value.items()]
            if isinstance(value, (list, tuple)):
                return [_shape(v) for v in value]
            return value

        def _failure(e):
            return {"error": type(e).__name__ + ": " + str(e)}

        def _guarded(function, *args):
            try:
                return {"ok": function(*args)}
            except Exception as e:
                return _failure(e)

        def _capture(cf, args):
            holder = []

            def local(frame, event, arg):
                if 'df_final' in frame.f_locals:
                    holder.append(frame.f_locals['df_final'])
                    raise _Captured()
                return local

            def outer(frame, event, arg):
                return local if frame.f_code is cf.excel_to_spec_list.__code__ else None

            sys.settrace(outer)
            try:
                cf.excel_to_spec_list(*args)
            except _Captured:
                pass
            finally:
                sys.settrace(None)
            return holder[0]

        def _row_pipeline(cf, row, entity_based):
            dict_list = cf.pandas_row_to_dict_list(row)
            stages = {"dicts": _shape(dict_list), "orAnd": [], "facets": []}
            for item in dict_list:
                or_and = cf.split_OR_AND_values(item, entity_based)
                stages["orAnd"].append(_shape(or_and))
                stages["facets"].append(_shape(cf.split_AND_values_to_individual_facet_dicts(or_and)))
            return stages

        def _series(columns, cells):
            values = [list(v) if is_list else v[0] for is_list, v in cells]
            return pd.Series(values, index=columns, dtype=object)

        def group(root, path, sheet, skip, ifc, entity_based, separate_by_json):
            try:
                cf = _cf(root)
                df = _capture(cf, (path, sheet, json.loads(separate_by_json), skip, ifc, bool(entity_based)))
                return json.dumps({"ok": {
                    "columns": [str(c) for c in df.columns],
                    "rows": [[[isinstance(v, list), list(v) if isinstance(v, list) else [v]] for v in row]
                             for row in df.itertuples(index=False, name=None)],
                }})
            except Exception as e:
                return json.dumps(_failure(e))

        def facets(root, path, sheet, skip, ifc, entity_based, separate_by_json, groups_json):
            try:
                cf = _cf(root)
                groups = json.loads(groups_json)
                df = _capture(cf, (path, sheet, json.loads(separate_by_json), skip, ifc, bool(entity_based)))
                rows = range(df.index.size)
                result = {
                    "dictionary": [[_guarded(lambda cols=cols, i=i: _shape(cf.pandas_row_to_dict(df[cols].iloc[i]))) for i in rows]
                                   for cols in groups["dictionary"]],
                    "facets": [[_guarded(_row_pipeline, cf, df[cols].iloc[i], bool(entity_based)) for i in rows]
                               for cols in groups["facets"]],
                }
                return json.dumps({"ok": result})
            except Exception as e:
                return json.dumps(_failure(e))

        def pipeline(root, rows_json, entity_based):
            cf = _cf(root)
            rows = json.loads(rows_json)
            return json.dumps([_guarded(_row_pipeline, cf, _series(r["columns"], r["cells"]), bool(entity_based)) for r in rows])

        def row_to_dict(root, rows_json):
            cf = _cf(root)
            rows = json.loads(rows_json)
            return json.dumps([_guarded(lambda r=r: _shape(cf.pandas_row_to_dict(_series(r["columns"], r["cells"])))) for r in rows])

        def separate_value(root, cases_json):
            cf = _cf(root)
            out = []
            for case in json.loads(cases_json):
                dictionary = {k: list(v) for k, v in case["dict"]}
                def run():
                    cf.separate_dict_value(dictionary, case["combined"], case["second"], case["delimiter"])
                    return _shape(dictionary)
                out.append(_guarded(run))
            return json.dumps(out)

        def combinations(root, cases_json):
            cf = _cf(root)
            out = []
            for case in json.loads(cases_json):
                items = [{k: list(v) for k, v in item} for item in case]
                out.append(_guarded(lambda items=items: _shape(cf.generate_combinations(items))))
            return json.dumps(out)

        def text(root, values_json):
            cf = _cf(root)
            return json.dumps([{"strip": v.strip(), "upper": v.upper(), "repr": repr(v), "list": repr([v, v]),
                                "complex": cf.is_complex_restriction(v)} for v in json.loads(values_json)])
        """;

    private static readonly PythonScript Script = new(Code);

    /// <summary>The grouped dataframe df_final: the columns and, for each row, (is a list, values) per column.</summary>
    public static object? Group(string path, string sheet, int skipRows, string ifcVersions, bool entityBased, IReadOnlyList<string> separateBy) =>
        Run("group", RepositoryRoot.Path, path, sheet, skipRows, ifcVersions, entityBased, Json(separateBy));

    /// <summary>The results of the facet functions for all rows of df_final, for the given column groups.</summary>
    public static object? Facets(string path, string sheet, int skipRows, string ifcVersions, bool entityBased,
        IReadOnlyList<string> separateBy, IReadOnlyList<IReadOnlyList<string>> dictionaryGroups, IReadOnlyList<IReadOnlyList<string>> facetGroups) =>
        Run("facets", RepositoryRoot.Path, path, sheet, skipRows, ifcVersions, entityBased, Json(separateBy),
            Json(new Dictionary<string, object> { ["dictionary"] = dictionaryGroups, ["facets"] = facetGroups }));

    public static object? Pipeline(IReadOnlyList<SyntheticRow> rows, bool entityBased) =>
        Run("pipeline", RepositoryRoot.Path, Json(rows.Select(Describe)), entityBased);

    public static object? RowToDictionary(IReadOnlyList<SyntheticRow> rows) =>
        Run("row_to_dict", RepositoryRoot.Path, Json(rows.Select(Describe)));

    public static object? SeparateValue(IEnumerable<object> cases) => Run("separate_value", RepositoryRoot.Path, Json(cases));

    public static object? Combinations(IEnumerable<object> cases) => Run("combinations", RepositoryRoot.Path, Json(cases));

    public static object? Text(IEnumerable<string> values) => Run("text", RepositoryRoot.Path, Json(values));

    private static object Describe(SyntheticRow row) => new
    {
        columns = row.Cells.Select(cell => cell.Column),
        cells = row.Cells.Select(cell => new object[] { row.IsList, cell.Values }),
    };

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static object? Run(string function, params object[] arguments)
    {
        using var document = Script.Call(function, arguments);
        return Tree.FromJson(document.RootElement);
    }
}

/// <summary>A row for the function tests. All cells are lists with the same length, or all are single values.</summary>
internal sealed record SyntheticRow(IReadOnlyList<Cell> Cells, bool IsList);
