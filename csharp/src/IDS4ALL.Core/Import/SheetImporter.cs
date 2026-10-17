using IDS4ALL.Core.Excel;
using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Core.Import;

/// <summary>
/// The input sheet after the import. Every cell has text. A missing cell has the text
/// <see cref="ColumnNames.KeywordMissing"/>, like the dataframe df_filled in custom_functions.py.
/// </summary>
/// <param name="Columns">The columns that belong to each facet. Specification includes SpecificationIfcVersion.</param>
/// <param name="Frame">The relevant columns. Same order as in the sheet; a SpecificationIfcVersion column that was missing is last.</param>
public sealed record ImportedSheet(SheetColumns Columns, StringFrame Frame);

/// <summary>Port of the import part of excel_to_spec_list, up to the creation of df_filled.</summary>
public static class SheetImporter
{
    public static ImportedSheet Import(XlsxWorkbook workbook, Ids4AllSettings settings)
    {
        var allColumns = PandasExcel.ReadColumns(workbook, settings.SheetName, settings.SkippedRows);
        var columns = SheetColumns.From(allColumns, settings.IsEntityBasedApplicability, settings.SheetName);
        var frame = PandasExcel.ReadStrings(workbook, settings.SheetName, settings.SkippedRows, columns.Relevant);

        var names = frame.Columns.ToList();
        var rows = frame.Rows.Select(row => row.ToList()).ToList();

        // Empty requirement cardinality gets the default value.
        const string cardinality = RequirementPrefix + RequirementCardinality;
        FillMissing(names, rows, cardinality, "required");

        // Empty specification IFC version gets the default version of the sheet IDS4ALL.
        var specification = columns.Specification.ToList();
        if (!names.Contains(SpecificationIfcVersion))
        {
            names.Add(SpecificationIfcVersion);
            foreach (var row in rows) row.Add(settings.IfcVersion);
            specification.Add(SpecificationIfcVersion);
        }
        else
        {
            FillMissing(names, rows, SpecificationIfcVersion, settings.IfcVersion);
        }

        // A missing value gets a placeholder.
        var filled = rows.Select(row => row.Select(cell => cell ?? KeywordMissing).ToArray()).ToList();
        return new ImportedSheet(columns with { Specification = specification }, new StringFrame(names, filled));
    }

    /// <summary>Python fillna on one column: only missing cells get the value.</summary>
    private static void FillMissing(List<string> names, List<List<string?>> rows, string column, string value)
    {
        var index = names.IndexOf(column);
        if (index < 0) return;
        foreach (var row in rows) row[index] ??= value;
    }
}
