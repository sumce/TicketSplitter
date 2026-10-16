using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketSplitter.Core.Mineru;

/// <summary>
/// 断点续跑状态。老流水线失败后只能手写 resume 脚本（还因此泄露了 Token），
/// 这里把「哪些块已经解析完」持久化到 mineru/parse_state.json。
/// 只记录文件名与页范围，不记录任何凭据。
/// </summary>
public sealed class ParseStateStore
{
    public sealed record Entry(string FileName, int StartPage, int EndPageExclusive, string ResultDir);

    public sealed class State
    {
        [JsonPropertyName("completed")] public List<Entry> Completed { get; set; } = [];
    }

    private readonly State _state;

    private ParseStateStore(State state) => _state = state;

    public static ParseStateStore Load(string path)
    {
        if (!File.Exists(path)) return new ParseStateStore(new State());
        try
        {
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State();
            return new ParseStateStore(state);
        }
        catch (JsonException)
        {
            // 状态文件损坏时不要阻塞用户，重新开始即可
            return new ParseStateStore(new State());
        }
    }

    public bool IsCompleted(ChunkInfo chunk) =>
        _state.Completed.Any(e => e.FileName == chunk.FileName
                                  && e.StartPage == chunk.StartPage
                                  && e.EndPageExclusive == chunk.EndPageExclusive);

    public void MarkCompleted(ChunkInfo chunk, string resultDir)
    {
        _state.Completed.RemoveAll(e => e.FileName == chunk.FileName);
        _state.Completed.Add(new Entry(chunk.FileName, chunk.StartPage, chunk.EndPageExclusive, resultDir));
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
    }
}
