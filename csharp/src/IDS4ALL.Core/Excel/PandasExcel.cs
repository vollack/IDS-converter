namespace IDS4ALL.Core.Excel;

/// <summary>Cells are text or null. Null is a missing value (pandas NaN).</summary>
public sealed record StringFrame(IReadOnlyList<string> Columns, IReadOnlyList<string?[]> Rows)
{
    public static readonly StringFrame Empty = new([], []);

    public int ColumnIndex(string column)
    {
        for (var i = 0; i < Columns.Count; i++)
            if (Columns[i] == column) return i;
        return -1;
    }
}

/// <summary>The parts of pandas.read_excel that the converter uses.</summary>
public static class PandasExcel
{
    /// <summary>pandas STR_NA_VALUES: a text cell with one of these texts is a missing value.</summary>
    public static readonly IReadOnlySet<string> NaValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "", "#N/A", "#N/A N/A", "#NA", "-1.#IND", "-1.#QNAN", "-NaN", "-nan", "1.#IND", "1.#QNAN",
        "<NA>", "N/A", "NA", "NULL", "NaN", "None", "n/a", "nan", "null",
    };

    /// <summary>
    /// The column names, as <c>pd.read_excel(..., skiprows=n, nrows=0).columns</c>.
    /// pandas reads only the first rows then, so the width comes from the rows up to the header row.
    /// </summary>
    public static IReadOnlyList<string> ReadColumns(XlsxWorkbook workbook, string sheet, int skipRows) =>
        Header(workbook.ReadRows(sheet, skipRows + 1), skipRows);

    /// <summary>
    /// <c>pd.read_excel(path, sheet_name=sheet, skiprows=skipRows, usecols=useColumns, dtype=str)</c>.
    /// The columns keep their order in the sheet. A sheet without data gives an empty frame.
    /// </summary>
    public static StringFrame ReadStrings(XlsxWorkbook workbook, string sheet, int skipRows, IReadOnlyCollection<string> useColumns)
    {
        var rows = workbook.ReadRows(sheet);
        if (rows.Count == 0 || skipRows >= rows.Count) return StringFrame.Empty;

        var columns = Header(rows, skipRows);
        var missing = useColumns.Where(name => !columns.Contains(name)).ToList();
        if (missing.Count > 0)
            throw new XlsxException($"Usecols do not match columns, columns expected but not found: [{string.Join(", ", missing.Select(name => $"'{name}'"))}]");

        var selected = Enumerable.Range(0, columns.Count).Where(i => useColumns.Contains(columns[i])).ToArray();
        var dataRows = rows.Skip(skipRows + 1).ToList();
        var memos = selected.Select(_ => new EqualValueMemo()).ToArray();
        var data = dataRows
            .Select(line => selected.Select((i, position) => ToText(memos[position].Apply(line[i]))).ToArray())
            .ToList();
        return new StringFrame(selected.Select(i => columns[i]).ToList(), data);
    }

    /// <summary>
    /// <c>pd.read_excel(path, sheet_name=sheet, usecols=[0, 1], header=None, skiprows=skipRows)</c>.
    /// Values keep their type: string, BigInteger, double, bool or a date or time type. Null is a missing value.
    /// </summary>
    public static IReadOnlyList<(object? First, object? Second)> ReadFirstTwoColumns(XlsxWorkbook workbook, string sheet, int skipRows)
    {
        var rows = workbook.ReadRows(sheet);
        var lines = rows.Skip(skipRows).ToList();
        if (lines.Count == 0) return [];
        if (lines[0].Length < 2)
            throw new XlsxException("Defining usecols with out-of-bounds indices is not allowed. [0, 1] are out-of-bounds.");
        var first = new EqualValueMemo();
        var second = new EqualValueMemo();
        return lines.Select(line => (MissingToNull(first.Apply(line[0])), MissingToNull(second.Apply(line[1])))).ToList();
    }

    // ---- pandas text parser rules ---------------------------------------------------------

    /// <summary>The names in the header row (row number skipRows). No such row gives no columns.</summary>
    private static IReadOnlyList<string> Header(IReadOnlyList<object?[]> rows, int skipRows)
    {
        if (skipRows >= rows.Count) return [];

        var line = rows[skipRows];
        var names = line.Select((cell, index) => cell is "" ? $"Unnamed: {index}" : PythonFormat.Str(cell)).ToList();
        var unnamed = line.Select((cell, index) => (cell, index)).Where(x => x.cell is "").Select(x => x.index).ToHashSet();
        return MangleDuplicates(names, unnamed);
    }

    /// <summary>
    /// pandas names the repeated column "a" as "a.1", "a.2" and so on. Named columns are handled
    /// before the unnamed ones, so a given name keeps its text.
    /// </summary>
    private static List<string> MangleDuplicates(List<string> columns, HashSet<int> unnamed)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int Count(string name) => counts.GetValueOrDefault(name);

        var order = Enumerable.Range(0, columns.Count).Where(i => !unnamed.Contains(i))
            .Concat(Enumerable.Range(0, columns.Count).Where(unnamed.Contains));
        foreach (var i in order)
        {
            var column = columns[i];
            var original = column;
            var current = Count(column);
            if (current > 0)
            {
                while (current > 0)
                {
                    counts[original] = current + 1;
                    column = $"{original}.{current}";
                    current = columns.Contains(column) ? current + 1 : Count(column);
                }
            }
            columns[i] = column;
            counts[column] = current + 1;
        }
        return columns;
    }

    private static string? ToText(object? cell) => cell switch
    {
        string text => NaValues.Contains(text) ? null : text,
        double number when double.IsNaN(number) => null,
        _ => PythonFormat.Str(cell),
    };

    private static object? MissingToNull(object? cell) => cell switch
    {
        string text when NaValues.Contains(text) => null,
        double number when double.IsNaN(number) => null,
        _ => cell,
    };

    /// <summary>
    /// pandas sanitize_objects keeps one object for all equal values of a column. In Python
    /// True == 1 and False == 0, so the first of the two seen in the column replaces the others.
    /// </summary>
    private sealed class EqualValueMemo
    {
        private object? _zero;
        private object? _one;

        public object? Apply(object? cell)
        {
            var key = cell switch
            {
                bool flag => flag ? 1 : 0,
                System.Numerics.BigInteger number when number.IsZero => 0,
                System.Numerics.BigInteger number when number.IsOne => 1,
                _ => -1,
            };
            switch (key)
            {
                case 0: return _zero ??= cell;
                case 1: return _one ??= cell;
                default: return cell;
            }
        }
    }
}
