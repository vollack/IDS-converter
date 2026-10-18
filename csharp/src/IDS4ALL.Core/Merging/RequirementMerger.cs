using IDS4ALL.Core.Parsing;
using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Core.Merging;

/// <summary>Ports of the deepmerge Merger of custom_functions.py and of compare_and_merge_requirement_dicts.</summary>
public static class RequirementMerger
{
    /// <summary>
    /// merger.merge(baseData, next) for dictionaries of lists: a dictionary merges into the base dictionary (changed in place),
    /// a key that is new takes the list of <paramref name="next"/> itself, a key that exists gets a new list with the new values appended (append_unique).
    /// </summary>
    public static FacetData Merge(FacetData baseData, FacetData next)
    {
        foreach (var (key, values) in next)
            baseData[key] = baseData.TryGetValue(key, out var existing) ? AppendUnique(existing, values) : values;
        return baseData;
    }

    /// <summary>deepmerge append_unique: the items of next that are not in the original list. Repeated items of next stay.</summary>
    private static List<string> AppendUnique(List<string> baseList, List<string> next)
    {
        var known = baseList.ToHashSet();
        return [.. baseList, .. next.Where(item => !known.Contains(item))];
    }

    /// <summary>
    /// Port of compare_and_merge_requirement_dicts. Checks whether <paramref name="first"/> is a subset of <paramref name="second"/>
    /// (with <paramref name="reverse"/> the other way round). The lists of the keys in <paramref name="notComparedKeys"/> are not compared.
    /// If it is a subset, <paramref name="second"/> is merged into <paramref name="first"/>.
    /// </summary>
    /// <returns>True if the dictionaries are different (nothing was merged).</returns>
    public static bool CompareAndMerge(FacetData first, FacetData second, IReadOnlyList<string> notComparedKeys, bool mergeOnlyValues, bool reverse = false)
    {
        var diff = false;
        var sameFacet = false;

        // A complex restriction cannot be in an enumeration, so requirements with one are never merged.
        foreach (var key in notComparedKeys)
        {
            if (first.TryGetValue(key, out var firstValues) && firstValues.Count > 0 && FacetParser.IsComplexRestriction(firstValues[0])) diff = true;
            if (second.TryGetValue(key, out var secondValues) && secondValues.Count > 0 && FacetParser.IsComplexRestriction(secondValues[0])) diff = true;
        }

        var looped = reverse ? second : first;
        var reference = reverse ? first : second;

        foreach (var key in looped.Keys)
        {
            if (key == Description) continue;

            // To merge only values, all keys except the description must occur in both dictionaries.
            if (mergeOnlyValues)
            {
                var keys1 = looped.Keys.Where(k => k != Description).ToHashSet();
                var keys2 = reference.Keys.Where(k => k != Description).ToHashSet();
                if (!keys1.SetEquals(keys2))
                {
                    diff = true;
                    break;
                }
            }

            if (reference.ContainsKey(key))
            {
                sameFacet = true;
                if (notComparedKeys.Contains(key)) continue;
                if (!first[key].SequenceEqual(second[key])) diff = true;
            }
            else
            {
                diff = true;
            }
        }

        if (!diff && sameFacet)
        {
            Merge(first, second);
            return false;
        }
        return true;
    }
}
