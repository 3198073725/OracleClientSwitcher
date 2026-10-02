using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OracleClientSwitcher;

internal static class AppLog
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OracleClientSwitcher");
    private static readonly string LogDirectory = Path.Combine(DataDirectory, "logs");
    private static readonly string LogFile = Path.Combine(LogDirectory, "activity.jsonl");
    private static readonly object SyncRoot = new();

    public static string FilePath => LogFile;

    public static void Information(string category, string action, string result, string detail = "") =>
        Write(new AppLogEntry { Timestamp = DateTime.Now, Level = AppLogLevel.Information, Category = category, Action = action, Result = result, Detail = detail });

    public static void Warning(string category, string action, string detail) =>
        Write(new AppLogEntry { Timestamp = DateTime.Now, Level = AppLogLevel.Warning, Category = category, Action = action, Result = "警告", Detail = detail });

    public static void Error(string category, string action, Exception exception) =>
        Write(new AppLogEntry { Timestamp = DateTime.Now, Level = AppLogLevel.Error, Category = category, Action = action, Result = exception.GetType().Name, Detail = exception.ToString() });

    public static void Write(AppLogEntry entry)
    {
        try
        {
            entry.Category = Redact(entry.Category);
            entry.Action = Redact(entry.Action);
            entry.Result = Redact(entry.Result);
            entry.Detail = Redact(entry.Detail);
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFile, JsonSerializer.Serialize(entry) + Environment.NewLine, new UTF8Encoding(false));
                RotateIfNeeded();
            }
        }
        catch { }
    }

    public static IReadOnlyList<AppLogEntry> Read(int maximum = 1000)
    {
        lock (SyncRoot)
        {
            try
            {
                if (!File.Exists(LogFile)) return Array.Empty<AppLogEntry>();
                return File.ReadLines(LogFile)
                    .Reverse()
                    .Take(Math.Max(1, maximum))
                    .Select(line =>
                    {
                        try { return JsonSerializer.Deserialize<AppLogEntry>(line); }
                        catch { return null; }
                    })
                    .Where(x => x is not null)
                    .Cast<AppLogEntry>()
                    .ToList();
            }
            catch { return Array.Empty<AppLogEntry>(); }
        }
    }

    public static TnsOperationResult Clear()
    {
        lock (SyncRoot)
        {
            try
            {
                if (File.Exists(LogFile)) File.Delete(LogFile);
                return new TnsOperationResult { Success = true, Message = "日志已清理。" };
            }
            catch (Exception ex) { return new TnsOperationResult { Success = false, Message = "清理日志失败：" + ex.Message }; }
        }
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        string redacted = value;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        if (profile.Length > 0) redacted = redacted.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        string userName = Environment.UserName;
        if (userName.Length > 0)
        {
            redacted = Regex.Replace(redacted, $@"(?i)(C:\\Users\\){Regex.Escape(userName)}(?=\\|\b)", "$1<用户>");
            redacted = redacted.Replace(userName, "<用户>", StringComparison.OrdinalIgnoreCase);
        }
        return redacted;
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(LogFile);
        if (!info.Exists || info.Length < 5 * 1024 * 1024) return;
        string archive = Path.Combine(LogDirectory, $"activity-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        File.Move(LogFile, archive, true);
        foreach (FileInfo old in new DirectoryInfo(LogDirectory).GetFiles("activity-*.jsonl").OrderByDescending(x => x.LastWriteTime).Skip(4))
            try { old.Delete(); } catch { }
    }
}

internal static class DiagnosticReportService
{
    public static TnsOperationResult Create(string destination, IReadOnlyList<OracleClientInfo> clients, OracleEnvironmentStatus environmentStatus)
    {
        try
        {
            string temporary = destination + $".tmp-{Guid.NewGuid():N}";
            try
            {
                using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                {
                    AddText(archive, "summary.txt", BuildSummary(clients, environmentStatus));
                    AddText(archive, "logs.json", JsonSerializer.Serialize(AppLog.Read(2000), new JsonSerializerOptions { WriteIndented = true }));
                    AddText(archive, "scan-settings.json", JsonSerializer.Serialize(ScanConfigurationManager.LoadSettings(), new JsonSerializerOptions { WriteIndented = true }));
                    AddText(archive, "clients.json", JsonSerializer.Serialize(clients.Select(x => new
                    {
                        Name = x.Version,
                        x.Architecture,
                        x.ClientType,
                        x.HasSqlPlus,
                        Path = AppLog.Redact(x.PathDirectory),
                        TnsAdmin = AppLog.Redact(x.TnsAdmin),
                        x.DiscoverySource
                    }), new JsonSerializerOptions { WriteIndented = true }));
                }
                File.Move(temporary, destination, true);
            }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
            AppLog.Information("诊断", "生成报告", "成功", destination);
            return new TnsOperationResult { Success = true, Message = "诊断报告已生成：" + destination };
        }
        catch (Exception ex)
        {
            AppLog.Error("诊断", "生成报告", ex);
            return new TnsOperationResult { Success = false, Message = "生成诊断报告失败：" + ex.Message };
        }
    }

    private static string BuildSummary(IReadOnlyList<OracleClientInfo> clients, OracleEnvironmentStatus status)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        return $"Oracle 客户端切换器诊断报告\r\n" +
               $"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}\r\n" +
               $"程序版本：{assembly.GetName().Version}\r\n" +
               $"系统：{RuntimeInformation.OSDescription}\r\n" +
               $"进程架构：{RuntimeInformation.ProcessArchitecture}\r\n" +
               $"系统架构：{RuntimeInformation.OSArchitecture}\r\n" +
               $".NET：{RuntimeInformation.FrameworkDescription}\r\n" +
               $"客户端数量：{clients.Count}\r\n" +
               $"环境状态：{status.State} / {AppLog.Redact(status.Summary)}\r\n" +
               $"环境详情：{AppLog.Redact(status.Detail)}\r\n" +
               "\r\n说明：报告中的用户目录和用户名已自动脱敏。\r\n";
    }

    private static void AddText(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(true));
        writer.Write(AppLog.Redact(content));
    }
}

internal static class CompatibilityService
{
    public static CompatibilityCheckResult Check()
    {
        var warnings = new List<string>();
        bool supported = OperatingSystem.IsWindows();
        if (!supported) warnings.Add("本程序仅支持 Windows。" );
        if (OperatingSystem.IsWindows() && !OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            supported = false;
            warnings.Add("需要 Windows 10、Windows Server 2016 或更高版本。" );
        }
        if (!Environment.Is64BitOperatingSystem)
            warnings.Add("当前是 32 位 Windows，只能使用 32 位 Oracle 客户端。" );
        if (Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Build < 17763)
            warnings.Add("系统版本较旧，部分 TLS 和界面功能可能不可用。" );
        return new CompatibilityCheckResult
        {
            Supported = supported,
            Summary = supported ? "系统兼容性检查通过" : "当前系统不受支持",
            Warnings = warnings
        };
    }

    public static string? FindNewerLocalVersion()
    {
        try
        {
            Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version();
            string directory = AppContext.BaseDirectory;
            return Directory.GetFiles(directory, "OracleClientSwitcher-*.exe")
                .Select(path => (Path: path, Version: ParseVersion(Path.GetFileNameWithoutExtension(path))))
                .Where(x => x.Version > current)
                .OrderByDescending(x => x.Version)
                .Select(x => x.Path)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static Version ParseVersion(string fileName)
    {
        Match match = Regex.Match(fileName, @"(?<!\d)(\d+\.\d+\.\d+)(?!\d)");
        return match.Success && Version.TryParse(match.Groups[1].Value, out Version? version) ? version : new Version();
    }
}
