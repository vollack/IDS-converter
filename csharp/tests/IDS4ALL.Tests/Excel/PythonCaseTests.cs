using System.Text.Json;
using IDS4ALL.Core.Excel;

namespace IDS4ALL.Tests.Excel;

public sealed class PythonCaseTests
{
    [Theory]
    [InlineData("straße", "STRASSE", "straße")]
    [InlineData("İ", "İ", "i̇")]
    [InlineData("ı", "I", "ı")]
    [InlineData("ﬁ", "FI", "ﬁ")]
    [InlineData("ΐ", "Ϊ́", "ΐ")]
    [InlineData("IfcWall.USERDEFINED", "IFCWALL.USERDEFINED", "ifcwall.userdefined")]
    public void UpperAndLower_UseTheFullCaseMapping(string text, string upper, string lower)
    {
        Assert.Equal(upper, PythonText.Upper(text));
        Assert.Equal(lower, PythonText.Lower(text));
    }

    [Theory]
    [InlineData("ΑΣ", "ας")]
    [InlineData("ΣΑ", "σα")]
    [InlineData("ΑΣΑ", "ασα")]
    [InlineData("Σ", "σ")]
    [InlineData("A.Σ", "a.ς")]
    [InlineData("AΣ.B", "aσ.b")]
    public void Lower_UsesTheFinalSigma(string text, string expected) => Assert.Equal(expected, PythonText.Lower(text));

    private const string Code = """
        import json, itertools

        def mappings():
            out = []
            for cp in range(0x110000):
                if 0xD800 <= cp <= 0xDFFF:
                    continue
                c = chr(cp)
                if c.upper() != c or c.lower() != c:
                    out.append([cp, c.upper(), c.lower()])
            return json.dumps(out)

        def combinations(pool, length):
            return json.dumps([[s, s.upper(), s.lower()] for n in range(1, length + 1) for s in map("".join, itertools.product(pool, repeat=n))])
        """;

    private static readonly PythonScript Script = new(Code);

    [PythonFact]
    public void UpperAndLower_MatchPythonForEveryCodePoint()
    {
        using var document = Script.Call("mappings");
        var changed = new Dictionary<int, (string Upper, string Lower)>();
        foreach (var item in document.RootElement.EnumerateArray())
            changed[item[0].GetInt32()] = (item[1].GetString()!, item[2].GetString()!);

        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF) continue;
            var text = char.ConvertFromUtf32(codePoint);
            var (upper, lower) = changed.GetValueOrDefault(codePoint, (text, text));
            Assert.True(upper == PythonText.Upper(text), $"Upper(U+{codePoint:X4}) differs.");
            Assert.True(lower == PythonText.Lower(text), $"Lower(U+{codePoint:X4}) differs.");
        }
    }

    [PythonFact]
    public void Lower_MatchesPythonForTheFinalSigma()
    {
        // Letters, the capital sigma, case-ignorable characters (apostrophe, full stop, soft hyphen, combining accent) and a cased letter outside the BMP.
        string[] pool = ["a", "Α", "Σ", "'", ".", "­", "́", "1", " ", char.ConvertFromUtf32(0x10400)];
        using var document = Script.Call("combinations", pool, 4);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var text = item[0].GetString()!;
            Assert.True(item[1].GetString() == PythonText.Upper(text), $"Upper({JsonSerializer.Serialize(text)}) differs.");
            Assert.True(item[2].GetString() == PythonText.Lower(text), $"Lower({JsonSerializer.Serialize(text)}) differs.");
        }
    }
}
