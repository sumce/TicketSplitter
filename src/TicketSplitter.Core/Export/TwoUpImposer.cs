using PdfSharp.Drawing;
using PdfSharp.Pdf;
using TicketSplitter.Core.Pdf;

namespace TicketSplitter.Core.Export;

public sealed record TwoUpOptions
{
    /// <summary>输出纸张。默认 A4 横版。</summary>
    public double SheetWidth { get; init; } = 841.89;
    public double SheetHeight { get; init; } = 595.28;

    /// <summary>中缝留白（pt），方便对折或裁切。</summary>
    public double Gutter { get; init; } = 8;
}

/// <summary>
/// 把一张票的页面按「左右两张排一面」重排成 A4 横版。
/// 奇数页时最后一面右半留空。
/// </summary>
public static class TwoUpImposer
{
    /// <summary>返回生成的面数。destPath 会被覆盖。</summary>
    public static int Impose(string srcPdf, string destPdf, TwoUpOptions? options = null)
    {
        options ??= new TwoUpOptions();

        using var info = PdfSource.Open(srcPdf);
        var pageCount = info.PageCount;
        if (pageCount == 0)
            throw new InvalidOperationException(srcPdf + " 没有页面");

        var sheetW = options.SheetWidth;
        var sheetH = options.SheetHeight;
        var gutter = options.Gutter;
        var slot = (sheetW - gutter) / 2;

        using var form = XPdfForm.FromFile(srcPdf);
        using var dst = new PdfDocument();

        var sheets = 0;
        for (var i = 0; i < pageCount; i += 2)
        {
            var page = dst.AddPage();
            page.Width = XUnit.FromPoint(sheetW);
            page.Height = XUnit.FromPoint(sheetH);

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                DrawSlot(gfx, form, info.Pages[i], i, slot, sheetH, 0);
                if (i + 1 < pageCount)
                    DrawSlot(gfx, form, info.Pages[i + 1], i + 1, slot, sheetH, slot + gutter);
            }
            sheets++;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(destPdf));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        dst.Save(destPdf);
        return sheets;
    }

    /// <summary>
    /// 关键：按 **CropBox 换算出的可见尺寸** 排版，不能用 PDFsharp 的 Width/Height
    /// （那是 MediaBox）。本项目的扫描件 MediaBox 是整张 A4、CropBox 只有半张，
    /// 直接按 Width 画会把内容横向拉伸一倍。
    /// </summary>
    private static void DrawSlot(
        XGraphics gfx, XPdfForm form, PageInfo page, int pageIndex,
        double slotWidth, double sheetHeight, double left)
    {
        var w = page.VisibleWidth;
        var h = page.VisibleHeight;
        if (w <= 0 || h <= 0) return;

        var scale = Math.Min(slotWidth / w, sheetHeight / h);
        var dw = w * scale;
        var dh = h * scale;
        var dx = left + (slotWidth - dw) / 2;
        var dy = (sheetHeight - dh) / 2;

        form.PageIndex = pageIndex;
        gfx.DrawImage(form, new XRect(dx, dy, dw, dh));
    }
}
