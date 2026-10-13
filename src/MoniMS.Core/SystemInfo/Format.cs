using System.Globalization;

namespace MoniMS.Core.SystemInfo;

public static class Format
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(double bytes, int decimals = 1)
    {
        var unit = 0;
        while (bytes >= 1024 && unit < ByteUnits.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes:0} {ByteUnits[unit]}"
            : bytes.ToString("F" + decimals, CultureInfo.InvariantCulture) + " " + ByteUnits[unit];
    }

    public static string Speed(double bytesPerSec) => Bytes(bytesPerSec) + "/s";

    /// <summary>"1 day, 2 hours, 38 mins" (1이면 단수).</summary>
    public static string Uptime(TimeSpan t)
    {
        var time = $"{Count(t.Hours, "hour")}, {Count(t.Minutes, "min")}";
        return t.TotalDays >= 1 ? $"{Count((int)t.TotalDays, "day")}, {time}" : time;
    }

    private static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
