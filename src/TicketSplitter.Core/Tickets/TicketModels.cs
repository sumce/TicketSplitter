namespace TicketSplitter.Core.Tickets;

/// <summary>MinerU content_list.json 里的一条内容块。</summary>
public sealed record ContentItem(string Type, string? Text, int PageIndex);

/// <summary>一张切分出来的作业票。页号均为 0 基，EndPage 为开区间上界。</summary>
public sealed record Ticket
{
    /// <summary>1 基序号，与 作业票清单.csv 的「序号」一致。</summary>
    public required int Index { get; init; }
    public required int StartPage { get; init; }
    public required int EndPage { get; init; }
    public required string Type { get; init; }
    public required string Area { get; init; }
    public string? Time { get; init; }

    public int PageCount => EndPage - StartPage;
    public int StartPage1Based => StartPage + 1;

    /// <summary>文件名里的时间片段（与老流水线一致：斜杠换成短横线）。</summary>
    public string TimeForFileName => (Time ?? "时间未知").Replace('/', '-');

    public override string ToString() =>
        $"#{Index} {Type} {Area} {Time ?? "-"} p{StartPage + 1}-{EndPage} ({PageCount}页)";
}

public enum TicketIssue
{
    /// <summary>只有一页。作业票必然包含签字/施工后检查页，单页几乎一定是残片。</summary>
    SinglePage,
    MissingTime,
    UnknownArea,
    UnknownType,
    /// <summary>与上一张票之间存在未覆盖的页。</summary>
    PageGap,
    /// <summary>与上一张票页区间重叠。</summary>
    PageOverlap,
}

public sealed record TicketWarning(int TicketIndex, TicketIssue Issue, string Message);
