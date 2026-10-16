using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TicketSplitter.App.Services;

/// <summary>未打包 WinUI 应用的选取器必须先用窗口句柄初始化，否则直接抛异常。</summary>
public static class PickerHelper
{
    public static async Task<string?> PickPdfAsync(IntPtr hwnd, string? startDir = null)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".pdf");
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickFolderAsync(IntPtr hwnd)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public static void RevealInExplorer(string path)
    {
        if (Directory.Exists(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else if (File.Exists(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
