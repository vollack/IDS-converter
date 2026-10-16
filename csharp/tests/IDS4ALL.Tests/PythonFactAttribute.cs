using IDS4ALL.IfcTester;

namespace IDS4ALL.Tests;

/// <summary>A fact that needs the embedded Python. It is skipped when the Python environment variables are not set.</summary>
public sealed class PythonFactAttribute : FactAttribute
{
    public PythonFactAttribute()
    {
        var dll = Environment.GetEnvironmentVariable(PythonRuntimeOptions.PythonDllVariable)
                  ?? Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
        if (string.IsNullOrWhiteSpace(dll))
            Skip = $"Set {PythonRuntimeOptions.PythonDllVariable} and {PythonRuntimeOptions.SitePackagesVariable} to run the Python tests.";
    }
}
