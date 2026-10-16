using System.Globalization;
using System.Numerics;
using System.Text;

namespace IDS4ALL.Core.Excel;

/// <summary>Formats values like the Python str() function does.</summary>
public static class PythonFormat
{
    public static string Str(object? value) => value switch
    {
        null => "None",
        string text => text,
        bool flag => flag ? "True" : "False",
        BigInteger number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        double number => Repr(number),
        DateTime dateTime => Str(dateTime),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => Str(time),
        PythonTimedelta span => span.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported value type."),
    };

    /// <summary>Python repr() of a float: shortest round-trip digits, exponent form below 1e-4 and from 1e16.</summary>
    public static string Repr(double value)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        if (value == 0) return double.IsNegative(value) ? "-0.0" : "0.0";

        var sign = value < 0 ? "-" : "";
        var (digits, exponent) = ShortestDigits(Math.Abs(value));
        // The value is 0.<digits> * 10^exponent. The decimal point position is "exponent".
        var scientificExponent = exponent - 1;

        if (scientificExponent < -4 || scientificExponent >= 16)
        {
            var mantissa = digits.Length == 1 ? digits : digits[0] + "." + digits[1..];
            var exponentSign = scientificExponent < 0 ? "-" : "+";
            return $"{sign}{mantissa}e{exponentSign}{Math.Abs(scientificExponent):00}";
        }

        if (exponent <= 0) return $"{sign}0.{new string('0', -exponent)}{digits}";
        if (digits.Length <= exponent) return $"{sign}{digits}{new string('0', exponent - digits.Length)}.0";
        return $"{sign}{digits[..exponent]}.{digits[exponent..]}";
    }

    private static (string Digits, int Exponent) ShortestDigits(double value)
    {
        // "R" gives the shortest string that round-trips. Read digits and exponent from it.
        var shortest = value.ToString("R", CultureInfo.InvariantCulture);
        var mantissaText = shortest;
        var exponent10 = 0;
        var eIndex = shortest.IndexOfAny(['E', 'e']);
        if (eIndex >= 0)
        {
            mantissaText = shortest[..eIndex];
            exponent10 = int.Parse(shortest[(eIndex + 1)..], CultureInfo.InvariantCulture);
        }

        var dot = mantissaText.IndexOf('.');
        var integerPart = dot < 0 ? mantissaText : mantissaText[..dot];
        var fractionPart = dot < 0 ? "" : mantissaText[(dot + 1)..];
        var allDigits = integerPart + fractionPart;
        var pointPosition = integerPart.Length + exponent10;

        var leadingZeros = allDigits.Length - allDigits.TrimStart('0').Length;
        allDigits = allDigits.TrimStart('0');
        pointPosition -= leadingZeros;
        allDigits = allDigits.TrimEnd('0');
        return (allDigits, pointPosition);
    }

    public static string Str(DateTime value)
    {
        var text = value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return value.Ticks % TimeSpan.TicksPerSecond == 0 ? text : text + "." + Microseconds(value.Ticks);
    }

    public static string Str(TimeOnly value)
    {
        var text = value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return value.Ticks % TimeSpan.TicksPerSecond == 0 ? text : text + "." + Microseconds(value.Ticks);
    }

    private static string Microseconds(long ticks) =>
        (ticks % TimeSpan.TicksPerSecond / 10).ToString("000000", CultureInfo.InvariantCulture);
}
