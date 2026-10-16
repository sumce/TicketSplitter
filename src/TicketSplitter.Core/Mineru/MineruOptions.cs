namespace TicketSplitter.Core.Mineru;

/// <summary>MinerU 精准解析参数。默认值与现有 Python 流水线保持一致。</summary>
public sealed record MineruOptions
{
    /// <summary>API Token。只保存在内存里，绝不写日志。</summary>
    public required string Token { get; init; }

    public string BaseUrl { get; init; } = "https://mineru.net/api/v4";

    /// <summary>vlm 或 pipeline。</summary>
    public string ModelVersion { get; init; } = "vlm";

    public string Language { get; init; } = "ch";
    public bool IsOcr { get; init; } = true;
    public bool EnableTable { get; init; } = true;
    public bool EnableFormula { get; init; } = true;

    /// <summary>单次上传的硬限制（MB）。MinerU 侧为 200MB。</summary>
    public double MaxUploadMb { get; init; } = 200;

    /// <summary>单块最大页数。</summary>
    public int MaxPagesPerChunk { get; init; } = 180;

    /// <summary>单个分块的最大重试次数。这是老流水线最缺的能力。</summary>
    public int MaxRetries { get; init; } = 4;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollTimeout { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan UploadTimeout { get; init; } = TimeSpan.FromMinutes(20);
}

public sealed record ChunkInfo(string Path, int StartPage, int EndPageExclusive)
{
    public int PageCount => EndPageExclusive - StartPage;
    public string FileName => System.IO.Path.GetFileName(Path);
}

public enum ParseStage
{
    Planning,
    Uploading,
    Queued,
    Parsing,
    Downloading,
    Merging,
    Done,
    Failed,
}

public sealed record ParseProgress(
    ParseStage Stage,
    int ChunkIndex,
    int ChunkCount,
    string Message,
    int Percent = 0,
    int Attempt = 1);

public sealed record ChunkOutcome(ChunkInfo Chunk, string ResultDir, bool ReusedFromCache, int Attempts);

public sealed record ParseResult(
    string MergedContentListPath,
    IReadOnlyList<ChunkOutcome> Chunks,
    int TotalPages);
