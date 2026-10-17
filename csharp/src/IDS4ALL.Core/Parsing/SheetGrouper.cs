using IDS4ALL.Core.Import;
using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Core.Parsing;

/// <summary>Port of the two groupby steps in excel_to_spec_list.</summary>
public static class SheetGrouper
{
    /// <param name="sheet">The imported sheet. Every cell has text.</param>
    /// <param name="separateBy">General data (Phase, Role, Usecase) that splits the specifications into files. These columns stay key columns.</param>
    public static GroupedSheet Group(ImportedSheet sheet, IReadOnlyList<string> separateBy)
    {
        var columns = sheet.Frame.Columns.ToList();
        var rows = sheet.Frame.Rows.Select(row => row.Select(cell => cell!).ToArray()).ToList();

        // Step 1: merge the rows that differ only in R.PropertyValue and R.AttributeValue. The values are joined with a delimiter.
        var valueColumns = new[] { RequirementPrefix + PropertyValue, RequirementPrefix + AttributeValue }
            .Where(columns.Contains).ToList();
        var step1Keys = columns.Where(column => !valueColumns.Contains(column)).ToList();
        var step1Rows = new List<string[]>();
        foreach (var (key, members) in GroupBy(rows, Indexes(columns, step1Keys)))
        {
            var merged = Indexes(columns, valueColumns)
                .Select(index => string.Join(DelimiterMergedRows, members.Select(member => member[index])));
            step1Rows.Add([.. key, .. merged]);
        }
        var step1Columns = step1Keys.Concat(valueColumns).ToList();

        // Step 2: merge all requirement columns and the general columns that are not used to separate the files.
        // Their values stay in lists, one value for each merged row.
        var mergeColumns = new[]
        {
            sheet.Columns.RequirementEntity, sheet.Columns.RequirementProperty, sheet.Columns.RequirementMaterial,
            sheet.Columns.RequirementAttribute, sheet.Columns.RequirementClassification, sheet.Columns.RequirementPartOf,
            sheet.Columns.General.Where(column => !separateBy.Contains(column)).ToList(),
        }.SelectMany(list => list).ToHashSet();

        var keyColumns = step1Keys.Where(column => !mergeColumns.Contains(column)).ToList();
        var aggregated = valueColumns.Concat(step1Keys.Where(mergeColumns.Contains)).ToList();
        var aggregatedIndexes = Indexes(step1Columns, aggregated);

        var finalRows = new List<IReadOnlyList<string>[]>();
        foreach (var (key, members) in GroupBy(step1Rows, Indexes(step1Columns, keyColumns)))
        {
            var cells = key.Select(value => (IReadOnlyList<string>)[value])
                .Concat(aggregatedIndexes.Select(index => (IReadOnlyList<string>)members.Select(member => member[index]).ToList()));
            finalRows.Add([.. cells]);
        }

        return new GroupedSheet(keyColumns.Concat(aggregated).ToList(), aggregated, finalRows);
    }

    private static int[] Indexes(List<string> columns, IEnumerable<string> selected) =>
        selected.Select(column => columns.IndexOf(column)).ToArray();

    /// <summary>pandas groupby with sort=False: the groups keep the order of their first row.</summary>
    private static List<(string[] Key, List<string[]> Members)> GroupBy(IReadOnlyList<string[]> rows, int[] keyIndexes)
    {
        var groups = new List<(string[] Key, List<string[]> Members)>();
        var lookup = new Dictionary<string[], int>(new KeyComparer());
        foreach (var row in rows)
        {
            var key = keyIndexes.Select(index => row[index]).ToArray();
            if (lookup.TryGetValue(key, out var position))
            {
                groups[position].Members.Add(row);
            }
            else
            {
                lookup[key] = groups.Count;
                groups.Add((key, [row]));
            }
        }
        return groups;
    }

    private sealed class KeyComparer : IEqualityComparer<string[]>
    {
        public bool Equals(string[]? x, string[]? y) => x!.AsSpan().SequenceEqual(y);

        public int GetHashCode(string[] key)
        {
            var hash = new HashCode();
            foreach (var part in key) hash.Add(part, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
