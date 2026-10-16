namespace TicketSplitter.Core.Pdf;

public enum PdfLayout
{
    /// <summary>每页本身就是一张 A5 竖版表，原样使用。</summary>
    HalfSheet,

    /// <summary>一面两页、左右并排（横版扫描）。</summary>
    Spread,

    /// <summary>一面两页、上下排列（竖版 A4，两张 A5 堆叠）。</summary>
    Stacked,

    /// <summary>多种混在一起，自动判定不可靠，必须让用户确认。</summary>
    Mixed,
}

public sealed record LayoutAnalysis(
    PdfLayout Layout,
    int PageCount,
    int SpreadPages,
    int StackedPages,
    int FlatPages,
    double DominantRatio,
    IReadOnlyList<string> Notes)
{
    /// <summary>建议的切分样式；Mixed 时不给建议，交由用户决定。</summary>
    public SheetSplitMode SuggestedSplitMode => Layout switch
    {
        PdfLayout.Spread => SheetSplitMode.LeftRight,
        PdfLayout.Stacked => SheetSplitMode.TopBottom,
        _ => SheetSplitMode.None,
    };

    public bool NeedsSplit => SuggestedSplitMode != SheetSplitMode.None;

    public string Label => Layout switch
    {
        PdfLayout.Spread => "一面两页 · 左右并排",
        PdfLayout.Stacked => "一面两页 · 上下排列",
        PdfLayout.HalfSheet => "每页一张票",
        _ => "版式混杂，需人工确认",
    };

    public string Detail =>
        $"共 {PageCount} 页：每页一张 {FlatPages} 页、左右两张 {SpreadPages} 页、上下两张 {StackedPages} 页" +
        $"（主导版式占比 {DominantRatio:P0}）";
}

public static class PdfLayoutAnalyzer
{
    /// <summary>
    /// 按"主导版式占比"判定。相比只抽样前 8 页的老做法，这里对全部页统计，
    /// 能识别出老流水线会静默当成半幅处理的混合文档。
    /// </summary>
    public static LayoutAnalysis Analyze(IReadOnlyList<PageInfo> pages, double threshold = 0.98)
    {
        if (pages.Count == 0)
            throw new ArgumentException("文档没有页面", nameof(pages));

        var n = pages.Count;
        var spread = pages.Count(IsLeftRight);
        var stacked = pages.Count(IsTopBottom);
        var flat = n - spread - stacked;

        PdfLayout layout;
        if (spread >= n * threshold) layout = PdfLayout.Spread;
        else if (stacked >= n * threshold) layout = PdfLayout.Stacked;
        else if (flat >= n * threshold) layout = PdfLayout.HalfSheet;
        else layout = PdfLayout.Mixed;

        var dominant = Math.Max(spread, Math.Max(stacked, flat)) / (double)n;

        var notes = new List<string>();
        if (layout == PdfLayout.Mixed)
            notes.Add("文档内混有多种版式，自动拆分可能破坏页序，请确认后再继续。");

        // 按 5pt 分箱：同一批纸常出现 420 与 421 这种 1pt 抖动，
        // 直接按整数分组会把它们当成两种尺寸而误报。
        var sizes = pages.GroupBy(p => (Bucket(p.VisibleWidth), Bucket(p.VisibleHeight)))
                         .OrderByDescending(g => g.Count())
                         .ToList();
        if (sizes.Count > 1)
        {
            var others = sizes.Skip(1).Sum(g => g.Count());
            if (others * 100 / n >= 5)
                notes.Add($"可见尺寸不统一：主尺寸 {sizes[0].Key.Item1}x{sizes[0].Key.Item2}（{sizes[0].Count()} 页），" +
                          $"另有 {others} 页尺寸不同。可能是扫描件里混入了空白页或异形页。");
        }

        return new LayoutAnalysis(layout, n, spread, stacked, flat, dominant, notes);
    }

    public static LayoutAnalysis Analyze(string path) => Analyze(PdfSource.Open(path).Pages);

    /// <summary>一面两页、左右并排：可见区明显是横的。</summary>
    private static bool IsLeftRight(PageInfo p) => p.IsSpread;

    /// <summary>
    /// 一面两页、上下排列：可见区是竖版 A4（约 595x842），而不是单张 A5（约 420x595）。
    /// 两者长宽比几乎一样（0.707 vs 0.706），只能靠绝对尺寸区分。
    /// </summary>
    private static bool IsTopBottom(PageInfo p) =>
        !p.IsSpread && p.VisibleWidth >= 500 && p.VisibleHeight >= 750;

    /// <summary>把尺寸归一到 5pt 的分箱，容忍扫描/裁切带来的亚点级抖动。</summary>
    private static double Bucket(double value) => Math.Round(value / 5.0) * 5.0;
}
