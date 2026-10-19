using IDS4ALL.IfcTester;
using Python.Runtime;

namespace IDS4ALL.Tests;

public sealed class PythonRuntimeTests
{
    [Fact]
    public void EnsureInitialized_WithMissingExtraPath_NamesThePath()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"ids4all-missing-{Guid.NewGuid():N}");
        var options = new PythonRuntimeOptions(typeof(PythonRuntimeTests).Assembly.Location, ExtraPaths: [missing]);

        var exception = Assert.Throws<PythonSetupException>(() => PythonRuntime.EnsureInitialized(options));

        Assert.Contains(missing, exception.Message);
    }

    [PythonFact]
    public void EnsureInitialized_WithMissingExtraModule_NamesThePackage()
    {
        var options = PythonRuntimeOptions.FromEnvironment() with { ExtraModules = ["ids4all_no_such_module.sub"] };

        var exception = Assert.Throws<PythonSetupException>(() => PythonRuntime.EnsureInitialized(options));

        Assert.Contains("ids4all_no_such_module", exception.Message);
    }

    [PythonFact]
    public void EnsureInitialized_WithExtraPath_AddsTheFolderOnceAndLoadsTheModule()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ids4all-extra-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var module = $"ids4all_probe_{Guid.NewGuid():N}";
        File.WriteAllText(Path.Combine(directory, module + ".py"), "VALUE = 42\n");
        try
        {
            var options = PythonRuntimeOptions.FromEnvironment() with { ExtraPaths = [directory], ExtraModules = [module] };

            PythonRuntime.EnsureInitialized(options);
            PythonRuntime.EnsureInitialized(options);

            using (Py.GIL())
            {
                using var sys = Py.Import("sys");
                using var searchPath = sys.GetAttr("path");
                using var text = new PyString(directory);
                using var count = searchPath.InvokeMethod("count", text);
                Assert.Equal(1, count.As<int>());
                using var probe = Py.Import(module);
                Assert.Equal(42, probe.GetAttr("VALUE").As<int>());
                searchPath.InvokeMethod("remove", text).Dispose();
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [PythonFact]
    public void EnsureInitialized_WithoutExtras_ChecksTheConverterModulesOnEveryCall()
    {
        var options = PythonRuntimeOptions.FromEnvironment();

        PythonRuntime.EnsureInitialized(options);
        PythonRuntime.EnsureInitialized(options);
    }
}
