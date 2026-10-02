using System.Text.Json;

namespace OracleClientSwitcher;

internal static class ScanConfigurationManager
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OracleClientSwitcher");
    private static readonly string SettingsFile = Path.Combine(DataDirectory, "scan-settings.json");
    private static readonly string CacheFile = Path.Combine(DataDirectory, "scan-cache.json");
    private static readonly object Sync = new();

    public static OracleScanSettings LoadSettings()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(SettingsFile)) return new OracleScanSettings();
                OracleScanSettings settings = JsonSerializer.Deserialize<OracleScanSettings>(File.ReadAllText(SettingsFile)) ?? new OracleScanSettings();
                settings.CacheHours = Math.Clamp(settings.CacheHours, 1, 168);
                settings.SelectedDriveRoots = NormalizeExistingRoots(settings.SelectedDriveRoots);
                settings.CustomDirectories = NormalizePaths(settings.CustomDirectories);
                settings.ExcludedDirectories = NormalizePaths(settings.ExcludedDirectories);
                return settings;
            }
            catch { return new OracleScanSettings(); }
        }
    }

    public static TnsOperationResult SaveSettings(OracleScanSettings settings)
    {
        try
        {
            var normalized = new OracleScanSettings
            {
                Mode = settings.Mode,
                SelectedDriveRoots = NormalizeExistingRoots(settings.SelectedDriveRoots),
                CustomDirectories = NormalizePaths(settings.CustomDirectories),
                ExcludedDirectories = NormalizePaths(settings.ExcludedDirectories),
                UseCache = settings.UseCache,
                CacheHours = Math.Clamp(settings.CacheHours, 1, 168)
            };
            lock (Sync)
            {
                Directory.CreateDirectory(DataDirectory);
                WriteAtomic(SettingsFile, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
                TryDelete(CacheFile);
            }
            return new TnsOperationResult { Success = true, Message = "扫描设置已保存；旧缓存已清除，下次扫描会使用新范围。" };
        }
        catch (Exception ex)
        {
            return new TnsOperationResult { Success = false, Message = "保存扫描设置失败：" + ex.Message };
        }
    }

    public static IReadOnlyList<OracleClientInfo>? TryLoadFreshCache(OracleScanSettings settings)
    {
        if (!settings.UseCache) return null;
        lock (Sync)
        {
            try
            {
                if (!File.Exists(CacheFile)) return null;
                OracleScanCache? cache = JsonSerializer.Deserialize<OracleScanCache>(File.ReadAllText(CacheFile));
                if (cache is null || DateTime.Now - cache.CreatedAt > TimeSpan.FromHours(settings.CacheHours)) return null;
                var valid = new List<OracleClientInfo>();
                foreach (OracleClientInfo cached in cache.Clients)
                {
                    OracleClientInfo? refreshed = OracleScanner.CreateClientInfo(cached.PathDirectory, cached.DiscoverySource + " · 缓存");
                    if (refreshed is not null) valid.Add(refreshed);
                }
                return valid.Count == 0 || valid.Count != cache.Clients.Count ? null : valid;
            }
            catch { return null; }
        }
    }

    public static void SaveCache(IEnumerable<OracleClientInfo> clients)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                var cache = new OracleScanCache { CreatedAt = DateTime.Now, Clients = clients.ToList() };
                WriteAtomic(CacheFile, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    public static void ClearCache()
    {
        lock (Sync) TryDelete(CacheFile);
    }

    public static IReadOnlyList<string> GetDefaultDriveRoots()
    {
        string systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\') + "\\";
        string? appRoot = Path.GetPathRoot(AppContext.BaseDirectory);
        return DriveInfo.GetDrives()
            .Where(x => x.IsReady && (x.DriveType == DriveType.Fixed || x.DriveType == DriveType.Removable))
            .Select(x => x.RootDirectory.FullName)
            .Where(x => !string.Equals(x, systemDrive, StringComparison.OrdinalIgnoreCase) || string.Equals(x, appRoot, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<string> NormalizeExistingRoots(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(NormalizePath)
            .Where(x => x is not null && Path.GetPathRoot(x) == x)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizePaths(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(NormalizePath)
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
            string? root = Path.GetPathRoot(full);
            return root is not null && string.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                ? root
                : full.TrimEnd('\\', '/');
        }
        catch { return null; }
    }

    private static void WriteAtomic(string target, string content)
    {
        string temporary = target + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, target, true);
        }
        finally { TryDelete(temporary); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
