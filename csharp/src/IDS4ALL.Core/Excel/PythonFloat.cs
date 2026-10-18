using System.Globalization;
using System.Text.RegularExpressions;
using IDS4ALL.Core.Import;

namespace IDS4ALL.Core.Excel;

/// <summary>The Python functions float() and int(float()) for text.</summary>
public static partial class PythonFloat
{
    // Digits may be separated by single underscores, like in Python source code.
    [GeneratedRegex(@"^[+-]?((\d+(_\d+)*)(\.(\d+(_\d+)*)?)?|\.\d+(_\d+)*)([eE][+-]?\d+(_\d+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^[+-]?(inf|infinity|nan)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpecialPattern();

    /// <summary>float(text). Like Python it accepts the decimal digits of other scripts.</summary>
    /// <exception cref="ImportException">Python raises a ValueError.</exception>
    public static double Parse(string text)
    {
        var stripped = PythonText.Strip(text);
        if (NumberPattern().IsMatch(stripped))
            return double.Parse(AsciiDigits(stripped.Replace("_", "")), NumberStyles.Float, CultureInfo.InvariantCulture);

        if (SpecialPattern().IsMatch(stripped))
        {
            var negative = stripped.StartsWith('-');
            return stripped.TrimStart('+', '-').StartsWith("nan", StringComparison.OrdinalIgnoreCase)
                ? double.NaN
                : negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        throw new ImportException("could not convert string to float: " + PythonText.Repr(text));
    }

    private static string AsciiDigits(string text) =>
        string.Concat(text.Select(c => char.IsDigit(c) ? (char)('0' + CharUnicodeInfo.GetDecimalDigitValue(c)) : c));

    /// <summary>int(float(text)): the number truncated toward zero.</summary>
    /// <exception cref="ImportException">The text is not a number, or the number is not finite or does not fit into an int.</exception>
    public static int ParseInteger(string text)
    {
        var number = Parse(text);
        if (double.IsNaN(number)) throw new ImportException("cannot convert float NaN to integer");
        if (double.IsInfinity(number)) throw new ImportException("cannot convert float infinity to integer");
        var truncated = Math.Truncate(number);
        if (truncated is < int.MinValue or > int.MaxValue) throw new ImportException("The number " + PythonText.Repr(text) + " is too large for a length.");
        return (int)truncated;
    }
}
