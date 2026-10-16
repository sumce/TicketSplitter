using TicketSplitter.Core.Pdf;

namespace TicketSplitter.Core.Mineru;

/// <summary>
/// 分块策略。页数上限之外还按体积估算——扫描件单页可能几 MB，
/// 180 页很容易超过 MinerU 的 200MB 上传上限（老流水线就出现过 155MB 的块）。
/// </summary>
public static class PdfChunker
{
    /// <summary>体积安全余量：只用到上限的 90%。</summary>
    private const double SafetyFactor = 0.9;

    public static int EstimatePagesPerChunk(string path, int totalPages, MineruOptions options)
    {
        if (totalPages <= 0) return 1;

        var sizeMb = new FileInfo(path).Length / 1048576.0;
        var perPageMb = sizeMb / totalPages;
        if (perPageMb <= 0) return options.MaxPagesPerChunk;

        var bySize = (int)(options.MaxUploadMb * SafetyFactor / perPageMb);
        return Math.Max(1, Math.Min(options.MaxPagesPerChunk, bySize));
    }

    /// <summary>生成分块计划；单块时直接复用源文件，不产生副本。</summary>
    public static IReadOnlyList<ChunkInfo> Plan(string path, int totalPages, string chunkDir, MineruOptions options)
    {
        var perChunk = EstimatePagesPerChunk(path, totalPages, options);
        if (perChunk >= totalPages)
            return [new ChunkInfo(path, 0, totalPages)];

        Directory.CreateDirectory(chunkDir);
        var stem = Path.GetFileNameWithoutExtension(path);
        var chunks = new List<ChunkInfo>();

        using var source = PdfSource.Open(path);
        for (var start = 0; start < totalPages; start += perChunk)
        {
            var end = Math.Min(start + perChunk, totalPages);
            var file = Path.Combine(chunkDir, $"{stem}_part{chunks.Count + 1}_p{start + 1}-{end}.pdf");
            source.WriteRange(start, end, file);
            chunks.Add(new ChunkInfo(file, start, end));
        }
        return chunks;
    }
}
