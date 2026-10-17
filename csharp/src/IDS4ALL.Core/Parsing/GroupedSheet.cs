namespace IDS4ALL.Core.Parsing;

/// <summary>The values of one column in one row. A column that was grouped into a list has one value for each merged row.</summary>
public readonly record struct Cell(string Column, IReadOnlyList<string> Values);

/// <summary>
/// The input sheet after the two grouping steps, like the dataframe df_final in custom_functions.py.
/// The key columns come first and have one value in each row. The aggregated columns follow.
/// </summary>
/// <param name="Columns">The key columns, then the aggregated columns.</param>
/// <param name="AggregatedColumns">The columns that hold one value for each merged row.</param>
/// <param name="Rows">One list of values for each column of <see cref="Columns"/>.</param>
public sealed record GroupedSheet(
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> AggregatedColumns,
    IReadOnlyList<IReadOnlyList<string>[]> Rows)
{
    /// <summary>Port of <c>df_final[columns].iloc[rowIndex]</c>. The cells keep the order of <paramref name="columns"/>.</summary>
    public IReadOnlyList<Cell> Row(int rowIndex, IEnumerable<string> columns)
    {
        var row = Rows[rowIndex];
        return columns.Select(column => new Cell(column, row[IndexOf(column)])).ToList();
    }

    private int IndexOf(string column)
    {
        for (var i = 0; i < Columns.Count; i++)
            if (Columns[i] == column) return i;
        throw new KeyNotFoundException($"The grouped sheet has no column '{column}'.");
    }
}
