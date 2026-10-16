namespace IDS4ALL.Core.Import;

/// <summary>The entries of the sheet "IDS4ALL" of the workbook.</summary>
/// <param name="SheetName">Sheet with the information requirements.</param>
/// <param name="IfcVersion">Default IFC versions, separated by "|".</param>
/// <param name="SeparateBy">Categories (Phase, Role, Usecase) that split the output into several IDS files.</param>
/// <param name="SkippedRows">Rows above the header row of the input sheet.</param>
/// <param name="IsEntityBasedApplicability">Only the entity-based applicability columns are used.</param>
public sealed record Ids4AllSettings(
    string SheetName,
    string IfcVersion,
    IReadOnlyList<string> SeparateBy,
    int SkippedRows,
    bool IsEntityBasedApplicability,
    string Title,
    string? Copyright,
    string? Version,
    string? Description,
    string? Author);
