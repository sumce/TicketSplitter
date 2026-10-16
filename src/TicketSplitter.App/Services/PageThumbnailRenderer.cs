using Microsoft.UI;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;

namespace TicketSplitter.App.Services;

/// <summary>
/// 用 Windows 自带的 Windows.Data.Pdf 渲染页面缩略图（不引入额外 PDF 渲染依赖）。
/// 渲染会遵循页面 CropBox，所以半幅拆分后的结果能直接看到。
/// </summary>
public sealed class PageThumbnailRenderer : IDisposable
{
    private readonly PdfDocument _document;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PageThumbnailRenderer(PdfDocument document) => _document = document;

    public int PageCount => (int)_document.PageCount;

    public static async Task<PageThumbnailRenderer> LoadAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var doc = await PdfDocument.LoadFromFileAsync(file);
        return new PageThumbnailRenderer(doc);
    }

    /// <summary>渲染一页为 BitmapImage。PdfDocument 不是线程安全的，这里串行化。</summary>
    public async Task<BitmapImage?> RenderAsync(int pageIndex, int targetWidth, CancellationToken ct = default)
    {
        if (pageIndex < 0 || pageIndex >= PageCount) return null;

        await _gate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            using var page = _document.GetPage((uint)pageIndex);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();

            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(40, targetWidth),
                BackgroundColor = Colors.White,
            };

            await page.RenderToStreamAsync(stream, options).AsTask(ct).ConfigureAwait(true);
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream).AsTask(ct).ConfigureAwait(true);
            return bitmap;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        // Windows.Data.Pdf.PdfDocument 是 WinRT 对象，由 GC/COM 释放，没有 Dispose
        _gate.Dispose();
    }
}
