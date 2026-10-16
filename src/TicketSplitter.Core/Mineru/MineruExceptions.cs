namespace TicketSplitter.Core.Mineru;

public class MineruException : Exception
{
    public MineruException(string message) : base(message) { }
    public MineruException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>解析失败但可以重试（网络抖动、服务端 5xx、parsing failed 等）。</summary>
public sealed class MineruTransientException : MineruException
{
    public MineruTransientException(string message) : base(message) { }
    public MineruTransientException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Token 无效或已失效。重试没有意义，UI 收到它应当把本机保存的 Token 清掉并要求重新填写。
/// </summary>
public sealed class MineruAuthException : MineruException
{
    public MineruAuthException(string message) : base(message) { }
}
