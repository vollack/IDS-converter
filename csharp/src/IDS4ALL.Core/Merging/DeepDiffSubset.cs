using System.Globalization;

namespace IDS4ALL.Core.Merging;

public enum DiffKind { DictionaryItemAdded, DictionaryItemRemoved, IterableItemAdded, IterableItemRemoved, ValuesChanged }

/// <summary>Which kinds of change DeepDiff(a, b, ignore_order=True) reports for two applicabilities.</summary>
public sealed record ApplicabilityDiff(bool DictionaryItemAdded, bool DictionaryItemRemoved, bool IterableItemAdded, bool IterableItemRemoved, bool ValuesChanged);

/// <summary>
/// The part of the Python library DeepDiff (version 8.6.2) that custom_functions.py uses: text, list and dictionary values,
/// no type changes. The comparison with ignore_order=True pairs changed items like DeepDiff does (rough distance below 0.3).
/// </summary>
public static class DeepDiffSubset
{
    private const double CutoffDistanceForPairs = 0.3;
    private const double CutoffIntersectionForPairs = 0.7;
    private const double ThresholdToDiffDeeper = 0.33;

    /// <summary>
    /// DeepDiff(a, b, include_paths=keys) or, with ignoreOrder, DeepDiff(a, b, ignore_order=True, include_paths=keys), for dictionaries of lists of text.
    /// True if DeepDiff returns a difference. Without ignore_order lists compare in order, with it they compare as sets.
    /// </summary>
    public static bool IncludedPathsDiffer(FacetData a, FacetData b, IEnumerable<string> keys, bool ignoreOrder)
    {
        foreach (var key in keys)
        {
            var inA = a.TryGetValue(key, out var listA);
            var inB = b.TryGetValue(key, out var listB);
            if (inA != inB) return true;
            if (!inA) continue;
            if (ignoreOrder ? !listA!.ToHashSet().SetEquals(listB!) : !listA!.SequenceEqual(listB!)) return true;
        }
        return false;
    }

    /// <summary>DeepDiff(a, b) without options is empty exactly when the lists are equal (Python ==).</summary>
    public static bool ListsDiffer(IReadOnlyList<FacetData> a, IReadOnlyList<FacetData> b)
    {
        if (a.Count != b.Count) return true;
        for (var i = 0; i < a.Count; i++)
            if (!FacetEquals(a[i], b[i])) return true;
        return false;
    }

    public static bool FacetEquals(FacetData a, FacetData b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && pair.Value.SequenceEqual(other));

    /// <summary>The change kinds of DeepDiff(a, b, ignore_order=True).</summary>
    public static ApplicabilityDiff CompareApplicability(IReadOnlyList<FacetData> a, IReadOnlyList<FacetData> b)
    {
        var paths = Compare(DiffNode.From(a), DiffNode.From(b));
        return new ApplicabilityDiff(
            paths[DiffKind.DictionaryItemAdded].Count > 0,
            paths[DiffKind.DictionaryItemRemoved].Count > 0,
            paths[DiffKind.IterableItemAdded].Count > 0,
            paths[DiffKind.IterableItemRemoved].Count > 0,
            paths[DiffKind.ValuesChanged].Count > 0);
    }

    /// <summary>The paths of the changes of DeepDiff(t1, t2, ignore_order=True), sorted, for each kind of change.</summary>
    public static IReadOnlyDictionary<DiffKind, IReadOnlyList<string>> Compare(DiffNode t1, DiffNode t2)
    {
        var reports = Run(t1, t2);
        return Enum.GetValues<DiffKind>().ToDictionary(
            kind => kind,
            kind => (IReadOnlyList<string>)reports.Where(r => r.Kind == kind).Select(r => r.Path).Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>DeepDiff._get_rough_distance of the pair: the size of the delta divided by the sizes of both values.</summary>
    public static double RoughDistance(DiffNode t1, DiffNode t2)
    {
        var length = DeltaLength(Run(t1, t2));
        return length == 0 ? 0 : length / (double)(t1.Count + t2.Count);
    }

    // ---- the diff --------------------------------------------------------------------------

    private sealed class Report(DiffKind kind, string path, DiffNode? t1, DiffNode? t2)
    {
        public DiffKind Kind { get; set; } = kind;
        public string Path { get; } = path;
        public DiffNode? T1 { get; } = t1;
        public DiffNode? T2 { get; set; } = t2;
    }

    private static List<Report> Run(DiffNode t1, DiffNode t2)
    {
        var reports = new List<Report>();
        Diff(t1, t2, "root", reports);
        MutualAddsAndRemovesBecomeValueChanges(reports);
        return reports;
    }

    /// <summary>An item added and an item removed at the same path are reported as one changed value.</summary>
    private static void MutualAddsAndRemovesBecomeValueChanges(List<Report> reports)
    {
        var added = reports.Where(r => r.Kind == DiffKind.IterableItemAdded).GroupBy(r => r.Path).ToDictionary(g => g.Key, g => g.Last());
        var removed = reports.Where(r => r.Kind == DiffKind.IterableItemRemoved).GroupBy(r => r.Path).ToDictionary(g => g.Key, g => g.Last());
        foreach (var path in added.Keys.Where(removed.ContainsKey))
        {
            reports.Remove(added[path]);
            removed[path].Kind = DiffKind.ValuesChanged;
            removed[path].T2 = added[path].T2;
        }
    }

    /// <summary>The size of the delta view. Entries with the same path overwrite each other.</summary>
    private static int DeltaLength(List<Report> reports)
    {
        var entries = new Dictionary<(string Group, string Path), DiffNode>();
        foreach (var report in reports)
        {
            var item = report.T2 ?? report.T1!;
            var group = report.Kind switch
            {
                DiffKind.IterableItemAdded => "added_at_indexes",
                DiffKind.IterableItemRemoved => "removed_at_indexes",
                _ => report.Kind.ToString(),
            };
            entries[(group, report.Path)] = item;
        }
        return entries.Values.Sum(item => item.Length);
    }

    private static void Diff(DiffNode t1, DiffNode t2, string path, List<Report> reports)
    {
        if (ReferenceEquals(t1, t2)) return;
        switch (t1, t2)
        {
            case (DiffText text1, DiffText text2):
                if (text1.Value != text2.Value) reports.Add(new Report(DiffKind.ValuesChanged, path, t1, t2));
                break;
            case (DiffMap map1, DiffMap map2):
                DiffMapValues(map1, map2, path, reports);
                break;
            case (DiffList list1, DiffList list2):
                DiffList(list1, list2, path, reports);
                break;
            default:
                throw new NotSupportedException($"The values at {path} have different types. This subset of DeepDiff does not report type changes.");
        }
    }

    private static void DiffMapValues(DiffMap t1, DiffMap t2, string path, List<Report> reports)
    {
        var values1 = t1.Entries.ToDictionary(e => e.Key, e => e.Value);
        var values2 = t2.Entries.ToDictionary(e => e.Key, e => e.Value);
        var intersect = t2.Entries.Select(e => e.Key).Where(values1.ContainsKey).ToList();
        var added = t2.Entries.Select(e => e.Key).Where(key => !values1.ContainsKey(key)).ToList();
        var removed = t1.Entries.Select(e => e.Key).Where(key => !values2.ContainsKey(key)).ToList();

        var union = added.Count + removed.Count + intersect.Count;
        if (union > 1 && intersect.Count / (double)union < ThresholdToDiffDeeper)
        {
            reports.Add(new Report(DiffKind.ValuesChanged, path, t1, t2));
            return;
        }

        foreach (var key in added) reports.Add(new Report(DiffKind.DictionaryItemAdded, ChildPath(path, key), null, values2[key]));
        foreach (var key in removed) reports.Add(new Report(DiffKind.DictionaryItemRemoved, ChildPath(path, key), values1[key], null));
        foreach (var key in intersect) Diff(values1[key], values2[key], ChildPath(path, key), reports);
    }

    private static string ChildPath(string path, string key) => path + "['" + key + "']";

    private static string ChildPath(string path, int index) => path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    private sealed record Indexed(int Index, DiffNode Item);

    private static OrderedDictionary<string, Indexed> HashTable(DiffList list)
    {
        var table = new OrderedDictionary<string, Indexed>();
        for (var i = 0; i < list.Items.Count; i++)
            table.TryAdd(list.Items[i].Canonical, new Indexed(i, list.Items[i]));
        return table;
    }

    /// <summary>Port of _diff_iterable_with_deephash with report_repetition=False.</summary>
    private static void DiffList(DiffList t1, DiffList t2, string path, List<Report> reports)
    {
        var table1 = HashTable(t1);
        var table2 = HashTable(t2);
        var added = table2.Keys.Where(hash => !table1.ContainsKey(hash)).ToList();
        var removed = table1.Keys.Where(hash => !table2.ContainsKey(hash)).ToList();

        var getPairs = !((added.Count + removed.Count) / (double)(table1.Count + table2.Count + 1) > CutoffIntersectionForPairs);
        var pairs = getPairs ? MostInCommonPairs(added, removed, table1, table2) : [];

        foreach (var hash in added.ToList())
        {
            var other = TakePair(pairs, hash, removed);
            var item2 = table2[hash];
            if (other is null)
            {
                reports.Add(new Report(DiffKind.IterableItemAdded, ChildPath(path, item2.Index), null, item2.Item));
            }
            else
            {
                var item1 = table1[other];
                Diff(item1.Item, item2.Item, ChildPath(path, item1.Index), reports);
            }
        }

        foreach (var hash in removed.ToList())
        {
            var other = TakePair(pairs, hash, added);
            var item1 = table1[hash];
            if (other is null) reports.Add(new Report(DiffKind.IterableItemRemoved, ChildPath(path, item1.Index), item1.Item, null));
            else Diff(item1.Item, table2[other].Item, ChildPath(path, item1.Index), reports);
        }
    }

    /// <summary>get_other_pair: takes the pair of the hash out of the pairs and out of the hashes of the other side.</summary>
    private static string? TakePair(Dictionary<string, string> pairs, string hash, List<string> otherHashes)
    {
        if (!pairs.Remove(hash, out var other)) return null;
        pairs.Remove(other);
        otherHashes.Remove(other);
        return other;
    }

    /// <summary>
    /// Port of _get_most_in_common_pairs_in_iterables. Sets pop their last item. A from-hash keeps the last unused
    /// candidate that it pops, and the earlier candidates stay marked as used.
    /// </summary>
    private static Dictionary<string, string> MostInCommonPairs(
        List<string> added, List<string> removed, OrderedDictionary<string, Indexed> table1, OrderedDictionary<string, Indexed> table2)
    {
        var mostInCommon = new OrderedDictionary<string, OrderedDictionary<double, List<string>>>();
        foreach (var addedHash in added)
        {
            foreach (var removedHash in removed)
            {
                var distance = RoughDistance(table1[removedHash].Item, table2[addedHash].Item);
                if (distance >= CutoffDistanceForPairs) continue;

                if (!mostInCommon.TryGetValue(addedHash, out var byDistance))
                    mostInCommon[addedHash] = byDistance = [];
                if (!byDistance.TryGetValue(distance, out var candidates))
                    byDistance[distance] = candidates = [];
                if (!candidates.Contains(removedHash)) candidates.Add(removedHash);
            }
        }

        var fromHashesByDistance = new Dictionary<double, List<string>>();
        foreach (var (fromHash, byDistance) in mostInCommon)
        {
            foreach (var distance in byDistance.Keys)
            {
                if (!fromHashesByDistance.TryGetValue(distance, out var fromHashes))
                    fromHashesByDistance[distance] = fromHashes = [];
                if (!fromHashes.Contains(fromHash)) fromHashes.Add(fromHash);
            }
        }

        var used = new HashSet<string>();
        var pairs = new Dictionary<string, string>();
        foreach (var distance in fromHashesByDistance.Keys.Order())
        {
            var fromHashes = fromHashesByDistance[distance];
            while (fromHashes.Count > 0)
            {
                var fromHash = Pop(fromHashes);
                if (used.Contains(fromHash)) continue;
                var toHashes = mostInCommon[fromHash][distance];
                while (toHashes.Count > 0)
                {
                    var toHash = Pop(toHashes);
                    if (used.Contains(toHash)) continue;
                    used.Add(fromHash);
                    used.Add(toHash);
                    pairs[fromHash] = toHash;
                }
            }
        }

        foreach (var (from, to) in pairs.ToList()) pairs[to] = from;
        return pairs;
    }

    private static string Pop(List<string> items)
    {
        var last = items[^1];
        items.RemoveAt(items.Count - 1);
        return last;
    }
}
