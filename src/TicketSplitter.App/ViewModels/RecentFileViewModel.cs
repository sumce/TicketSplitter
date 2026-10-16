namespace TicketSplitter.App.ViewModels;

/// <summary>最近打开的文件（显示文件名，路径只做副标题与提示）。</summary>
public sealed record RecentFileViewModel(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}
