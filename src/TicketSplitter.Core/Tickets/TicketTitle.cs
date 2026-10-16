using System.Text.RegularExpressions;

namespace TicketSplitter.Core.Tickets;

/// <summary>
/// 标题识别。规则沿用现有 Python 流水线，容忍常见 OCR 错字：
/// 「动火作业申请表」会被识别成「动作业申请表 / 动作作业申请表 / 动火火作业申请表」等。
/// </summary>
public static class TicketTitle
{
    public const string Contractor = "承揽商作业申请表";
    public const string HighWork = "高处作业申请表";
    public const string HotWork = "动火作业申请表";
    public const string Unknown = "未知";

    /// <summary>标题文本长度上限，超过就不是标题（避免整段正文里含标题字样被误判）。</summary>
    public const int MaxTitleLength = 12;

    private static readonly Regex TitleRe = new(
        @"^(承揽商作业申请表|高处作业申请表|动[火作]{0,2}作业申请表)$",
        RegexOptions.Compiled);

    private static readonly Regex ContractorRe = new(@"^承揽商", RegexOptions.Compiled);
    private static readonly Regex HighRe = new(@"^高处", RegexOptions.Compiled);

    public static bool TryMatch(string text, out string type)
    {
        type = string.Empty;
        if (text.Length is 0 or > MaxTitleLength) return false;
        if (!TitleRe.IsMatch(text)) return false;
        type = Normalize(text);
        return true;
    }

    public static string Normalize(string text) =>
        ContractorRe.IsMatch(text) ? Contractor :
        HighRe.IsMatch(text) ? HighWork : HotWork;
}
