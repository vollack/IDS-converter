namespace IDS4ALL.IfcTester;

/// <summary>ifctester rejected the IDS or could not write the file. The message is the Python message.</summary>
public sealed class IdsWriteException(string message, Exception? inner = null) : Exception(message, inner);
