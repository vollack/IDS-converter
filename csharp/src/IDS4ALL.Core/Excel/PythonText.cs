using System.Globalization;
using System.Text;

namespace IDS4ALL.Core.Excel;

/// <summary>String functions with the behavior of the Python str methods and of repr().</summary>
public static class PythonText
{
    /// <summary>Python str.isspace() for one UTF-16 unit. Python also counts U+001C to U+001F as white space.</summary>
    public static bool IsSpace(char c) =>
        (int)c is (>= 0x09 and <= 0x0d) or (>= 0x1c and <= 0x20) or 0x85 or 0xa0 or 0x1680
            or (>= 0x2000 and <= 0x200a) or 0x2028 or 0x2029 or 0x202f or 0x205f or 0x3000;

    /// <summary>Python str.strip() without arguments.</summary>
    public static string Strip(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsSpace(text[start])) start++;
        while (end > start && IsSpace(text[end - 1])) end--;
        return text[start..end];
    }

    /// <summary>Python str.upper(). It uses the full case mapping, so "ß" gives "SS".</summary>
    public static string Upper(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i += CodePointLength(text, i))
        {
            var piece = text.Substring(i, CodePointLength(text, i));
            builder.Append(PythonCaseData.Upper.TryGetValue(CodePointAt(text, i), out var mapped) ? mapped : piece.ToUpperInvariant());
        }
        return builder.ToString();
    }

    /// <summary>Python str.lower(). It uses the full case mapping, so "İ" gives two characters, and the final sigma rule.</summary>
    public static string Lower(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i += CodePointLength(text, i))
        {
            var codePoint = CodePointAt(text, i);
            if (codePoint == 0x3A3) builder.Append(IsFinalSigma(text, i) ? 'ς' : 'σ');
            else if (PythonCaseData.Lower.TryGetValue(codePoint, out var mapped)) builder.Append(mapped);
            else builder.Append(text.Substring(i, CodePointLength(text, i)).ToLowerInvariant());
        }
        return builder.ToString();
    }

    /// <summary>A capital sigma is final if a cased letter is before it and no cased letter is after it. Case-ignorable characters do not count.</summary>
    private static bool IsFinalSigma(string text, int index)
    {
        var before = index;
        while (before > 0)
        {
            before -= before >= 2 && char.IsLowSurrogate(text[before - 1]) && char.IsHighSurrogate(text[before - 2]) ? 2 : 1;
            var codePoint = CodePointAt(text, before);
            if (!Contains(PythonCaseData.CaseIgnorable, codePoint))
                return Contains(PythonCaseData.Cased, codePoint) && !IsCasedAfter(text, index + 1);
        }
        return false;
    }

    private static bool IsCasedAfter(string text, int start)
    {
        for (var i = start; i < text.Length; i += CodePointLength(text, i))
        {
            var codePoint = CodePointAt(text, i);
            if (!Contains(PythonCaseData.CaseIgnorable, codePoint)) return Contains(PythonCaseData.Cased, codePoint);
        }
        return false;
    }

    private static int CodePointLength(string text, int index) =>
        index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;

    private static int CodePointAt(string text, int index) =>
        CodePointLength(text, index) == 2 ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];

    /// <param name="ranges">Pairs of first and last code point, in ascending order.</param>
    private static bool Contains(int[] ranges, int codePoint)
    {
        int low = 0, high = ranges.Length / 2 - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (codePoint < ranges[2 * middle]) high = middle - 1;
            else if (codePoint > ranges[2 * middle + 1]) low = middle + 1;
            else return true;
        }
        return false;
    }

    /// <summary>Python repr() of a str.</summary>
    public static string Repr(string text)
    {
        var quote = text.Contains('\'') && !text.Contains('"') ? '"' : '\'';
        var builder = new StringBuilder().Append(quote);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == quote || c == '\\')
            {
                builder.Append('\\').Append(c);
                continue;
            }

            switch (c)
            {
                case '\t': builder.Append("\\t"); continue;
                case '\n': builder.Append("\\n"); continue;
                case '\r': builder.Append("\\r"); continue;
                case ' ': builder.Append(' '); continue;
            }

            var length = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            var codePoint = length == 2 ? char.ConvertToUtf32(c, text[i + 1]) : c;
            if (IsPrintable(codePoint)) builder.Append(text, i, length);
            else if (codePoint < 0x100) builder.Append("\\x").Append(codePoint.ToString("x2", CultureInfo.InvariantCulture));
            else if (codePoint < 0x10000) builder.Append("\\u").Append(codePoint.ToString("x4", CultureInfo.InvariantCulture));
            else builder.Append("\\U").Append(codePoint.ToString("x8", CultureInfo.InvariantCulture));
            i += length - 1;
        }

        return builder.Append(quote).ToString();
    }

    /// <summary>Python repr() of a list of str, for example <c>['a', 'b']</c>.</summary>
    public static string Repr(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(Repr)) + "]";

    private static bool IsPrintable(int codePoint)
    {
        if (codePoint is >= 0xD800 and <= 0xDFFF) return false;
        return CharUnicodeInfo.GetUnicodeCategory(codePoint) is not (UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator);
    }
}
