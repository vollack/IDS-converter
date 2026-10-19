using Python.Runtime;

namespace IDS4ALL.IfcTester;

/// <summary>Starts the embedded Python once per process and checks the packages that the bridge needs.</summary>
public static class PythonRuntime
{
    private static readonly object Gate = new();
    private static bool _initialized;

    private static readonly string[] RequiredModules = ["xmlschema", "ifcopenshell", "ifctester.ids"];

    /// <summary>
    /// Starts Python on the first call. The Python library of the first call stays for the whole process.
    /// Every call adds its folders to sys.path once and checks its modules, so a caller can name its own packages.
    /// </summary>
    /// <exception cref="PythonSetupException">A file or folder does not exist, Python does not start, or a module does not load.</exception>
    public static void EnsureInitialized(PythonRuntimeOptions options)
    {
        lock (Gate)
        {
            if (!_initialized && !File.Exists(options.PythonDll))
                throw new PythonSetupException($"The Python library '{options.PythonDll}' does not exist.");
            if (options.SitePackages is not null && !Directory.Exists(options.SitePackages))
                throw new PythonSetupException($"The site-packages folder '{options.SitePackages}' does not exist.");
            foreach (var path in options.ExtraPaths ?? [])
                if (!Directory.Exists(path))
                    throw new PythonSetupException($"The extra path '{path}' does not exist.");

            if (!_initialized)
            {
                try
                {
                    Runtime.PythonDLL = options.PythonDll;
                    PythonEngine.Initialize();
                }
                catch (Exception ex)
                {
                    throw new PythonSetupException($"Python could not start from '{options.PythonDll}': {ex.Message}", ex);
                }

                // Let other threads take the GIL. Every Python call runs inside Py.GIL().
                PythonEngine.BeginAllowThreads();
                _initialized = true;
            }

            using (Py.GIL())
            {
                foreach (var path in (options.SitePackages is null ? [] : new[] { options.SitePackages }).Concat(options.ExtraPaths ?? []))
                    AddToPath(path);
                CheckModules(RequiredModules.Concat(options.ExtraModules ?? []));
            }
        }
    }

    private static void AddToPath(string path)
    {
        using var sys = Py.Import("sys");
        using var searchPath = sys.GetAttr("path");
        using var text = new PyString(path);
        using var count = searchPath.InvokeMethod("count", text);
        if (count.As<int>() == 0) searchPath.InvokeMethod("append", text).Dispose();
    }

    private static void CheckModules(IEnumerable<string> modules)
    {
        foreach (var module in modules)
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
