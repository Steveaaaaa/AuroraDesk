using System;
using System.Text;

namespace AuroraDesk.Core.Helpers;

public static class PinyinHelper
{
    private static readonly Encoding Gb2312;
    private static readonly int[] SecPosValueList =
    [
        1601, 1637, 1833, 2078, 2274, 2302, 2433, 2594, 2787,
        3106, 3212, 3472, 3635, 3722, 3730, 3858, 4027, 4086,
        4390, 4558, 4684, 4925, 5249, 5590
    ];

    private static readonly char[] FirstLetterList =
    [
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'j',
        'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's',
        't', 'w', 'x', 'y', 'z'
    ];

    static PinyinHelper()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Gb2312 = Encoding.GetEncoding("GB2312");
    }

    public static string GetInitials(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c >= 0x4E00 && c <= 0x9FFF)
            {
                sb.Append(GetChineseCharInitial(c));
            }
        }
        return sb.ToString();
    }

    private static char GetChineseCharInitial(char ch)
    {
        byte[] bytes = Gb2312.GetBytes(ch.ToString());
        if (bytes.Length < 2) return ch;
        int secPos = bytes[0] * 100 + bytes[1] - 160 * 100 - 160;
        if (secPos is <= 1600 or >= 5590) return '?';

        for (int i = 0; i < 23; i++)
        {
            if (secPos >= SecPosValueList[i] && secPos < SecPosValueList[i + 1])
            {
                return FirstLetterList[i];
            }
        }
        return '?';
    }

    /// <summary>
    /// Scores how well a query matches a title + its Pinyin initials (higher is better, 0 = no match).
    /// </summary>
    public static int MatchScore(string query, string title, string? precomputedInitials = null)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(title)) return 0;
        string q = query.Trim().ToLowerInvariant();
        string t = title.Trim().ToLowerInvariant();

        if (t == q) return 100;
        if (t.StartsWith(q, StringComparison.Ordinal)) return 90;
        if (t.Contains(q, StringComparison.Ordinal)) return 75;

        string initials = precomputedInitials ?? GetInitials(title);
        if (initials == q) return 85;
        if (initials.StartsWith(q, StringComparison.Ordinal)) return 70;
        if (initials.Contains(q, StringComparison.Ordinal)) return 55;

        // Subsequence match on initials (e.g. "yyls" matching "yysls")
        int qi = 0;
        for (int i = 0; i < initials.Length && qi < q.Length; i++)
        {
            if (initials[i] == q[qi]) qi++;
        }
        if (qi == q.Length && q.Length >= 2) return 40;

        return 0;
    }
}
