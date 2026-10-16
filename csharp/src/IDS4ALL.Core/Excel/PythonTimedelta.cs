using System.Globalization;
using System.Numerics;
using System.Text;

namespace IDS4ALL.Core.Excel;

/// <summary>A Python datetime.timedelta. It reaches 999999999 days, more than a .NET TimeSpan.</summary>
public readonly record struct PythonTimedelta
{
    private const long MicrosecondsPerDay = 86_400_000_000;
    private const long MicrosecondsPerSecond = 1_000_000;
    private const long MaxDays = 999_999_999;

    public BigInteger Microseconds { get; }

    /// <exception cref="OverflowException">The value is outside the range of a Python timedelta.</exception>
    public PythonTimedelta(BigInteger microseconds)
    {
        var days = BigInteger.Divide(microseconds - (microseconds.Sign < 0 ? MicrosecondsPerDay - 1 : 0), MicrosecondsPerDay);
        if (BigInteger.Abs(days) > MaxDays + 1 || days < -MaxDays || days > MaxDays)
            throw new OverflowException($"days={days}; must have magnitude <= {MaxDays}");
        Microseconds = microseconds;
    }

    /// <summary>Python <c>timedelta(days=value)</c>. The microseconds are rounded half to even.</summary>
    public static PythonTimedelta FromDays(double days)
    {
        if (double.IsNaN(days) || double.IsInfinity(days)) throw new OverflowException("cannot convert float to integer");
        var whole = Math.Truncate(days);
        var fraction = days - whole;
        var sum = new BigInteger(whole) * MicrosecondsPerDay;
        if (fraction != 0)
        {
            var microseconds = MicrosecondsPerDay * fraction;
            var wholeMicroseconds = Math.Truncate(microseconds);
            sum += new BigInteger(wholeMicroseconds);
            var leftover = microseconds - wholeMicroseconds;
            if (leftover != 0) sum += new BigInteger(Math.Round(leftover, MidpointRounding.ToEven));
        }
        return new PythonTimedelta(sum);
    }

    public static PythonTimedelta FromDays(BigInteger days) => new(days * MicrosecondsPerDay);

    public static PythonTimedelta FromSeconds(double seconds, long microseconds) =>
        new(new BigInteger(seconds) * MicrosecondsPerSecond + microseconds);

    /// <summary>The microseconds field of a normalized timedelta (0 to 999999).</summary>
    public long MicrosecondsField => (long)(((Microseconds % MicrosecondsPerSecond) + MicrosecondsPerSecond) % MicrosecondsPerSecond);

    /// <summary>Python <c>total_seconds()</c>.</summary>
    public double TotalSeconds => (double)((decimal)Microseconds / MicrosecondsPerSecond);

    /// <summary>Python str(timedelta), for example "1 day, 2:03:04" or "-1 day, 23:59:59".</summary>
    public override string ToString()
    {
        var days = BigInteger.Divide(Microseconds - (Microseconds.Sign < 0 ? MicrosecondsPerDay - 1 : 0), MicrosecondsPerDay);
        var rest = (long)(Microseconds - days * MicrosecondsPerDay);
        var seconds = rest / MicrosecondsPerSecond;
        var microseconds = rest % MicrosecondsPerSecond;

        var builder = new StringBuilder();
        if (!days.IsZero)
            builder.Append(CultureInfo.InvariantCulture, $"{days} day{(BigInteger.Abs(days).IsOne ? "" : "s")}, ");
        builder.Append(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}");
        if (microseconds != 0) builder.Append(CultureInfo.InvariantCulture, $".{microseconds:000000}");
        return builder.ToString();
    }
}
