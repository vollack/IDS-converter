using System.Text.Json;
using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;

namespace IDS4ALL.Tests.Excel;

public sealed class PythonFloatTests
{
    private const string Code = """
        import json

        def convert(values_json):
            out = []
            for value in json.loads(values_json):
                entry = {}
                try:
                    number = float(value)
                    entry["float"] = repr(number)
                    try:
                        entry["int"] = str(int(number))
                    except (ValueError, OverflowError):
                        entry["int"] = None
                except ValueError:
                    entry["float"] = None
                    entry["int"] = None
                out.append(entry)
            return json.dumps(out)
        """;

    private static readonly PythonScript Script = new(Code);

    private static readonly string[] Values =
    [
        "5", " 5 ", "+5", "-5", "5.", ".5", "-.5", "5.5", "1e3", "1E3", "1e+3", "1e-3", "1_000", "1_0.0_1", "1_000e1_0", "007", "0.0", "-0.0",
        "3.9", "-3.9", "1e999", "-1e999", "inf", "-inf", "Infinity", "+INF", "nan", "NaN", "-nan",
        "", " ", "abc", "1,5", "1__0", "_1", "1_", "1e", "e5", "0x10", "1 000", "--5", "5-", ".", "+", "1.2.3", "infinit", "1e5.5", "١٢٣",
    ];

    [PythonFact]
    public void Parse_MatchesPythonFloat()
    {
        using var document = Script.Call("convert", JsonSerializer.Serialize(Values));
        var expected = document.RootElement.EnumerateArray().ToList();

        for (var i = 0; i < Values.Length; i++)
        {
            var value = Values[i];
            var pythonFloat = expected[i].GetProperty("float").GetString();
            if (pythonFloat is null)
            {
                Assert.Throws<ImportException>(() => PythonFloat.Parse(value));
                continue;
            }

            Assert.True(pythonFloat == PythonFormat.Repr(PythonFloat.Parse(value)), $"float({PythonText.Repr(value)}): Python {pythonFloat}");

            var pythonInteger = expected[i].GetProperty("int").GetString();
            if (pythonInteger is null) Assert.Throws<ImportException>(() => PythonFloat.ParseInteger(value));
            else if (int.TryParse(pythonInteger, out var integer)) Assert.Equal(integer, PythonFloat.ParseInteger(value));
        }
    }

    [Fact]
    public void ParseInteger_TruncatesTowardZero()
    {
        Assert.Equal(3, PythonFloat.ParseInteger("3.9"));
        Assert.Equal(-3, PythonFloat.ParseInteger("-3.9"));
        Assert.Throws<ImportException>(() => PythonFloat.ParseInteger("inf"));
        Assert.Throws<ImportException>(() => PythonFloat.ParseInteger("nan"));
    }
}
