using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;

namespace TicketSplitter.Core.Mineru;

/// <summary>
/// MinerU v4 精准解析客户端：申请上传链接 -&gt; PUT 上传 -&gt; 轮询 -&gt; 下载 zip -&gt; 合并 content_list。
/// 与老流水线相比的关键差异：**带重试、带断点续跑**。
/// </summary>
public sealed class MineruClient : IDisposable
{
    private const string StateFileName = "parse_state.json";

    private readonly MineruOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public MineruClient(MineruOptions options, HttpMessageHandler? handler = null)
    {
        _options = options;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _ownsHttp = true;
        _http.Timeout = Timeout.InfiniteTimeSpan;   // 由每个请求自己控制超时
    }

    /// <summary>
    /// 解析整份 PDF。分块彼此独立，按 MaxParallelChunks **并行**上传/轮询/下载；
    /// 结果始终按分块顺序合并，保证每块的 page_idx 偏移正确，
    /// 因此切点即使落在一张票中间也不会影响后续切票。
    /// </summary>
    public async Task<ParseResult> ParseAsync(
        string pdfPath,
        string mineruDir,
        IReadOnlyList<ChunkBoundary> boundaries,
        IProgress<ParseProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
            throw new MineruException("缺少 MinerU Token（请在设置里填写，或设置环境变量 MINERU_TOKEN）。");
        if (boundaries.Count == 0)
            throw new MineruException("分块计划为空。");

        Directory.CreateDirectory(mineruDir);
        var statePath = Path.Combine(mineruDir, StateFileName);
        var state = ParseStateStore.Load(statePath);

        var totalPages = boundaries[^1].EndPageExclusive;
        var parallel = Math.Clamp(_options.MaxParallelChunks, 1, 8);

        progress?.Report(new ParseProgress(ParseStage.Planning, 0, boundaries.Count,
            boundaries.Count == 1
                ? $"共 {totalPages} 页，单块解析"
                : $"共 {totalPages} 页，分 {boundaries.Count} 块并行解析（并发 {parallel}）"));

        var chunks = await Task.Run(
            () => PdfChunker.Materialize(pdfPath, boundaries, Path.Combine(mineruDir, "chunks")), ct)
            .ConfigureAwait(false);

        var outcomes = new ChunkOutcome?[chunks.Count];
        using var gate = new SemaphoreSlim(parallel, parallel);
        var stateLock = new object();

        var tasks = chunks.Select(async (chunk, i) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var resultDir = Path.Combine(mineruDir, Path.GetFileNameWithoutExtension(chunk.FileName));

                // ---- 断点续跑：这一块已有结果就直接复用 ----
                if (state.IsCompleted(chunk) && TryFindContentList(resultDir) is not null)
                {
                    progress?.Report(new ParseProgress(ParseStage.Parsing, i + 1, chunks.Count,
                        $"块 {i + 1}/{chunks.Count} 已有解析结果，跳过（断点续跑）", 100));
                    outcomes[i] = new ChunkOutcome(chunk, resultDir, ReusedFromCache: true, Attempts: 0);
                    return;
                }

                outcomes[i] = await ParseChunkWithRetryAsync(chunk, resultDir, i, chunks.Count, progress, ct)
                    .ConfigureAwait(false);

                lock (stateLock)
                {
                    state.MarkCompleted(chunk, resultDir);
                    state.Save(statePath);
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var ordered = new List<ChunkOutcome>(chunks.Count);
        foreach (var o in outcomes)
        {
            if (o is null) throw new MineruException("有分块未产出结果。");
            ordered.Add(o);
        }

        progress?.Report(new ParseProgress(ParseStage.Merging, chunks.Count, chunks.Count, "正在合并解析结果…"));
        var merged = MergeContentLists(mineruDir, ordered);
        progress?.Report(new ParseProgress(ParseStage.Done, chunks.Count, chunks.Count, "解析完成", 100));

        return new ParseResult(merged, ordered, totalPages);
    }

    private async Task<ChunkOutcome> ParseChunkWithRetryAsync(
        ChunkInfo chunk, string resultDir, int index, int count,
        IProgress<ParseProgress>? progress, CancellationToken ct)
    {
        MineruException? last = null;

        for (var attempt = 1; attempt <= Math.Max(1, _options.MaxRetries); attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var local = progress;
                await ParseChunkOnceAsync(chunk, resultDir, index, count, local, ct).ConfigureAwait(false);
                return new ChunkOutcome(chunk, resultDir, ReusedFromCache: false, Attempts: attempt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (MineruTransientException ex)
            {
                last = ex;
                if (attempt == Math.Max(1, _options.MaxRetries))
                    break;

                var delay = TimeSpan.FromTicks(_options.RetryBaseDelay.Ticks * (long)Math.Pow(2, attempt - 1));
                progress?.Report(new ParseProgress(ParseStage.Failed, index + 1, count,
                    $"块 {index + 1}/{count} 第 {attempt} 次失败：{ex.Message}；{delay.TotalSeconds:F0}s 后重试",
                    Attempt: attempt + 1));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        throw new MineruException(
            $"块 {index + 1}/{count}（{chunk.FileName}，第 {chunk.StartPage + 1}-{chunk.EndPageExclusive} 页）" +
            $"重试 {_options.MaxRetries} 次后仍失败：{last?.Message}", last!);
    }

    private async Task ParseChunkOnceAsync(
        ChunkInfo chunk, string resultDir, int index, int count,
        IProgress<ParseProgress>? progress, CancellationToken ct)
    {
        var sizeMb = new FileInfo(chunk.Path).Length / 1048576.0;
        if (sizeMb > _options.MaxUploadMb)
            throw new MineruException($"块 {chunk.FileName} 为 {sizeMb:F1}MB，超过 {_options.MaxUploadMb:F0}MB 上传上限（这是配置问题，重试无用）。");

        progress?.Report(new ParseProgress(ParseStage.Uploading, index + 1, count,
            $"块 {index + 1}/{count}：申请上传链接（{sizeMb:F1}MB，第 {chunk.StartPage + 1}-{chunk.EndPageExclusive} 页）"));

        var (batchId, uploadUrl) = await RequestUploadUrlAsync(chunk.FileName, ct).ConfigureAwait(false);

        progress?.Report(new ParseProgress(ParseStage.Uploading, index + 1, count,
            $"块 {index + 1}/{count}：上传中…", Percent: 5));

        await PutFileAsync(uploadUrl, chunk.Path, ct).ConfigureAwait(false);

        progress?.Report(new ParseProgress(ParseStage.Queued, index + 1, count,
            $"块 {index + 1}/{count}：已提交，等待解析", Percent: 10));

        var item = await PollAsync(batchId, index, count, progress, ct).ConfigureAwait(false);

        var zipUrl = item["full_zip_url"]?.GetValue<string>()
            ?? throw new MineruTransientException("解析完成但没有返回 full_zip_url");

        progress?.Report(new ParseProgress(ParseStage.Downloading, index + 1, count,
            $"块 {index + 1}/{count}：下载解析结果", Percent: 95));

        await DownloadAndExtractAsync(zipUrl, resultDir, ct).ConfigureAwait(false);

        if (TryFindContentList(resultDir) is null)
            throw new MineruTransientException($"块 {chunk.FileName} 的结果包里没有 content_list.json");
    }

    private async Task<(string BatchId, string UploadUrl)> RequestUploadUrlAsync(string fileName, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["files"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = fileName,
                    ["data_id"] = fileName,
                    ["is_ocr"] = _options.IsOcr,
                    ["language"] = _options.Language,
                    ["enable_table"] = _options.EnableTable,
                    ["enable_formula"] = _options.EnableFormula,
                },
            },
            ["model_version"] = _options.ModelVersion,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/file-urls/batch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        var json = await SendJsonAsync(request, ct).ConfigureAwait(false);
        ThrowIfFailed(json, "申请上传链接失败");

        var data = json["data"]!;
        var batchId = data["batch_id"]?.GetValue<string>()
            ?? throw new MineruTransientException("返回里没有 batch_id");
        var uploadUrl = data["file_urls"]?.AsArray().FirstOrDefault()?.GetValue<string>()
            ?? throw new MineruTransientException("返回里没有上传链接");

        return (batchId, uploadUrl);
    }

    private async Task PutFileAsync(string url, string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StreamContent(stream) };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.UploadTimeout);

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
                throw new MineruTransientException($"上传失败：HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            throw new MineruTransientException($"上传失败：{ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MineruTransientException("上传超时", ex);
        }
    }

    private async Task<JsonNode> PollAsync(
        string batchId, int index, int count, IProgress<ParseProgress>? progress, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _options.PollTimeout;
        var lastState = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_options.BaseUrl}/extract-results/batch/{batchId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);

            var json = await SendJsonAsync(request, ct).ConfigureAwait(false);
            ThrowIfFailed(json, "查询解析状态失败");

            var item = json["data"]?["extract_result"]?.AsArray().FirstOrDefault()
                ?? throw new MineruTransientException("返回里没有 extract_result");

            var state = item["state"]?.GetValue<string>() ?? "unknown";

            if (state != lastState)
            {
                lastState = state;
                var text = Describe(state, item, index, count);
                var stage = state switch
                {
                    "running" or "converting" => ParseStage.Parsing,
                    "done" => ParseStage.Parsing,
                    _ => ParseStage.Queued,
                };
                progress?.Report(new ParseProgress(stage, index + 1, count, text, Percent: 50));
            }

            switch (state)
            {
                case "done":
                    return item;
                case "failed":
                    throw new MineruTransientException($"解析失败：{item["err_msg"]?.GetValue<string>() ?? "未知错误"}");
            }

            await Task.Delay(_options.PollInterval, ct).ConfigureAwait(false);
        }

        throw new MineruTransientException($"轮询超时（{_options.PollTimeout.TotalMinutes:F0} 分钟），batch_id={batchId}");
    }

    private static string Describe(string state, JsonNode item, int index, int count)
    {
        var label = state switch
        {
            "waiting-file" => "等待文件",
            "pending" => "排队中",
            "running" => "解析中",
            "converting" => "格式转换中",
            "done" => "完成",
            "failed" => "失败",
            _ => state,
        };

        var progress = item["extract_progress"];
        if (state is "running" or "converting" && progress is not null)
        {
            var done = progress["extracted_pages"]?.GetValue<int>() ?? 0;
            var total = progress["total_pages"]?.GetValue<int>() ?? 0;
            if (total > 0) label += $" {done}/{total} 页";
        }

        return $"块 {index + 1}/{count}：{label}";
    }

    private async Task DownloadAndExtractAsync(string zipUrl, string resultDir, CancellationToken ct)
    {
        Directory.CreateDirectory(resultDir);
        var zipPath = Path.Combine(resultDir, "result.zip");
        try
        {
            using var response = await _http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var file = File.Create(zipPath))
                await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);

            ZipFile.ExtractToDirectory(zipPath, resultDir, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    /// <summary>把 code != 0 的响应分成「鉴权失败」和「可重试」两类。</summary>
    private static void ThrowIfFailed(JsonNode json, string prefix)
    {
        var code = CodeOf(json);
        if (code.Length == 0 || code == "0") return;

        var msg = MessageOf(json);
        if (LooksLikeAuthFailure(code, msg))
            throw new MineruAuthException($"Token 无效或已失效：[{code}] {msg}");
        throw new MineruTransientException($"{prefix}：[{code}] {msg}");
    }

    /// <summary>code 可能是数字也可能是字符串，统一取成字符串。</summary>
    private static string CodeOf(JsonNode json)
    {
        var node = json["code"];
        if (node is null) return string.Empty;
        if (node is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i.ToString();
            if (v.TryGetValue<long>(out var l)) return l.ToString();
            if (v.TryGetValue<string>(out var s)) return s ?? string.Empty;
        }
        return node.ToJsonString().Trim('"');
    }

    private static string MessageOf(JsonNode json) => json["msg"] switch
    {
        null => string.Empty,
        JsonValue v when v.TryGetValue<string>(out var s) => s ?? string.Empty,
        var other => other.ToJsonString().Trim('"'),
    };

    private static bool LooksLikeAuthFailure(string code, string message) =>
        code is "A0202" or "A0211" or "A0212" or "A0213" or "-401" or "-403"
        || message.Contains("token", StringComparison.OrdinalIgnoreCase)
        || message.Contains("认证", StringComparison.Ordinal)
        || message.Contains("授权", StringComparison.Ordinal)
        || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);

    private async Task<JsonNode> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if ((int)response.StatusCode >= 500)
                throw new MineruTransientException($"服务端错误 HTTP {(int)response.StatusCode}");
            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
                throw new MineruAuthException($"Token 无效或已失效（HTTP {(int)response.StatusCode}），请重新填写。");
            if (!response.IsSuccessStatusCode)
                throw new MineruException($"请求失败：HTTP {(int)response.StatusCode}");

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonNode.Parse(text) ?? throw new MineruTransientException("服务端返回了空响应");
        }
        catch (HttpRequestException ex)
        {
            throw new MineruTransientException($"网络请求失败：{ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new MineruTransientException($"服务端返回了无法解析的内容：{ex.Message}", ex);
        }
    }

    /// <summary>在结果目录里找 content_list.json（跳过我们自己的 merged 文件）。</summary>
    public static string? TryFindContentList(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*_content_list.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("merged", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>把各块的 content_list 按块顺序拼接，page_idx 累加前面块的实际页数。</summary>
    public static string MergeContentLists(string mineruDir, IReadOnlyList<ChunkOutcome> outcomes)
    {
        var mergedPath = Path.Combine(mineruDir, "merged_content_list.json");
        var all = new JsonArray();
        var offset = 0;

        foreach (var outcome in outcomes)
        {
            var file = TryFindContentList(outcome.ResultDir)
                ?? throw new MineruException($"块 {outcome.Chunk.FileName} 缺少 content_list.json");

            var items = JsonNode.Parse(File.ReadAllText(file))?.AsArray()
                ?? throw new MineruException($"块 {outcome.Chunk.FileName} 的 content_list.json 不是数组");

            foreach (var item in items)
            {
                if (item is null) continue;
                var pageIdx = item["page_idx"]?.GetValue<int>() ?? 0;
                item["page_idx"] = pageIdx + offset;
                all.Add(item.DeepClone());
            }

            offset += outcome.Chunk.PageCount;
        }

        File.WriteAllText(mergedPath, all.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), Encoding.UTF8);
        return mergedPath;
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
