using Microsoft.Win32;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OracleClientSwitcher;

internal static class OracleScanner
{
    private static readonly HashSet<string> SkippedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN", "System Volume Information", "Windows", "WinSxS",
        "node_modules", ".git", ".svn", "Recovery"
    };

    public static async Task<List<OracleClientInfo>> ScanAutomaticallyAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            OracleScanSettings settings = ScanConfigurationManager.LoadSettings();
            if (!forceRefresh)
            {
                IReadOnlyList<OracleClientInfo>? cached = ScanConfigurationManager.TryLoadFreshCache(settings);
                if (cached is not null)
                {
                    progress?.Report($"已载入扫描缓存（{cached.Count} 个客户端）");
                    return cached.OrderByDescending(x => ParseVersion(x.Version)).ThenBy(x => x.Architecture).ToList();
                }
            }

            var candidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            AddPathCandidates(candidates);
            AddRegistryCandidates(candidates);

            foreach (string customDirectory in settings.CustomDirectories.Where(Directory.Exists))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"正在扫描固定目录 {customDirectory} …");
                ScanTree(customDirectory, 8, candidates, "固定目录", settings.ExcludedDirectories, cancellationToken);
            }

            string? appRoot = Path.GetPathRoot(AppContext.BaseDirectory);
            IReadOnlyList<string> selectedRoots = settings.SelectedDriveRoots.Count > 0
                ? settings.SelectedDriveRoots
                : ScanConfigurationManager.GetDefaultDriveRoots();
            if (settings.Mode != OracleScanMode.Quick)
            {
                foreach (string root in selectedRoots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Directory.Exists(root)) continue;
                    bool isAppDrive = string.Equals(Path.GetPathRoot(root), appRoot, StringComparison.OrdinalIgnoreCase);
                    int depth = settings.Mode == OracleScanMode.Full ? 8 : isAppDrive ? 4 : 3;
                    progress?.Report($"正在扫描 {root}（{ModeName(settings.Mode)}）…");
                    ScanTree(root, depth, candidates, $"磁盘 {Path.GetPathRoot(root)}", settings.ExcludedDirectories, cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            List<OracleClientInfo> result = candidates
                .Where(x => IsOracleBinaryDirectory(x.Key))
                .Select(x => CreateClientInfo(x.Key, string.Join("、", x.Value.OrderBy(y => y))))
                .Where(x => x is not null)
                .Cast<OracleClientInfo>()
                .GroupBy(x => Normalize(x.PathDirectory), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(x => ParseVersion(x.Version))
                .ThenBy(x => x.Architecture)
                .ToList();
            if (settings.UseCache) ScanConfigurationManager.SaveCache(result);
            return result;
        }, cancellationToken);
    }

    public static async Task<List<OracleClientInfo>> ScanDirectoryAsync(
        string root,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var candidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (IsOracleBinaryDirectory(root)) AddCandidate(candidates, root, "手动目录");
            progress?.Report($"正在扫描 {root} …");
            ScanTree(root, 8, candidates, "手动目录", Array.Empty<string>(), cancellationToken);
            return candidates.Where(x => IsOracleBinaryDirectory(x.Key))
                .Select(x => CreateClientInfo(x.Key, string.Join("、", x.Value))).Where(x => x is not null).Cast<OracleClientInfo>()
                .OrderByDescending(x => ParseVersion(x.Version)).ToList();
        }, cancellationToken);
    }

    public static OracleClientInfo? CreateClientInfo(string binaryDirectory, string discoverySource = "指定目录")
    {
        try
        {
            string pathDir = Path.GetFullPath(binaryDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string home = string.Equals(Path.GetFileName(pathDir), "bin", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(pathDir)?.FullName ?? pathDir
                : pathDir;

            // 扫描阶段绝不运行客户端自带的 EXE。旧版 Oracle 组件中可能包含
            // 与 64 位 Windows 不兼容的安装/检测程序，执行它们会触发 16 位程序弹窗。
            // OCI DLL 的版本资源足以提供精确版本，PE 头则用于判断位数。
            var versionInfo = FileVersionInfo.GetVersionInfo(Path.Combine(pathDir, "oci.dll"));
            string versionText = versionInfo.FileVersion ?? versionInfo.ProductVersion ?? "未知";
            Match versionMatch = Regex.Match(versionText, @"\d+(?:\.\d+){1,4}");
            string version = versionMatch.Success ? versionMatch.Value : "未知";

            string architecture = ReadPeArchitecture(Path.Combine(pathDir, "oci.dll"));
            bool isInstant = Directory.EnumerateFiles(pathDir, "BASIC*_README*", SearchOption.TopDirectoryOnly).Any()
                || Path.GetFileName(pathDir).Contains("instantclient", StringComparison.OrdinalIgnoreCase);
            bool isLite = Directory.EnumerateFiles(pathDir, "BASIC_LITE*", SearchOption.TopDirectoryOnly).Any();
            string tnsAdmin = FindTnsAdmin(pathDir, home);

            return new OracleClientInfo
            {
                PathDirectory = pathDir,
                OracleHome = home,
                Version = version,
                Architecture = architecture,
                ClientType = isInstant ? (isLite ? "Instant Client (Lite)" : "Instant Client") : "完整客户端",
                TnsAdmin = tnsAdmin,
                HasSqlPlus = File.Exists(Path.Combine(pathDir, "sqlplus.exe")),
                IsLite = isLite,
                DiscoverySource = discoverySource
            };
        }
        catch
        {
            return null;
        }
    }

    public static bool IsOracleBinaryDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;
        return File.Exists(Path.Combine(directory, "oci.dll")) &&
               (File.Exists(Path.Combine(directory, "genezi.exe")) ||
                File.Exists(Path.Combine(directory, "sqlplus.exe")) ||
                Directory.EnumerateFiles(directory, "oraoc*.dll", SearchOption.TopDirectoryOnly).Any());
    }

    public static bool LooksLikeOraclePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (IsOracleBinaryDirectory(expanded)) return true;
        string leaf = Path.GetFileName(expanded.TrimEnd('\\', '/'));
        return Regex.IsMatch(leaf, @"^(instantclient|oracleclient)[_\-]?", RegexOptions.IgnoreCase);
    }

    public static string Normalize(string path)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))).TrimEnd('\\', '/'); }
        catch { return path.Trim().Trim('"').TrimEnd('\\', '/'); }
    }

    private static void AddPathCandidates(Dictionary<string, HashSet<string>> candidates)
    {
        foreach (EnvironmentVariableTarget target in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            string? value = Environment.GetEnvironmentVariable("Path", target);
            if (string.IsNullOrWhiteSpace(value)) continue;
            foreach (string entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string expanded = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
                if (IsOracleBinaryDirectory(expanded)) AddCandidate(candidates, expanded, target == EnvironmentVariableTarget.User ? "用户 PATH" : "系统 PATH");
            }
        }
    }

    private static void AddRegistryCandidates(Dictionary<string, HashSet<string>> candidates)
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using RegistryKey? oracle = baseKey.OpenSubKey(@"SOFTWARE\ORACLE");
                    if (oracle is null) continue;
                    AddHome(oracle.GetValue("ORACLE_HOME") as string, candidates, "注册表");
                    foreach (string subName in oracle.GetSubKeyNames())
                    {
                        using RegistryKey? sub = oracle.OpenSubKey(subName);
                        AddHome(sub?.GetValue("ORACLE_HOME") as string, candidates, "注册表");
                    }
                }
                catch { }
            }
        }
    }

    private static void AddHome(string? home, Dictionary<string, HashSet<string>> candidates, string source)
    {
        if (string.IsNullOrWhiteSpace(home)) return;
        if (IsOracleBinaryDirectory(home)) AddCandidate(candidates, home, source);
        string bin = Path.Combine(home, "bin");
        if (IsOracleBinaryDirectory(bin)) AddCandidate(candidates, bin, source);
    }

    private static void ScanTree(
        string root,
        int maxDepth,
        Dictionary<string, HashSet<string>> candidates,
        string source,
        IReadOnlyList<string> exclusions,
        CancellationToken token)
    {
        if (!Directory.Exists(root) || IsExcluded(root, exclusions)) return;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            if (current.Depth > maxDepth) continue;
            if (IsOracleBinaryDirectory(current.Path))
            {
                AddCandidate(candidates, current.Path, source);
                continue;
            }
            if (current.Depth == maxDepth) continue;
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(current.Path))
                {
                    string name = Path.GetFileName(directory);
                    if (SkippedNames.Contains(name) || name.StartsWith('.') || IsExcluded(directory, exclusions)) continue;
                    try
                    {
                        var attributes = File.GetAttributes(directory);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }
                    queue.Enqueue((directory, current.Depth + 1));
                }
            }
            catch { }
        }
    }

    private static void AddCandidate(Dictionary<string, HashSet<string>> candidates, string path, string source)
    {
        string normalized = Normalize(path);
        if (!candidates.TryGetValue(normalized, out HashSet<string>? sources))
        {
            sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            candidates[normalized] = sources;
        }
        sources.Add(source);
    }

    private static bool IsExcluded(string path, IReadOnlyList<string> exclusions)
    {
        string normalized = Normalize(path);
        return exclusions.Any(exclusion =>
        {
            string excluded = Normalize(exclusion);
            return string.Equals(normalized, excluded, StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(excluded.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string ModeName(OracleScanMode mode) => mode switch
    {
        OracleScanMode.Quick => "快速扫描",
        OracleScanMode.Full => "完整扫描",
        _ => "标准扫描"
    };

    public static string ReadPeArchitecture(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var reader = new BinaryReader(stream);
            stream.Position = 0x3c;
            int peOffset = reader.ReadInt32();
            stream.Position = peOffset + 4;
            return reader.ReadUInt16() switch
            {
                0x014c => "32 位",
                0x8664 => "64 位",
                0xaa64 => "ARM64",
                _ => "未知"
            };
        }
        catch { return "未知"; }
    }

    private static string FindTnsAdmin(string pathDir, string home)
    {
        string[] options =
        {
            Path.Combine(pathDir, "network", "admin"),
            Path.Combine(home, "network", "admin"),
            pathDir
        };
        return options.FirstOrDefault(x => File.Exists(Path.Combine(x, "tnsnames.ora")) || File.Exists(Path.Combine(x, "sqlnet.ora")))
            ?? Path.Combine(pathDir, "network", "admin");
    }

    private static Version ParseVersion(string value)
    {
        // System.Version 最多接受四段；Oracle 常见版本号为 19.30.0.0.0。
        string normalized = string.Join('.', value.Split('.').Take(4));
        return Version.TryParse(normalized, out Version? result) ? result : new Version(0, 0);
    }
}
