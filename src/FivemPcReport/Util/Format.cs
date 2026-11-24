using System.Globalization;

namespace FivemPcReport.Util;

internal static class Format
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

    public static string Bytes(long? bytes)
    {
        if (bytes is null) return "n/d";
        double b = bytes.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
        return b.ToString(i >= 3 ? "0.0" : "0", It) + " " + units[i];
    }

    public static string Gb(long bytes) => (bytes / 1024d / 1024 / 1024).ToString("0.0", It) + " GB";

    public static string Pct(double? v) => v is null ? "n/d" : v.Value.ToString("0", It) + "%";

    public static string Num(double v, string fmt = "0.#") => v.ToString(fmt, It);

    public static string Date(DateTime? d) => d is null ? "n/d" : d.Value.ToString("dd/MM/yyyy", It);

    public static string YesNo(bool? b, string yes = "sì", string no = "no") => b is null ? "n/d" : b.Value ? yes : no;

    public static string AgeMonths(DateTime? d)
    {
        if (d is null) return "";
        var months = (int)((DateTime.Now - d.Value).TotalDays / 30.4);
        return months <= 0 ? "questo mese" : months == 1 ? "1 mese fa" : $"{months} mesi fa";
    }
}
