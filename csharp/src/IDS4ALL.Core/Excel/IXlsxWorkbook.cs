namespace IDS4ALL.Core.Excel;

/// <summary>A reader for the cell values of an .xlsx file. The values are described in <see cref="XlsxWorkbook"/>.</summary>
public interface IXlsxWorkbook : IDisposable
{
    IReadOnlyList<string> SheetNames { get; }

    IReadOnlyList<object?[]> ReadRows(string sheetName, int? maxRows = null);
}
