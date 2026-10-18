using System.Text.Json;
using IDS4ALL.Core.Merging;

namespace IDS4ALL.Tests.Merging;

/// <summary>Compares <see cref="DeepDiffSubset"/> with the Python library DeepDiff on generated values.</summary>
public sealed class DeepDiffEquivalenceTests
{
    private const string Code = """
        import json
        from deepdiff import DeepDiff
        from deepdiff.helper import DELTA_VIEW

        KINDS = ["dictionary_item_added", "dictionary_item_removed", "iterable_item_added", "iterable_item_removed", "values_changed"]

        def build(node):
            if isinstance(node, str):
                return node
            if isinstance(node, list):
                return [build(item) for item in node]
            return {key: build(value) for key, value in node["d"]}

        def compare(cases_json):
            out = []
            for a, b in json.loads(cases_json):
                t1, t2 = build(a), build(b)
                result = DeepDiff(t1, t2, ignore_order=True)
                distance = DeepDiff(t1, t2, ignore_order=True, view=DELTA_VIEW, cache_purge_level=0)._get_rough_distance()
                out.append({"kinds": {kind: sorted(list(result.get(kind, []))) for kind in KINDS}, "distance": distance})
            return json.dumps(out)

        def included(cases_json):
            out = []
            for case in json.loads(cases_json):
                result = DeepDiff(case["a"], case["b"], ignore_order=case["ignoreOrder"], include_paths=case["keys"])
                out.append(bool(result))
            return json.dumps(out)
        """;

    private static readonly PythonScript Script = new(Code);

    private static readonly string[] Words = ["A", "B", "C", "D", "E", "F"];
    private static readonly string[] Keys = ["Entity", "PredefinedType", "Property", "PropertySet", "Attribute"];

    private static int Seed => int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_SEED"), out var seed) ? seed : 20261018;

    private static int Runs => int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_RUNS"), out var runs) ? runs : 150;

    [PythonFact]
    public void ApplicabilityLists_ReportTheSameChanges()
    {
        var random = new Random(Seed);
        var cases = Enumerable.Range(0, Runs * 4).Select(_ => AppPair(random)).ToList();
        AssertSame(cases);
    }

    [PythonFact]
    public void DictionariesAndNestedLists_ReportTheSameChanges()
    {
        var random = new Random(Seed + 1);
        var cases = new List<(DiffNode, DiffNode)>();
        for (var i = 0; i < Runs * 2; i++)
        {
            var map = Map(random);
            cases.Add((map, MutateMap(random, map)));
            var nested = new DiffList(Enumerable.Range(0, random.Next(0, 5)).Select(_ => (DiffNode)TextList(random)).ToList());
            cases.Add((nested, MutateItems(random, nested, node => MutateItems(random, (DiffList)node, _ => Text(random), () => Text(random)), () => TextList(random))));
            var flat = TextList(random);
            cases.Add((flat, MutateItems(random, flat, node => Text(random), () => Text(random))));
        }
        AssertSame(cases);
    }

    [PythonFact]
    public void SimilarApplicabilities_ArePairedLikeDeepDiff()
    {
        var random = new Random(Seed + 3);
        var cases = Enumerable.Range(0, Runs * 4).Select(_ => ClusterPair(random)).ToList();
        AssertSame(cases);
    }

    [PythonFact]
    public void IncludedPaths_DifferLikeDeepDiff()
    {
        var random = new Random(Seed + 2);
        var cases = new List<(FacetData A, FacetData B, string[] Keys, bool IgnoreOrder)>();
        for (var i = 0; i < Runs * 3; i++)
        {
            var a = FacetMap(random);
            var b = random.Next(3) == 0 ? FacetMap(random) : MutateFacet(random, a);
            var keys = Keys.Where(_ => random.Next(2) == 0).DefaultIfEmpty(Keys[random.Next(Keys.Length)]).ToArray();
            cases.Add((a, b, keys, random.Next(2) == 0));
        }

        var payload = JsonSerializer.Serialize(cases.Select(c => new { a = c.A, b = c.B, keys = c.Keys, ignoreOrder = c.IgnoreOrder }));
        using var document = Script.Call("included", payload);
        var expected = document.RootElement.EnumerateArray().Select(e => e.GetBoolean()).ToList();
        for (var i = 0; i < cases.Count; i++)
        {
            var actual = DeepDiffSubset.IncludedPathsDiffer(cases[i].A, cases[i].B, cases[i].Keys, cases[i].IgnoreOrder);
            Assert.True(expected[i] == actual,
                $"Case {i}: DeepDiff {expected[i]} but C# {actual}. keys={string.Join(",", cases[i].Keys)} ignoreOrder={cases[i].IgnoreOrder}\n{JsonSerializer.Serialize(cases[i].A)}\n{JsonSerializer.Serialize(cases[i].B)}");
        }
    }

    private static void AssertSame(List<(DiffNode A, DiffNode B)> cases)
    {
        var payload = JsonSerializer.Serialize(cases.Select(c => new[] { ToJson(c.A), ToJson(c.B) }));
        using var document = Script.Call("compare", payload);
        var results = document.RootElement.EnumerateArray().ToList();
        for (var i = 0; i < cases.Count; i++)
        {
            var (a, b) = cases[i];
            var actual = DeepDiffSubset.Compare(a, b);
            foreach (var kind in Enum.GetValues<DiffKind>())
            {
                var expected = results[i].GetProperty("kinds").GetProperty(PythonName(kind)).EnumerateArray().Select(e => e.GetString()!).ToList();
                Assert.True(expected.SequenceEqual(actual[kind]),
                    $"Case {i}, {kind}: DeepDiff [{string.Join(", ", expected)}] but C# [{string.Join(", ", actual[kind])}]\n{JsonSerializer.Serialize(ToJson(a))}\n{JsonSerializer.Serialize(ToJson(b))}");
            }

            var expectedDistance = results[i].GetProperty("distance").GetDouble();
            var actualDistance = DeepDiffSubset.RoughDistance(a, b);
            Assert.True(expectedDistance == actualDistance,
                $"Case {i}: distance DeepDiff {expectedDistance} but C# {actualDistance}\n{JsonSerializer.Serialize(ToJson(a))}\n{JsonSerializer.Serialize(ToJson(b))}");
        }
    }

    private static string PythonName(DiffKind kind) => kind switch
    {
        DiffKind.DictionaryItemAdded => "dictionary_item_added",
        DiffKind.DictionaryItemRemoved => "dictionary_item_removed",
        DiffKind.IterableItemAdded => "iterable_item_added",
        DiffKind.IterableItemRemoved => "iterable_item_removed",
        _ => "values_changed",
    };

    private static object ToJson(DiffNode node) => node switch
    {
        DiffText text => text.Value,
        DiffList list => list.Items.Select(ToJson).ToList(),
        DiffMap map => new { d = map.Entries.Select(e => new object[] { e.Key, ToJson(e.Value) }).ToList() },
        _ => throw new ArgumentOutOfRangeException(nameof(node)),
    };

    // ---- generators -----------------------------------------------------------------------

    private static (DiffNode, DiffNode) AppPair(Random random)
    {
        var app = new DiffList(Enumerable.Range(0, random.Next(1, 5)).Select(_ => (DiffNode)Map(random)).ToList());
        DiffNode other = random.Next(4) == 0
            ? new DiffList(Enumerable.Range(0, random.Next(1, 5)).Select(_ => (DiffNode)Map(random)).ToList())
            : MutateItems(random, app, node => MutateMap(random, (DiffMap)node), () => Map(random));
        return (app, other);
    }

    /// <summary>Two lists of variants of one facet. Many pairs have the same distance, so the tie rules matter.</summary>
    private static (DiffNode, DiffNode) ClusterPair(Random random)
    {
        var baseMap = new DiffMap(Keys.Take(random.Next(2, 5))
            .Select(key => new KeyValuePair<string, DiffNode>(key, new DiffList(Enumerable.Range(0, random.Next(2, 5)).Select(_ => (DiffNode)Text(random)).ToList())))
            .ToList());
        DiffList Variants() => new(Enumerable.Range(0, random.Next(1, 5)).Select(_ => (DiffNode)Variant(random, baseMap)).ToList());
        return (Variants(), Variants());
    }

    private static DiffMap Variant(Random random, DiffMap map)
    {
        var entries = map.Entries.ToList();
        var index = random.Next(entries.Count);
        var words = ((DiffList)entries[index].Value).Items.ToList();
        switch (random.Next(3))
        {
            case 0:
                words[random.Next(words.Count)] = Text(random);
                break;
            case 1:
                words.Add(Text(random));
                break;
            default:
                words.RemoveAt(random.Next(words.Count));
                break;
        }
        entries[index] = new KeyValuePair<string, DiffNode>(entries[index].Key, new DiffList(words));
        return new DiffMap(entries);
    }

    private static DiffText Text(Random random) => new(Words[random.Next(Words.Length)]);

    private static DiffList TextList(Random random) => new(Enumerable.Range(0, random.Next(0, 4)).Select(_ => (DiffNode)Text(random)).ToList());

    private static DiffMap Map(Random random)
    {
        var keys = Keys.Where(_ => random.Next(2) == 0).ToList();
        if (keys.Count == 0) keys.Add(Keys[random.Next(Keys.Length)]);
        return new DiffMap(keys.Select(key => new KeyValuePair<string, DiffNode>(key, TextList(random))).ToList());
    }

    private static DiffMap MutateMap(Random random, DiffMap map)
    {
        var entries = map.Entries.ToList();
        switch (random.Next(6))
        {
            case 0 when entries.Count > 1:
                entries.RemoveAt(random.Next(entries.Count));
                break;
            case 1:
                var key = Keys[random.Next(Keys.Length)];
                if (entries.All(e => e.Key != key)) entries.Add(new KeyValuePair<string, DiffNode>(key, TextList(random)));
                break;
            case 2 or 3 when entries.Count > 0:
                var index = random.Next(entries.Count);
                entries[index] = new KeyValuePair<string, DiffNode>(
                    entries[index].Key, MutateItems(random, (DiffList)entries[index].Value, _ => Text(random), () => Text(random)));
                break;
        }
        return new DiffMap(entries);
    }

    private static DiffList MutateItems(Random random, DiffList list, Func<DiffNode, DiffNode> change, Func<DiffNode> create)
    {
        var items = list.Items.ToList();
        for (var step = random.Next(0, 3); step >= 0; step--)
        {
            switch (random.Next(7))
            {
                case 0 when items.Count > 0:
                    items.RemoveAt(random.Next(items.Count));
                    break;
                case 1:
                    items.Insert(random.Next(items.Count + 1), create());
                    break;
                case 2 or 3 when items.Count > 0:
                    var index = random.Next(items.Count);
                    items[index] = change(items[index]);
                    break;
                case 4:
                    items = items.OrderBy(_ => random.Next()).ToList();
                    break;
                case 5 when items.Count > 0:
                    items.Add(items[random.Next(items.Count)]);
                    break;
            }
        }
        return new DiffList(items);
    }

    private static FacetData FacetMap(Random random)
    {
        var map = new FacetData();
        foreach (var key in Keys.Where(_ => random.Next(2) == 0))
            map[key] = Enumerable.Range(0, random.Next(0, 4)).Select(_ => Words[random.Next(Words.Length)]).ToList();
        return map;
    }

    private static FacetData MutateFacet(Random random, FacetData map)
    {
        var copy = new FacetData();
        foreach (var (key, values) in map) copy[key] = [.. values];
        switch (random.Next(5))
        {
            case 0 when copy.Count > 0:
                copy.Remove(copy.GetAt(random.Next(copy.Count)).Key);
                break;
            case 1:
                copy[Keys[random.Next(Keys.Length)]] = [Words[random.Next(Words.Length)]];
                break;
            case 2 when copy.Count > 0:
                copy.GetAt(random.Next(copy.Count)).Value.Reverse();
                break;
            case 3 when copy.Count > 0:
                copy.GetAt(random.Next(copy.Count)).Value.Add(Words[random.Next(Words.Length)]);
                break;
        }
        return copy;
    }
}
