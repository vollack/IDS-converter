namespace IDS4ALL.IfcTester;

/// <param name="PythonDll">Path of the Python library, for example python314.dll.</param>
/// <param name="SitePackages">Folder with ifctester and its packages. Null if Python finds them itself.</param>
public sealed record PythonRuntimeOptions(string PythonDll, string? SitePackages = null)
{
    public const string PythonDllVariable = "IDS4ALL_PYTHON_DLL";
    public const string SitePackagesVariable = "IDS4ALL_PYTHON_SITE_PACKAGES";

    /// <summary>Reads IDS4ALL_PYTHON_DLL (or PYTHONNET_PYDLL) and IDS4ALL_PYTHON_SITE_PACKAGES.</summary>
    public static PythonRuntimeOptions FromEnvironment()
    {
        var dll = Environment.GetEnvironmentVariable(PythonDllVariable)
                  ?? Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
        if (string.IsNullOrWhiteSpace(dll))
            throw new PythonSetupException(
                $"The Python library is not set. Set the environment variable {PythonDllVariable} to the path of the Python library, for example python314.dll.");
        var sitePackages = Environment.GetEnvironmentVariable(SitePackagesVariable);
        return new PythonRuntimeOptions(dll, string.IsNullOrWhiteSpace(sitePackages) ? null : sitePackages);
    }
}
