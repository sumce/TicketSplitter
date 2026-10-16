using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketSplitter.App.Services;

/// <summary>
/// 应用设置。未打包的 WinUI 应用拿不到 ApplicationData.Current，
/// 因此落到 %LOCALAPPDATA%\TicketSplitter\settings.json。
/// Token 用 DPAPI（当前用户作用域）加密后落盘，不明文保存。
/// </summary>
/// <summary>用户自定义的样式（持久化到 settings.json）。</summary>
public sealed record StyleRecord(string Name, string Description, TicketSplitter.Core.Pdf.SheetSplitMode Mode);

public sealed class AppSettings
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TicketSplitter");

    private static string SettingsPath => Path.Combine(DataDir, "settings.json");

    [JsonPropertyName("protectedToken")] public string? ProtectedToken { get; set; }
    [JsonPropertyName("lastSourcePath")] public string? LastSourcePath { get; set; }
    [JsonPropertyName("lastOutputDir")] public string? LastOutputDir { get; set; }
    [JsonPropertyName("rememberToken")] public bool RememberToken { get; set; }
    [JsonPropertyName("reuseExistingParse")] public bool ReuseExistingParse { get; set; }

    /// <summary>用户自己添加的样式。</summary>
    [JsonPropertyName("customStyles")] public List<StyleRecord> CustomStyles { get; set; } = [];

    /// <summary>最近提交过的文件。</summary>
    [JsonPropertyName("recentFiles")] public List<string> RecentFiles { get; set; } = [];

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath),
                    new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 设置文件损坏/不可读时不要阻塞启动
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public string? ReadToken()
    {
        if (string.IsNullOrEmpty(ProtectedToken)) return null;
        try
        {
            var bytes = Convert.FromBase64String(ProtectedToken);
            var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;   // 换了机器/用户就解不开，当作没保存
        }
    }

    public void WriteToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ProtectedToken = null;
            return;
        }
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            ProtectedToken = Convert.ToBase64String(bytes);
        }
        catch (CryptographicException)
        {
            ProtectedToken = null;
        }
    }
}
