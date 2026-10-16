using System.Text.RegularExpressions;

namespace TicketSplitter.Core.Tickets;

/// <summary>
/// 施工区域归一化。产出形如 FAB东区1层；识别不出返回 null。
/// 规则与 Python 老流水线一致（含 PAB/FRB -&gt; FAB 的 OCR 纠错）。
/// </summary>
public static partial class AreaNormalizer
{
    private static readonly Regex AreaSplitRe = new(@"施工区域负责", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRe = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex FabRe = new(@"^FAB(东|西)区([123])层", RegexOptions.Compiled);

    /// <summary>施工区域字段 -&gt; FAB东西区N层。</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // 「施工区域负责人」是另一个字段，先切掉
        var s = AreaSplitRe.Split(raw)[0];
        s = WhitespaceRe.Replace(s, "")
                        .Replace("PAB", "FAB")
                        .Replace("FRB", "FAB");

        var m = FabRe.Match(s);
        return m.Success ? $"FAB{m.Groups[1].Value}区{m.Groups[2].Value}层" : null;
    }

    private static readonly Regex SideRe = new(@"(东|西)(?:区|侧)", RegexOptions.Compiled);
    private static readonly Regex FloorRe = new(@"([一二三123])\s*[层F]", RegexOptions.Compiled);

    private static readonly Dictionary<char, char> CnDigits = new()
    {
        ['一'] = '1', ['二'] = '2', ['三'] = '3',
        ['1'] = '1', ['2'] = '2', ['3'] = '3',
    };

    /// <summary>
    /// 用「作业位置」兜底。老流水线的 LOC_RE 之所以写成 [作业务]{0,2}位置，
    /// 是因为 OCR 经常把「作业位置」吞成「位置」或「业位置」。
    /// </summary>
    public static string? FromLocation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var side = SideRe.Match(raw);
        var floor = FloorRe.Match(raw);
        if (!side.Success || !floor.Success) return null;
        if (!CnDigits.TryGetValue(floor.Groups[1].Value[0], out var digit)) return null;
        return $"FAB{side.Groups[1].Value}区{digit}层";
    }
}
