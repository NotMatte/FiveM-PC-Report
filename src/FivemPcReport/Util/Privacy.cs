using System.Text.RegularExpressions;

namespace FivemPcReport.Util;

/// <summary>
/// Hides what identifies the person in everything that ends up in the report: Windows user name, PC name,
/// IP addresses and FiveM identifiers (license, steam, discord, xbl, live, fivem).
/// </summary>
internal static partial class Privacy
{
    private static readonly string UserName = Environment.UserName;
    private static readonly string MachineName = Environment.MachineName;

    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = text;
        if (UserName.Length > 2)
            s = Regex.Replace(s, @"(?<=\\Users\\)" + Regex.Escape(UserName) + @"(?=\\|$)", "<utente>", RegexOptions.IgnoreCase);
        if (UserName.Length > 3)
            s = Regex.Replace(s, @"\b" + Regex.Escape(UserName) + @"\b", "<utente>", RegexOptions.IgnoreCase);
        if (MachineName.Length > 3)
            s = Regex.Replace(s, @"\b" + Regex.Escape(MachineName) + @"\b", "<pc>", RegexOptions.IgnoreCase);
        s = Ipv4().Replace(s, m => IsVersionLike(s, m) ? m.Value : "<ip>");
        s = Identifier().Replace(s, m => m.Groups[1].Value + ":<id>");
        return s;
    }

    // "1.0.2903.40" looks like an IP: keep it when it is part of a longer dotted number or a version string.
    private static bool IsVersionLike(string s, Match m)
    {
        int before = m.Index - 1, after = m.Index + m.Length;
        if (before >= 0 && (s[before] == '.' || char.IsDigit(s[before]))) return true;
        if (after < s.Length && s[after] == '.' && after + 1 < s.Length && char.IsDigit(s[after + 1])) return true;
        var prefix = s[Math.Max(0, m.Index - 16)..m.Index];
        return Regex.IsMatch(prefix, @"(?i)\b(version[ei]?|versione|ver\.?|v|build)\s*$");
    }

    [GeneratedRegex(@"\b(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}\b")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"\b(license2?|steam|discord|xbl|live|fivem|ip):[0-9a-fA-F]{4,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex Identifier();
}
