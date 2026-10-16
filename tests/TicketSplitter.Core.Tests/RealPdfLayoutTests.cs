using TicketSplitter.Core.Pdf;

namespace TicketSplitter.Core.Tests;

/// <summary>针对真实扫描件的版式识别回归；语料不在时自动跳过。</summary>
public class RealPdfLayoutTests
{
    private const string Corpus = @"D:\codex\UNZYP";

    [Fact]
    public void Already_Halved_Pdf_Is_Detected_As_HalfSheet()
    {
        var path = Path.Combine(Corpus, "PDF合并.pdf");
        if (!File.Exists(path)) return;

        using var src = PdfSource.Open(path);
        var analysis = PdfLayoutAnalyzer.Analyze(src.Pages);

        Assert.Equal(400, src.PageCount);
        Assert.Equal(PdfLayout.HalfSheet, analysis.Layout);
        Assert.False(analysis.NeedsSplit);
        Assert.Equal(SheetSplitMode.None, analysis.SuggestedSplitMode);
    }

    [Fact]
    public void Full_Spread_Scan_Is_Detected_And_Split_Into_Twice_The_Pages()
    {
        var path = Path.Combine(Corpus, "作业票", "20260916212450093.pdf");
        if (!File.Exists(path)) return;

        using var src = PdfSource.Open(path);
        var analysis = PdfLayoutAnalyzer.Analyze(src.Pages);
        Assert.Equal(PdfLayout.Spread, analysis.Layout);
        Assert.True(analysis.NeedsSplit);
        Assert.Equal(SheetSplitMode.LeftRight, analysis.SuggestedSplitMode);

        // 真实的退化 CropBox 必须被回退掉，否则可见尺寸会是 0
        Assert.All(src.Pages, p => Assert.True(p.VisibleWidth > 100));

        var outPath = Path.Combine(Path.GetTempPath(), $"ticketsplitter_test_{Guid.NewGuid():N}.pdf");
        try
        {
            var written = src.WriteSplit(outPath, SheetSplitMode.LeftRight);
            Assert.Equal(src.PageCount * 2, written);

            using var result = PdfSource.Open(outPath);
            Assert.Equal(src.PageCount * 2, result.PageCount);
            Assert.Equal(PdfLayout.HalfSheet, PdfLayoutAnalyzer.Analyze(result.Pages).Layout);

            // 图片 XObject 必须被复用：拆半幅不应让体积翻倍
            var srcLen = new FileInfo(path).Length;
            var dstLen = new FileInfo(outPath).Length;
            Assert.True(dstLen < srcLen * 1.35,
                $"拆半幅后体积异常膨胀：{srcLen / 1048576.0:F1}MB -> {dstLen / 1048576.0:F1}MB");
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
