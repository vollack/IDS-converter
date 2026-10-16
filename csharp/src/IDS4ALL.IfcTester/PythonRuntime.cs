using Python.Runtime;

namespace IDS4ALL.IfcTester;

/// <summary>Starts the embedded Python once per process and checks the packages that the bridge needs.</summary>
public static class PythonRuntime
{
    private static readonly object Gate = new();
    private static bool _initialized;

    private static readonly string[] RequiredModules = ["xmlschema", "ifcopenshell", "ifctester.ids"];

    public static void EnsureInitialized(PythonRuntimeOptions options)
    {
        lock (Gate)
        {
            if (_initialized) return;

            if (!File.Exists(options.PythonDll))
                throw new PythonSetupException($"The Python library '{options.PythonDll}' does not exist.");
            if (options.SitePackages is not null && !Directory.Exists(options.SitePackages))
                throw new PythonSetupException($"The site-packages folder '{options.SitePackages}' does not exist.");

            try
            {
                Runtime.PythonDLL = options.PythonDll;
                PythonEngine.Initialize();
            }
            catch (Exception ex)
            {
                throw new PythonSetupException($"Python could not start from '{options.PythonDll}': {ex.Message}", ex);
            }

            try
            {
                using (Py.GIL())
                {
                    if (options.SitePackages is not null)
                    {
                        using var sys = Py.Import("sys");
                        sys.GetAttr("path").InvokeMethod("append", new PyString(options.SitePackages));
                    }
                    CheckModules();
                }
            }
            finally
            {
                // Let other threads take the GIL. Every Python call runs inside Py.GIL().
                PythonEngine.BeginAllowThreads();
            }

            _initialized = true;
        }
    }

    private static void CheckModules()
    {
        foreach (var module in RequiredModules)
        {
            try
            {
                Py.Import(module).Dispose();
            }
            catch (PythonException ex)
            {
                var package = module.Split('.')[0];
                throw new PythonSetupException(
                    $"The Python package '{package}' does not load: {ex.Message}. Install it in the Python environment that the Python library belongs to.", ex);
            }
        }
    }
}
