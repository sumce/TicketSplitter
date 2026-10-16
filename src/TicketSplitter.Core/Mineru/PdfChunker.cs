using TicketSplitter.Core.Pdf;

namespace TicketSplitter.Core.Mineru;

/// <summary>把源 PDF 按切点落成实际的分块文件。</summary>
public static class PdfChunker
{
    /// <summary>
    /// 按给定切点生成分块文件；只有一块时直接复用源文件，不产生副本。
    /// 体积与页数的合法性由 ChunkPlanner.Validate 负责，这里不再偷偷改切点。
    /// </summary>
    public static IReadOnlyList<ChunkInfo> Materialize(
        string path, IReadOnlyList<ChunkBoundary> boundaries, string chunkDir)
    {
        if (boundaries.Count == 0)
            throw new ArgumentException("分块计划为空", nameof(boundaries));

        if (boundaries.Count == 1)
            return [new ChunkInfo(path, 0, boundaries[0].EndPageExclusive)];

        Directory.CreateDirectory(chunkDir);
        var stem = Path.GetFileNameWithoutExtension(path);
        var chunks = new List<ChunkInfo>(boundaries.Count);

        using var source = PdfSource.Open(path);
        for (var i = 0; i < boundaries.Count; i++)
        {
            var b = boundaries[i];
            var file = Path.Combine(chunkDir, $"{stem}_part{i + 1}_p{b.StartPage1Based}-{b.EndPageExclusive}.pdf");
            source.WriteRange(b.StartPage, b.EndPageExclusive, file);
            chunks.Add(new ChunkInfo(file, b.StartPage, b.EndPageExclusive));
        }
        return chunks;
    }
}
