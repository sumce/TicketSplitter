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
