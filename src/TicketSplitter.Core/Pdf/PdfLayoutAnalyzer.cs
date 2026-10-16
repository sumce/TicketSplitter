namespace TicketSplitter.Core.Pdf;

public enum PdfLayout
{
    /// <summary>每页本身就是一张 A5 竖版表，原样使用。</summary>
    HalfSheet,
    /// <summary>每页是一整张横版（并排两张表），需要拆左右半幅。</summary>
    Spread,
    /// <summary>两种混在一起，自动判定不可靠，必须让用户确认。</summary>
    Mixed,
}

public sealed record LayoutAnalysis(
    PdfLayout Layout,
    int PageCount,
    int SpreadPages,
    int FlatPages,
    double SpreadRatio,
    IReadOnlyList<string> Notes)
{
    public bool NeedsHalfSplit => Layout == PdfLayout.Spread;

    public string Label => Layout switch
    {
        PdfLayout.Spread => "整版（一页两张，将拆成左右半幅）",
        PdfLayout.HalfSheet => "半幅（每页一张，原样使用）",
        _ => "混合版式（需人工确认）",
    };

    public string Detail =>
        $"共 {PageCount} 页：整版 {SpreadPages} 页、半幅 {FlatPages} 页（整版占比 {SpreadRatio:P0}）";
}

public static class PdfLayoutAnalyzer
{
    /// <summary>
    /// 按「整版页占比」判定版式。相比只抽样前 8 页的老做法，
    /// 这里对全部页统计，能识别出老流水线会静默当成半幅处理的混合文档。
    /// </summary>
    public static LayoutAnalysis Analyze(IReadOnlyList<PageInfo> pages, double spreadThreshold = 0.98)
    {
        if (pages.Count == 0)
            throw new ArgumentException("文档没有页面", nameof(pages));

        var spread = pages.Count(p => p.IsSpread);
        var flat = pages.Count - spread;
        var ratio = (double)spread / pages.Count;

        var layout = ratio >= spreadThreshold ? PdfLayout.Spread
                   : ratio <= 1 - spreadThreshold ? PdfLayout.HalfSheet
                   : PdfLayout.Mixed;

        var notes = new List<string>();
        if (layout == PdfLayout.Mixed)
            notes.Add("文档内混有整版页与半幅页，自动拆分可能破坏页序，请确认后再继续。");

        // 按 5pt 分箱：真实扫描件里同一批纸常出现 420 与 421 这种 1pt 抖动，
        // 直接按整数分组会把它们当成两种尺寸而误报。
        var oddSizes = pages.GroupBy(p => (Bucket(p.VisibleWidth), Bucket(p.VisibleHeight)))
                            .OrderByDescending(g => g.Count())
                            .ToList();
        if (oddSizes.Count > 1)
        {
            var main = oddSizes[0];
            var others = oddSizes.Skip(1).Sum(g => g.Count());
            if (others > 0 && others * 100 / pages.Count >= 5)
                notes.Add($"可见尺寸不统一：主尺寸 {main.Key.Item1}x{main.Key.Item2}（{main.Count()} 页），" +
                          $"另有 {others} 页尺寸不同。可能是扫描件里混入了空白页或异形页。");
        }

        return new LayoutAnalysis(layout, pages.Count, spread, flat, ratio, notes);
    }

    public static LayoutAnalysis Analyze(string path) => Analyze(PdfSource.Open(path).Pages);

    /// <summary>把尺寸归一到 5pt 的分箱，容忍扫描/裁切带来的亚点级抖动。</summary>
    private static double Bucket(double value) => Math.Round(value / 5.0) * 5.0;
}
