using System.Text.Json;
using IDS4ALL.IfcTester;
using Python.Runtime;

namespace IDS4ALL.Tests;

/// <summary>A Python snippet that tests call through Python.NET. Each function returns a JSON string.</summary>
internal sealed class PythonScript(string code)
{
    // One call at a time for all scripts: a script may patch pandas for the duration of a call.
    private static readonly object Gate = new();
    private PyModule? _scope;

    public JsonDocument Call(string function, params object[] arguments)
    {
        lock (Gate)
        {
            PythonRuntime.EnsureInitialized(PythonRuntimeOptions.FromEnvironment());
            using (Py.GIL())
            {
                if (_scope is null)
                {
                    _scope = Py.CreateScope();
                    _scope.Exec(code);
                }

                var converted = arguments.Select(ToPython).ToArray();
                try
                {
                    using var callable = _scope.Get(function);
                    using var result = callable.Invoke(converted);
                    return JsonDocument.Parse(result.As<string>());
                }
                finally
                {
                    foreach (var item in converted) item.Dispose();
                }
            }
        }
    }

    private static PyObject ToPython(object value) => value switch
    {
        string text => new PyString(text),
        int number => new PyInt(number),
        bool flag => new PyInt(flag ? 1 : 0),
        IEnumerable<string> list => ToList(list),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static PyList ToList(IEnumerable<string> items)
    {
        var list = new PyList();
        foreach (var item in items)
        {
            using var text = new PyString(item);
            list.Append(text);
        }
        return list;
    }
}
