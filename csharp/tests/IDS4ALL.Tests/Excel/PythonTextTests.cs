using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Parsing;
using IDS4ALL.Tests.Parsing;

namespace IDS4ALL.Tests.Excel;

public sealed class PythonTextTests
{
    private static string Chars(params int[] codes) => new(codes.Select(code => (char)code).ToArray());

    [Fact]
    public void Strip_RemovesPythonWhiteSpace()
    {
        // Tab, group separator, no-break space, em space, ideographic space.
        Assert.Equal("a b", PythonText.Strip(Chars(9, 0x1f, 0xa0, 0x2003, 0x3000) + "a b" + Chars(0x1c, 0x85, 0x2028, 0x202f)));
    }

    [Fact]
    public void Strip_KeepsTheOtherCharacters()
    {
        Assert.Equal(Chars(0x200b) + "a" + Chars(0x180e), PythonText.Strip(" " + Chars(0x200b) + "a" + Chars(0x180e) + " "));
        Assert.Equal("", PythonText.Strip("  \t "));
    }

    [Theory]
    [InlineData("abc", "'abc'")]
    [InlineData("a'b", "\"a'b\"")]
    [InlineData("a\"b", "'a\"b'")]
    [InlineData("a'b\"c", "'a\\'b\"c'")]
    [InlineData("a\\b", "'a\\\\b'")]
    [InlineData("a\tb\nc\rd", "'a\\tb\\nc\\rd'")]
    [InlineData("ä€😀", "'ä€😀'")]
    public void Repr_FormatsLikePython(string text, string expected)
    {
        Assert.Equal(expected, PythonText.Repr(text));
    }

    [Fact]
    public void Repr_EscapesCharactersThatAreNotPrintable()
    {
        Assert.Equal("'\\x01\\x7f\\xa0\\xad\\u2028\\u200b\\ue000'", PythonText.Repr(Chars(1, 0x7f, 0xa0, 0xad, 0x2028, 0x200b, 0xe000)));
        Assert.Equal("'\\U000e0001'", PythonText.Repr(char.ConvertFromUtf32(0xe0001)));
    }

    [Fact]
    public void Repr_OfAList_ListsTheItems()
    {
        Assert.Equal("['a', \"b'\"]", PythonText.Repr(new[] { "a", "b'" }));
        Assert.Equal("[]", PythonText.Repr(Array.Empty<string>()));
    }

    private static readonly string[] Samples =
    [
        "", " ", "abc", "  a b  ", "a'b", "a\"b", "a'b\"c", "a\\b", "a\tb\nc\rd", "ä€😀", "IfcWall.USERDEFINED", "ifcLabel", "pattern=a|b", "\\<5",
        Chars(1), Chars(0x1c) + "x" + Chars(0x1f), Chars(0x7f), Chars(0x85, 0xa0, 0xad), Chars(0x2000, 0x200a, 0x200b, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000),
        Chars(0x180e) + "x", Chars(0xe000), char.ConvertFromUtf32(0xe0001), char.ConvertFromUtf32(0x1f600), "ǆ", "İ", "ıi", "Ωμέγα", Chars(0xfeff) + "x", Chars(0x378),
        Chars(0x0b, 0x0c) + "x" + Chars(0x0b, 0x0c),
    ];

    [PythonFact]
    public void Functions_MatchPython()
    {
        var expected = Assert.IsType<List<object?>>(ParsingReference.Text(Samples));

        for (var i = 0; i < Samples.Length; i++)
        {
            var sample = Samples[i];
            var item = Assert.IsType<List<object?>>(expected[i]);
            string Field(string name) => (string)Tree.Get(item, name)!;

            Assert.Equal(Field("strip"), PythonText.Strip(sample));
            Assert.Equal(Field("repr"), PythonText.Repr(sample));
            Assert.Equal(Field("list"), PythonText.Repr(new[] { sample, sample }));
            Assert.Equal(Field("upper"), PythonText.Upper(sample));
        }
    }
}
