using PdfSharp.Pdf.IO;
using TicketSplitter.Core.Export;

namespace TicketSplitter.Core.Tests;

/// <summary>
/// 双页合并（一张票的页面按左右两张排成 A4 横版）。
/// 用真实作业票做回归，语料不在时跳过。
/// </summary>
public class TwoUpImposerTests
{
    private const string Corpus = @"D:\codex\UNZYP";

    private static string Ticket => Path.Combine(Corpus, "1FXX", "高处作业申请表", "FAB西区1层",
        "ticket_002_高处作业申请表_FAB西区1层_2026-10-02至2026-10-02_page4-8.pdf");

    [Fact]
    public void Five_Pages_Become_Three_A4_Landscape_Sheets()
    {
        if (!File.Exists(Ticket)) return;

        var dst = Path.Combine(Path.GetTempPath(), "twoup_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var sheets = TwoUpImposer.Impose(Ticket, dst);
            Assert.Equal(3, sheets);                       // 5 页 -> 3 面

            using var doc = PdfReader.Open(dst, PdfDocumentOpenMode.Import);
            Assert.Equal(3, doc.PageCount);
            Assert.Equal(841.89, doc.Pages[0].Width.Point, 1);    // A4 横版
            Assert.Equal(595.28, doc.Pages[0].Height.Point, 1);
        }
        finally
        {
            if (File.Exists(dst)) File.Delete(dst);
        }
    }

    [Fact]
    public void Reuses_Page_Objects_Instead_Of_Rasterizing()
    {
        if (!File.Exists(Ticket)) return;

        var dst = Path.Combine(Path.GetTempPath(), "twoup_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            TwoUpImposer.Impose(Ticket, dst);
            var srcKb = new FileInfo(Ticket).Length / 1024.0;
            var dstKb = new FileInfo(dst).Length / 1024.0;

            // 页对象是复用的：体积应当和源在同一量级，而不是翻倍或膨胀
            Assert.True(dstKb < srcKb * 1.2,
                $"合并后体积异常：源 {srcKb:F0}KB -> 结果 {dstKb:F0}KB（像是被栅格化了）");
        }
        finally
        {
            if (File.Exists(dst)) File.Delete(dst);
        }
    }

    [Fact]
    public void Single_Page_Ticket_Produces_One_Sheet_With_Empty_Right_Half()
    {
        if (!File.Exists(Ticket)) return;

        var one = Path.Combine(Path.GetTempPath(), "twoup_src_" + Guid.NewGuid().ToString("N") + ".pdf");
        var dst = Path.Combine(Path.GetTempPath(), "twoup_out_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            using (var info = Pdf.PdfSource.Open(Ticket))
                info.WriteRange(0, 1, one);                   // 只取第 1 页

            Assert.Equal(1, TwoUpImposer.Impose(one, dst));

            using var doc = PdfReader.Open(dst, PdfDocumentOpenMode.Import);
            Assert.Equal(1, doc.PageCount);
            Assert.Equal(841.89, doc.Pages[0].Width.Point, 1);
        }
        finally
        {
            if (File.Exists(one)) File.Delete(one);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }
}
