namespace TicketSplitter.Core.Mineru;

/// <summary>一个解析分块的页范围（0 基，EndPageExclusive 为开区间上界）。</summary>
public sealed record ChunkBoundary(int StartPage, int EndPageExclusive)
{
    public int PageCount => EndPageExclusive - StartPage;
    public int StartPage1Based => StartPage + 1;
    public string Label => $"第 {StartPage1Based}-{EndPageExclusive} 页";
}

/// <summary>
/// 分块计划。切点由用户掌控——默认按云端单次页数上限均分，
/// 用户可以改，因为业务上更希望切点落在一批票的边界上。
/// </summary>
public static class ChunkPlanner
{
    /// <summary>按上限均分，返回每块的**结束页（1 基，含）**。</summary>
    public static List<int> DefaultEnds(int totalPages, int maxPagesPerChunk)
    {
        if (totalPages <= 0) return [];
        var max = Math.Max(1, maxPagesPerChunk);

        var ends = new List<int>();
        for (var e = max; e < totalPages; e += max) ends.Add(e);
        ends.Add(totalPages);
        return ends;
    }

    /// <summary>把结束页序列还原成页范围。</summary>
    public static IReadOnlyList<ChunkBoundary> FromEnds(IReadOnlyList<int> ends)
    {
        var list = new List<ChunkBoundary>(ends.Count);
        var start = 0;
        foreach (var end in ends)
        {
            list.Add(new ChunkBoundary(start, end));
            start = end;
        }
        return list;
    }

    /// <summary>
    /// 校验切点。返回的问题会直接显示给用户，不静默修正——
    /// 用户改过的切点被偷偷改回去比报错更糟。
    /// </summary>
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<int> ends, int totalPages, int maxPagesPerChunk, double estimatedMbPerPage, double maxUploadMb)
    {
        var problems = new List<string>();

        if (ends.Count == 0) { problems.Add("至少要有一个分块。"); return problems; }
        if (ends[^1] != totalPages) problems.Add($"最后一块必须覆盖到第 {totalPages} 页。");
        if (ends.Any(e => e <= 0 || e > totalPages)) problems.Add($"切点必须在 1 到 {totalPages} 之间。");

        for (var i = 1; i < ends.Count; i++)
            if (ends[i] <= ends[i - 1]) { problems.Add("切点必须从小到大排列，且不能重复。"); break; }

        var boundaries = FromEnds(ends);
        foreach (var b in boundaries)
        {
            if (b.PageCount <= 0) continue;
            if (b.PageCount > maxPagesPerChunk)
                problems.Add($"{b.Label} 有 {b.PageCount} 页，超过单次上限 {maxPagesPerChunk} 页。");

            if (estimatedMbPerPage > 0)
            {
                var mb = b.PageCount * estimatedMbPerPage;
                if (mb > maxUploadMb)
                    problems.Add($"{b.Label} 约 {mb:F0}MB，超过 {maxUploadMb:F0}MB 上传上限。");
            }
        }

        return problems;
    }

    /// <summary>每页平均体积（MB），用于估算分块体积。</summary>
    public static double EstimateMbPerPage(string path, int totalPages)
    {
        if (totalPages <= 0 || !File.Exists(path)) return 0;
        return new FileInfo(path).Length / 1048576.0 / totalPages;
    }
}
