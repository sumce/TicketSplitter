using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace TicketSplitter.Core.Pdf;

/// <summary>
/// 打开一次源 PDF，复用同一个 reader 导出多张票。
/// 实测（PDFsharp 6.1.1）同一个 Import 模式 reader 可以连续写入多个输出文档而不串页。
/// </summary>
public sealed class PdfSource : IDisposable
{
    private readonly PdfDocument _reader;

    private PdfSource(string path, PdfDocument reader, IReadOnlyList<PageInfo> pages)
    {
        Path = path;
        _reader = reader;
        Pages = pages;
    }

    public string Path { get; }
    public int PageCount => _reader.PageCount;
    public IReadOnlyList<PageInfo> Pages { get; }

    public static PdfSource Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到 PDF：{path}", path);

        var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var pages = new List<PageInfo>(doc.PageCount);
        for (var i = 0; i < doc.PageCount; i++)
            pages.Add(ReadPageInfo(doc.Pages[i], i));
        return new PdfSource(path, doc, pages);
    }

    internal static PageInfo ReadPageInfo(PdfPage page, int index)
    {
        var media = ToBox(page.MediaBox);

        // 注意：必须用 PdfPage.CropBox 属性，而不是 Elements["/CropBox"]。
        // 后者返回的是原始 PdfArray，拿 is PdfRectangle 判断会永远失败，
        // 结果是所有 CropBox 被当成「不存在」而回退 MediaBox —— 已由真实数据测试抓出。
        var crop = ToBox(page.CropBox);

        // 退化 CropBox（[0 0 0 0]）丢弃，交给 PageInfo.Box 回退 MediaBox
        return new PageInfo(index, media, crop.IsUsable ? crop : null, page.Rotate);
    }

    private static PageBox ToBox(PdfRectangle r) =>
        new PageBox(r.X1, r.Y1, r.X2, r.Y2).Normalized();

    /// <summary>把 [start, endExclusive) 页导出为一个新 PDF。</summary>
    public void WriteRange(int start, int endExclusive, string destPath) =>
        WriteRanges([(start, endExclusive)], destPath);

    /// <summary>把若干页区间按给定顺序导出为**一个**新 PDF（页区间内按原顺序）。</summary>
    public void WriteRanges(IReadOnlyList<(int Start, int End)> ranges, string destPath)
    {
        using var dst = new PdfDocument();
        foreach (var (start, end) in ranges)
        {
            if (start < 0 || end > PageCount || start >= end)
                throw new ArgumentOutOfRangeException(nameof(ranges),
                    $"页区间非法：[{start},{end})，文档共 {PageCount} 页");
            for (var i = start; i < end; i++)
                dst.AddPage(_reader.Pages[i]);
        }
        EnsureDirectory(destPath);
        dst.Save(destPath);
    }

    /// <summary>把某一页按样式拆成一个小的预览 PDF（1~2 页），用于样式预览。</summary>
    public void WritePageSplit(int pageIndex, SheetSplitMode mode, string destPath)
    {
        if (pageIndex < 0 || pageIndex >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        using var dst = new PdfDocument();
        var page = _reader.Pages[pageIndex];

        if (mode == SheetSplitMode.None)
        {
            dst.AddPage(page);
        }
        else
        {
            foreach (var box in SheetSplitter.Split(Pages[pageIndex], mode))
            {
                var np = dst.AddPage(page);
                np.CropBox = new PdfSharp.Pdf.PdfRectangle(
                    new PdfSharp.Drawing.XPoint(box.X1, box.Y1),
                    new PdfSharp.Drawing.XSize(box.Width, box.Height));
            }
        }

        EnsureDirectory(destPath);
        dst.Save(destPath);
    }

    /// <summary>
    /// 按样式导出：一面两页会拆成两页，None 则原样输出。
    /// 用 AddPage 复用同一源页并只改 CropBox，图片 XObject 不会翻倍。
    /// </summary>
    public int WriteSplit(string destPath, SheetSplitMode mode,
                          IProgress<int>? progress = null, CancellationToken ct = default)
    {
        using var dst = new PdfDocument();
        var written = 0;

        for (var i = 0; i < PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = _reader.Pages[i];

            if (mode == SheetSplitMode.None)
            {
                // 不切分时不动 CropBox，避免给退化 CropBox 的扫描件凭空加上一个
                dst.AddPage(page);
                written++;
            }
            else
            {
                foreach (var box in SheetSplitter.Split(Pages[i], mode))
                {
                    var np = dst.AddPage(page);
                    np.CropBox = new PdfSharp.Pdf.PdfRectangle(
                        new PdfSharp.Drawing.XPoint(box.X1, box.Y1),
                        new PdfSharp.Drawing.XSize(box.Width, box.Height));
                    written++;
                }
            }

            progress?.Report(i + 1);
        }

        EnsureDirectory(destPath);
        dst.Save(destPath);
        return written;
    }

    private static void EnsureDirectory(string destPath)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public void Dispose() => _reader.Dispose();
}
