using System.Text;

namespace IDS4ALL.Core.Merging;

/// <summary>A value that <see cref="DeepDiffSubset"/> compares: text, a list or a dictionary with text keys.</summary>
public abstract class DiffNode
{
    private string? _canonical;
    private int? _count;

    /// <summary>Equal canonical forms mean equal DeepHash values with ignore_order=True: a list is a set of its items.</summary>
    internal string Canonical => _canonical ??= BuildCanonical();

    /// <summary>The count that DeepHash stores for the value: text 1, list 1 + items, dictionary 1 + (1 + value) for each key.</summary>
    internal int Count => _count ??= BuildCount();

    /// <summary>DeepDiff _get_item_length of a delta value: text 1, a list or dictionary the sum of its values.</summary>
    internal abstract int Length { get; }

    protected abstract string BuildCanonical();

    protected abstract int BuildCount();

    public static DiffNode From(string text) => new DiffText(text);

    public static DiffNode From(IEnumerable<string> items) => new DiffList(items.Select(From).ToList());

    public static DiffNode From(FacetData data) =>
        new DiffMap(data.Select(pair => new KeyValuePair<string, DiffNode>(pair.Key, From((IEnumerable<string>)pair.Value))).ToList());

    public static DiffNode From(IEnumerable<FacetData> items) => new DiffList(items.Select(From).ToList());

    internal static string Quote(string text) => text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + text;
}

public sealed class DiffText(string value) : DiffNode
{
    public string Value { get; } = value;

    internal override int Length => 1;

    protected override string BuildCanonical() => "s" + Quote(Value);

    protected override int BuildCount() => 1;
}

public sealed class DiffList(IReadOnlyList<DiffNode> items) : DiffNode
{
    public IReadOnlyList<DiffNode> Items { get; } = items;

    internal override int Length => Items.Sum(item => item.Length);

    protected override string BuildCanonical() =>
        "l[" + string.Concat(Items.Select(item => item.Canonical).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(Quote)) + "]";

    protected override int BuildCount() => 1 + Items.Sum(item => item.Count);
}

public sealed class DiffMap(IReadOnlyList<KeyValuePair<string, DiffNode>> entries) : DiffNode
{
    public IReadOnlyList<KeyValuePair<string, DiffNode>> Entries { get; } = entries;

    internal override int Length => Entries.Sum(entry => entry.Value.Length);

    protected override string BuildCanonical()
    {
        var builder = new StringBuilder("d{");
        foreach (var entry in Entries.Select(e => Quote(e.Key) + "=" + Quote(e.Value.Canonical)).Order(StringComparer.Ordinal))
            builder.Append(Quote(entry));
        return builder.Append('}').ToString();
    }

    protected override int BuildCount() => 1 + Entries.Sum(entry => 1 + entry.Value.Count);
}
