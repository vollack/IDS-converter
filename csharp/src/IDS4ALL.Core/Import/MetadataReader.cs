using System.Numerics;
using IDS4ALL.Core.Excel;

namespace IDS4ALL.Core.Import;

/// <summary>Port of get_metadata in IDS4ALL-main.py.</summary>
public static class MetadataReader
{
    public const string MetadataSheet = "IDS4ALL";

    public static Ids4AllSettings Read(IXlsxWorkbook workbook)
    {
        var entries = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in PandasExcel.ReadFirstTwoColumns(workbook, MetadataSheet, 1))
        {
            // Python: {key: value for key, value in zip(...) if pd.notna(value)}. A later entry replaces an earlier one.
            if (value is not null && key is not null) entries[PythonFormat.Str(key)] = value;
        }

        foreach (var required in new[] { "Sheet name", "IFC version" })
            if (!entries.ContainsKey(required))
                throw new ImportException($"{required} is not defined in the Excel");

        var ifcVersion = Text(entries, "IFC version").Replace(" ", "").Replace(",", "|");
        var separateBy = entries.ContainsKey("File separators")
            ? Text(entries, "File separators").Replace(" ", "").Split(',')
            : [];
        var isEntityBased = entries.ContainsKey("Entity-based applicability")
            && Text(entries, "Entity-based applicability").ToLowerInvariant() == "yes";

        return new Ids4AllSettings(
            SheetName: Text(entries, "Sheet name"),
            IfcVersion: ifcVersion,
            SeparateBy: separateBy,
            SkippedRows: entries.TryGetValue("Skipped rows", out var skipped) ? SkippedRows(skipped) : 0,
            IsEntityBasedApplicability: isEntityBased,
            Title: entries.TryGetValue("Title", out var title) ? PythonFormat.Str(title) : "Not Defined",
            Copyright: Optional(entries, "Copyright"),
            Version: Optional(entries, "Version"),
            Description: Optional(entries, "Description"),
            Author: Optional(entries, "Author"));
    }

    private static string? Optional(Dictionary<string, object> entries, string key) =>
        entries.TryGetValue(key, out var value) ? PythonFormat.Str(value) : null;

    /// <summary>Python calls str methods on these entries and fails with AttributeError for a number.</summary>
    private static string Text(Dictionary<string, object> entries, string key) =>
        entries[key] as string
        ?? throw new ImportException($"{key} must be text, but the Excel has {PythonFormat.Str(entries[key])}.");

    private static int SkippedRows(object value) => value switch
    {
        BigInteger number when number >= 0 && number <= int.MaxValue => (int)number,
        _ => throw new ImportException($"Skipped rows must be a whole number of 0 or more, but the Excel has {PythonFormat.Str(value)}."),
    };
}
