namespace IDS4ALL.Core.Import;

/// <summary>The workbook has an invalid structure. The message is meant for the user and matches the Python converter.</summary>
public sealed class ImportException(string message, Exception? inner = null) : Exception(message, inner);
