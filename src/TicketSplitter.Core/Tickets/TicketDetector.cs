using System.Text.RegularExpressions;

namespace TicketSplitter.Core.Tickets;

/// <summary>
/// 切票：碰到票标题就切开。
/// 逐条对应现有 Python 流水线 split_tool.py / ticket_split.py 的 detect_tickets，
/// 以便用真实产出（作业票清单.csv）做回归比对。
/// </summary>
public static class TicketDetector
{
    private static readonly Regex AreaFieldRe = new(@"施工区域[:：]\s*([^\n]+)", RegexOptions.Compiled);

    /// <summary>「作业位置」常被 OCR 吞字，故前缀最多两个错字。</summary>
    private static readonly Regex LocationFieldRe = new(@"[作业务]{0,2}位置[:：]\s*([^\n]+)", RegexOptions.Compiled);

    /// <summary>施工时间可能被 OCR 切成多个文本块，向后拼接若干个再匹配。</summary>
    private const int TimeLookahead = 4;

    /// <summary>两个标题页相邻（间隔 1）时不一定是新票；但间隔大于 1 一定是新票。</summary>
    private const int NewTicketPageGap = 1;

    public static IReadOnlyList<Ticket> Detect(MineruContentList content, int totalPages)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (totalPages <= 0) throw new ArgumentOutOfRangeException(nameof(totalPages));

        var pageTexts = content.PageTexts;

        // ---- 1. 找出标题页 ----
        var titlePages = new SortedDictionary<int, string>();
        foreach (var (page, texts) in pageTexts)
        {
            foreach (var t in texts)
            {
                if (TicketTitle.TryMatch(t, out var type))
                {
                    titlePages[page] = type;
                    break;
                }
            }
        }

        if (titlePages.Count == 0)
            return [];

        // ---- 2. 判定票起点 ----
        var starts = new List<int>();
        var prevPage = -10;
        string? prevType = null;
        string? prevTime = null;

        foreach (var (page, type) in titlePages)
        {
            var pageTime = PageTimeOf(pageTexts, page);
            var isNew = page - prevPage > NewTicketPageGap
                        || type != prevType
                        || (pageTime is not null && prevTime is not null && pageTime != prevTime);
            if (isNew) starts.Add(page);

            prevPage = page;
            prevType = type;
            prevTime = pageTime;
        }

        // ---- 3. 逐页提取区域与时间 ----
        var pageArea = new Dictionary<int, string>();
        var pageTimeByPage = new Dictionary<int, string>();

        foreach (var (page, texts) in pageTexts)
        {
            var joined = string.Concat(texts);

            var areaMatch = AreaFieldRe.Match(joined);
            if (areaMatch.Success)
            {
                var a = AreaNormalizer.Normalize(areaMatch.Groups[1].Value);
                if (a is not null) pageArea.TryAdd(page, a);
            }

            var locMatch = LocationFieldRe.Match(joined);
            if (locMatch.Success)
            {
                var a = AreaNormalizer.FromLocation(locMatch.Groups[1].Value);
                if (a is not null) pageArea.TryAdd(page, a);
            }

            for (var i = 0; i < texts.Count; i++)
            {
                if (!texts[i].Contains("施工时间")) continue;
                var t = TimeRangeParser.Parse(Join(texts, i, TimeLookahead));
                if (t is not null) pageTimeByPage.TryAdd(page, t);
                break;
            }
        }

        // ---- 4. 组装票 ----
        var tickets = new List<Ticket>();
        string? prevArea = null;
        var bounds = new List<int>(starts) { totalPages };

        for (var i = 0; i < bounds.Count - 1; i++)
        {
            var start = bounds[i];
            var end = bounds[i + 1];

            var type = titlePages.TryGetValue(start, out var tv) ? tv : TicketTitle.Unknown;

            string? area = null;
            for (var p = start; p < end; p++)
                if (pageArea.TryGetValue(p, out var av)) { area = av; break; }
            area ??= prevArea ?? TicketTitle.Unknown;
            if (area != TicketTitle.Unknown) prevArea = area;

            string? span = null;
            for (var p = start; p < end; p++)
                if (pageTimeByPage.TryGetValue(p, out var tv2)) { span = tv2; break; }

            tickets.Add(new Ticket
            {
                Index = i + 1,
                StartPage = start,
                EndPage = end,
                Type = type,
                Area = area,
                Time = span,
            });
        }

        // 老流水线在导出阶段还有一道兜底：相邻票兜底区域。
        // 放在这里，保证「界面上看到的」和「导出到 CSV 的」完全一致。
        for (var i = 1; i < tickets.Count; i++)
        {
            if (tickets[i].Area != TicketTitle.Unknown) continue;
            var prev = tickets[i - 1].Area;
            if (prev != TicketTitle.Unknown)
                tickets[i] = tickets[i] with { Area = prev };
        }

        return tickets;
    }

    private static string? PageTimeOf(IReadOnlyDictionary<int, List<string>> pageTexts, int page)
    {
        if (!pageTexts.TryGetValue(page, out var texts)) return null;
        for (var i = 0; i < texts.Count; i++)
        {
            if (!texts[i].Contains("施工时间")) continue;
            var t = TimeRangeParser.Parse(Join(texts, i, TimeLookahead));
            if (t is not null) return t;
        }
        return null;
    }

    private static string Join(List<string> texts, int start, int count)
    {
        var end = Math.Min(texts.Count, start + count);
        var sb = new System.Text.StringBuilder();
        for (var i = start; i < end; i++) sb.Append(texts[i]);
        return sb.ToString();
    }
}
