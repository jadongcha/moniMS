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

    public static string Uptime(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} days, {t.Hours} hours, {t.Minutes} mins" : $"{t.Hours} hours, {t.Minutes} mins";
}
