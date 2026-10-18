using System.Text.Json;
using IDS4ALL.Core.Merging;
using IDS4ALL.Tests.Parsing;

namespace IDS4ALL.Tests.Merging;

/// <summary>
/// Runs the specification functions of custom_functions.py through Python.NET. The results are trees, see <see cref="Tree"/>:
/// a dictionary is a list of [key, value] pairs. A result is {"ok": value} or {"error": text}.
/// </summary>
internal static class MergingReference
{
    private const string Code = """
        import json, sys, importlib

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

        def _spec(s):
            return [["app", _shape(s["app"])], ["req", _shape(s["req"])], ["general", _shape(s["general"])],
                    ["spec", _shape(s["spec"])], ["app_splitted", s["app_splitted"]], ["sep_string", s["sep_string"]]]

        def specs(root, path, sheet, skip, ifc, entity_based, separate_by_json):
            try:
                cf = _cf(root)
                result = cf.excel_to_spec_list(path, sheet, json.loads(separate_by_json), skip, ifc, bool(entity_based))
                descriptions = [[[k[0], k[1]], v] for k, v in cf.property_description_dict.items()]
                return json.dumps({"ok": [["specs", [_spec(s) for s in result]], ["descriptions", descriptions]]})
            except Exception as e:
                return json.dumps(_failure(e))

        def requirements(root, cases_json):
            cf = _cf(root)
            out = []
            for case in json.loads(cases_json):
                first = {k: list(v) for k, v in case["first"]}
                second = {k: list(v) for k, v in case["second"]}
                try:
                    different = cf.compare_and_merge_requirement_dicts(first, second, case["notCompared"], case["mergeOnlyValues"], case["reverse"])
                    out.append({"ok": [["different", different], ["first", _shape(first)], ["second", _shape(second)]]})
                except Exception as e:
                    out.append(_failure(e))
            return json.dumps(out)
        """;

    private static readonly PythonScript Script = new(Code);

    public static object? Specs(string path, string sheet, int skipRows, string ifcVersions, bool entityBased, IReadOnlyList<string> separateBy)
    {
        using var document = Script.Call("specs", RepositoryRoot.Path, path, sheet, skipRows, ifcVersions, entityBased, JsonSerializer.Serialize(separateBy));
        return Tree.FromJson(document.RootElement);
    }

    public static IReadOnlyList<object?> Requirements(IEnumerable<object> cases)
    {
        using var document = Script.Call("requirements", RepositoryRoot.Path, JsonSerializer.Serialize(cases));
        return document.RootElement.EnumerateArray().Select(Tree.FromJson).ToList();
    }

    // ---- C# results as trees ----------------------------------------------------------------------

    public static object? ToTree(SpecificationList list) => Tree.Obj(
        ("specs", Tree.Items(list.Specifications, ToTree)),
        ("descriptions", Tree.Items(list.Descriptions.Entries, entry => new List<object?>
        {
            new List<object?> { entry.Key.PropertySet, entry.Key.Name },
            entry.Value,
        })));

    public static object? ToTree(SpecificationDraft spec) => Tree.Obj(
        ("app", Tree.Items(spec.App, ToTree)),
        ("req", Tree.Items(spec.Req, ToTree)),
        ("general", ToTree(spec.General)),
        ("spec", ToTree(spec.Spec)),
        ("app_splitted", spec.AppSplitted),
        ("sep_string", spec.SepString));

    public static List<object?> ToTree(FacetData data) => Tree.Pairs(data, values => Tree.Strings(values));
}
