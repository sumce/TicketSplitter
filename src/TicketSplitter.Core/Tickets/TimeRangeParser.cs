using System.Text.RegularExpressions;

namespace TicketSplitter.Core.Tickets;

/// <summary>施工时间解析：xxxx-xx-xx 至 xxxx-xx-xx。</summary>
public static class TimeRangeParser
{
    private static readonly Regex TimeRe = new(
        @"施工时间[:：]?\s*从?\s*([0-9]{4}[-/.][0-9]{1,2}[-/.][0-9]{1,2})" +
        @"\s*(?:至|到|-|~)\s*([0-9]{4}[-/.][0-9]{1,2}[-/.][0-9]{1,2})",
        RegexOptions.Compiled);

    /// <summary>在任何位置找施工时间；找不到返回 null。返回格式统一为 起至止。</summary>
    public static string? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = TimeRe.Match(text);
        return m.Success ? $"{m.Groups[1].Value}至{m.Groups[2].Value}" : null;
    }

    /// <summary>取起止日期（用于按日期分组等）。</summary>
    public static (string Start, string End)? ParseParts(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = TimeRe.Match(text);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }
}
