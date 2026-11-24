using System.Management;

namespace FivemPcReport.Util;

internal static class Wmi
{
    public static List<Dictionary<string, object?>> Query(string wql, string scope = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql));
        searcher.Options.Timeout = TimeSpan.FromSeconds(20);
        foreach (var obj in searcher.Get())
        {
            using (obj)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in obj.Properties)
                    row[p.Name] = p.Value;
                rows.Add(row);
            }
        }
        return rows;
    }

    public static string Str(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null ? (v.ToString() ?? "").Trim() : "";

    public static long Long(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return 0;
        try { return Convert.ToInt64(v); } catch { return 0; }
    }

    public static DateTime? Date(this Dictionary<string, object?> row, string key)
    {
        var s = row.Str(key);
        if (s.Length < 8) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); }
        catch
        {
            return DateTime.TryParseExact(s[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d)
                ? d : null;
        }
    }
}
