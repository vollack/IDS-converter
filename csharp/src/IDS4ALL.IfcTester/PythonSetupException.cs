namespace IDS4ALL.IfcTester;

/// <summary>Python, ifctester or one of its packages is missing or does not load.</summary>
public sealed class PythonSetupException(string message, Exception? inner = null) : Exception(message, inner);
