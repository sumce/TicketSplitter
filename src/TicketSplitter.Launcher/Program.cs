using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace TicketSplitter.Launcher;

/// <summary>
/// 单文件自解压启动器。
///
/// 为什么需要它：Windows App SDK 不支持 .NET 的单文件部署
/// （PublishSingleFile 打出来的 exe 会在启动时抛托管异常直接退出）。
/// 所以这里把发布好的整个程序目录作为 zip 内嵌进来，首次运行时释放到
/// %LOCALAPPDATA%\TicketSplitter\runtime\&lt;版本&gt;\，再从那里启动真正的程序。
/// 之后每次启动都跳过释放，直接拉起。
/// </summary>
internal static class Program
{
    private const string PayloadName = "payload.zip";
    private const string ReadyMarker = ".ready";

    [STAThread]
    private static int Main()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            var targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TicketSplitter", "runtime", version);

            var exePath = Path.Combine(targetDir, "TicketSplitter.exe");
            var readyPath = Path.Combine(targetDir, ReadyMarker);

            if (!File.Exists(exePath) || !File.Exists(readyPath))
            {
                using var splash = new SplashForm();
                splash.Show();
                splash.Refresh();
                Extract(targetDir);
                splash.Close();
            }

            if (!File.Exists(exePath))
                return Fail($"释放后找不到主程序：{exePath}");

            Process.Start(new ProcessStartInfo(exePath)
            {
                WorkingDirectory = targetDir,
                UseShellExecute = true,
            });
            return 0;
        }
        catch (Exception ex)
        {
            return Fail("启动失败：\n\n" + ex.Message);
        }
    }

    /// <summary>先释放到临时目录再整体改名，避免中途失败留下半成品。</summary>
    private static void Extract(string targetDir)
    {
        var staging = targetDir + ".tmp";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        using var stream = OpenPayload()
            ?? throw new InvalidOperationException("内置负载缺失，这个 exe 不完整。");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(staging, overwriteFiles: true);

        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
        Directory.Move(staging, targetDir);

        File.WriteAllText(Path.Combine(targetDir, ReadyMarker), DateTime.Now.ToString("O"));
    }

    /// <summary>
    /// 按名字找内嵌负载；找不到就按后缀兜底，免得将来改打包方式时又踩一次命名坑。
    /// </summary>
    private static Stream? OpenPayload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var direct = assembly.GetManifestResourceStream(PayloadName);
        if (direct is not null) return direct;

        var name = Array.Find(assembly.GetManifestResourceNames(),
            n => n.EndsWith(PayloadName, StringComparison.OrdinalIgnoreCase));
        return name is null ? null : assembly.GetManifestResourceStream(name);
    }

    private static int Fail(string message)
    {
        // 同时落一份日志，便于自动化排查（弹窗会阻塞，无法从外部看到原因）
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TicketSplitter");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "launcher.log"),
                $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch (Exception) { }

        MessageBox.Show(message, "作业票切分工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }
}
