namespace IDS4ALL.Core.Excel;

/// <summary>The workbook cannot be read: a missing sheet, a damaged file or an invalid cell.</summary>
public sealed class XlsxException(string message, Exception? inner = null) : Exception(message, inner);
