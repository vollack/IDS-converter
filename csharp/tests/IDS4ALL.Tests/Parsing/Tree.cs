using System.Text;
using System.Text.Json;

namespace IDS4ALL.Tests.Parsing;

/// <summary>
/// Plain values for the comparison with Python: string, bool, null, or <c>List&lt;object?&gt;</c>.
/// A dictionary is a list of [key, value] pairs, so that the order of the keys is part of the comparison.
/// </summary>
internal static class Tree
{
    public static object? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray().Select(FromJson).ToList(),
        JsonValueKind.Object => element.EnumerateObject().Select(p => (object?)new List<object?> { p.Name, FromJson(p.Value) }).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(element), element.ValueKind, "Unexpected JSON value."),
    };

    /// <summary>The value of a key in a list of [key, value] pairs, or null.</summary>
    public static object? Get(object? node, string key) =>
        node is List<object?> pairs
            ? pairs.OfType<List<object?>>().FirstOrDefault(pair => pair.Count == 2 && pair[0] is string name && name == key)?[1]
            : null;

    public static List<object?> Obj(params (string Key, object? Value)[] pairs) =>
        pairs.Select(pair => (object?)new List<object?> { pair.Key, pair.Value }).ToList();

    public static List<object?> Pairs<T>(OrderedDictionary<string, T> dictionary, Func<T, object?> convert) =>
        dictionary.Select(pair => (object?)new List<object?> { pair.Key, convert(pair.Value) }).ToList();

    public static List<object?> Strings(IEnumerable<string> values) => values.Cast<object?>().ToList();

    public static List<object?> Items<T>(IEnumerable<T> items, Func<T, object?> convert) => items.Select(convert).ToList();

    /// <summary>The path of the first difference, or null if both trees are equal.</summary>
    public static string? FirstDifference(object? expected, object? actual, string path = "$")
    {
        switch (expected, actual)
        {
            case (null, null):
                return null;
            case (string a, string b):
                return a == b ? null : $"{path}: Python {Quote(a)} but C# {Quote(b)}";
            case (bool a, bool b):
                return a == b ? null : $"{path}: Python {a} but C# {b}";
            case (List<object?> a, List<object?> b):
                for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
                {
                    var difference = FirstDifference(a[i], b[i], $"{path}[{i}]");
                    if (difference is not null) return difference;
                }
                return a.Count == b.Count ? null : $"{path}: Python has {a.Count} items but C# has {b.Count}";
            default:
                return $"{path}: Python {Describe(expected)} but C# {Describe(actual)}";
        }
    }

    public static string Describe(object? value) => value switch
    {
        null => "null",
        string text => Quote(text),
        bool flag => flag ? "true" : "false",
        List<object?> list => "[" + string.Join(", ", list.Select(Describe)) + "]",
        _ => value.ToString() ?? "",
    };

    private static string Quote(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in text)
            builder.Append(c < ' ' || c > '~' ? $"\\u{(int)c:x4}" : c == '"' ? "\\\"" : c.ToString());
        return builder.Append('"').ToString();
    }
}
