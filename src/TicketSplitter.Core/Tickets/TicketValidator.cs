namespace TicketSplitter.Core.Tickets;

/// <summary>
/// 产出体检。老流水线无论如何都会打印「完成」，单页残片、区域/时间缺失、
/// 页覆盖缺口都不会被发现——这里把它们显式暴露出来。
/// </summary>
public static class TicketValidator
{
    /// <summary>作业票必然含签字与施工后检查页；只有一页基本可以断定是切分残片。</summary>
    public const int SuspiciousMinPages = 2;

    public static IReadOnlyList<TicketWarning> Validate(IReadOnlyList<Ticket> tickets, int totalPages)
    {
        var warnings = new List<TicketWarning>();

        if (tickets.Count == 0)
        {
            warnings.Add(new TicketWarning(0, TicketIssue.UnknownType,
                "没有识别到任何作业票：解析结果里找不到票标题，请检查 OCR 质量或版式设置。"));
            return warnings;
        }

        if (tickets[0].StartPage > 0)
            warnings.Add(new TicketWarning(tickets[0].Index, TicketIssue.PageGap,
                $"第 1-{tickets[0].StartPage} 页（共 {tickets[0].StartPage} 页）不属于任何一张票，未被归档。"));

        for (var i = 0; i < tickets.Count; i++)
        {
            var t = tickets[i];

            if (t.PageCount < SuspiciousMinPages)
                warnings.Add(new TicketWarning(t.Index, TicketIssue.SinglePage,
                    $"票 #{t.Index} 只有 {t.PageCount} 页（{t.Type} {t.Area}），很可能是切分残片，建议人工核对。"));

            if (string.IsNullOrWhiteSpace(t.Time))
                warnings.Add(new TicketWarning(t.Index, TicketIssue.MissingTime,
                    $"票 #{t.Index} 没解析出施工时间。"));

            if (t.Area == TicketTitle.Unknown)
                warnings.Add(new TicketWarning(t.Index, TicketIssue.UnknownArea,
                    $"票 #{t.Index} 没解析出施工区域。"));

            if (t.Type == TicketTitle.Unknown)
                warnings.Add(new TicketWarning(t.Index, TicketIssue.UnknownType,
                    $"票 #{t.Index} 的票类型未知。"));

            if (i > 0)
            {
                var prev = tickets[i - 1];
                if (t.StartPage > prev.EndPage)
                    warnings.Add(new TicketWarning(t.Index, TicketIssue.PageGap,
                        $"票 #{prev.Index} 与 #{t.Index} 之间缺第 {prev.EndPage + 1}-{t.StartPage} 页。"));
                else if (t.StartPage < prev.EndPage)
                    warnings.Add(new TicketWarning(t.Index, TicketIssue.PageOverlap,
                        $"票 #{prev.Index} 与 #{t.Index} 页区间重叠。"));
            }
        }

        var last = tickets[^1];
        if (last.EndPage < totalPages)
            warnings.Add(new TicketWarning(last.Index, TicketIssue.PageGap,
                $"第 {last.EndPage + 1}-{totalPages} 页不属于任何一张票，未被归档。"));

        return warnings;
    }
}
