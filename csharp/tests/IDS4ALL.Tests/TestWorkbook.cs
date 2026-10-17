using IDS4ALL.Core.Excel;

namespace IDS4ALL.Tests;

/// <summary>Opens a workbook with the reader that the tests check. IDS4ALL_XLSX_READER=closedxml selects the ClosedXML spike.</summary>
internal static class TestWorkbook
{
    public static IXlsxWorkbook Open(string path) =>
        Environment.GetEnvironmentVariable("IDS4ALL_XLSX_READER") == "closedxml"
            ? ClosedXmlWorkbook.Open(path)
            : XlsxWorkbook.Open(path);
}
