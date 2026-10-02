using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace OracleClientSwitcher;

internal static class EnvironmentManager
{
    private const int HwndBroadcast = 0xffff;
    private const int WmSettingChange = 0x001a;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OracleClientSwitcher");
    private static readonly string BackupDirectory = Path.Combine(DataDirectory, "backups");
    private static readonly string ResultDirectory = Path.Combine(DataDirectory, "operation-results");

    public static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static ApplyResult Apply(OracleClientInfo client, EnvironmentVariableTarget target, bool setOracleHome)
    {
        if (!OracleScanner.IsOracleBinaryDirectory(client.PathDirectory))
            return new ApplyResult { Success = false, Message = "切换失败：所选目录不再是有效的 Oracle 客户端。请重新扫描。" };

        EnvironmentChangePreview preview = BuildChangePreview(client, target, setOracleHome);
        if (!preview.HasChanges)
            return new ApplyResult { Success = true, Message = $"Oracle {client.Version} {client.Architecture} 已经是{TargetName(target)}默认客户端，无需修改。" };

        string? backup = null;
        try
        {
            backup = CreateBackup(target);
            DesiredEnvironment desired = BuildDesiredEnvironment(client, target, setOracleHome);
            WriteEnvironment(target, desired.PathValue, desired.OracleHome, desired.TnsAdmin);
            VerifyEnvironment(target, desired.PathValue, desired.OracleHome, desired.TnsAdmin);
            BroadcastEnvironmentChange();
            return new ApplyResult
            {
                Success = true,
                BackupPath = backup,
                Message = $"已将 Oracle {client.Version} {client.Architecture} 设为{TargetName(target)}默认客户端。\n新启动的程序将使用此配置。"
            };
        }
        catch (Exception ex)
        {
            string rollbackError = string.Empty;
            bool rolledBack = backup is not null && TryRestoreSnapshot(backup, out rollbackError);
            string rollbackText = rolledBack
                ? "\n已自动恢复切换前的环境。"
                : backup is null ? string.Empty : $"\n自动回滚失败：{rollbackError}";
            return new ApplyResult
            {
                Success = false,
                BackupPath = backup,
                RolledBack = rolledBack,
                Message = "切换失败：" + ex.Message + rollbackText
            };
        }
    }

    public static EnvironmentChangePreview BuildChangePreview(
        OracleClientInfo client,
        EnvironmentVariableTarget target,
        bool setOracleHome)
    {
        string currentPath = Environment.GetEnvironmentVariable("Path", target) ?? string.Empty;
        string? currentHome = Environment.GetEnvironmentVariable("ORACLE_HOME", target);
        string? currentTns = Environment.GetEnvironmentVariable("TNS_ADMIN", target);
        DesiredEnvironment desired = BuildDesiredEnvironment(client, target, setOracleHome);
        string scope = TargetName(target);

        var changes = new List<EnvironmentChangeItem>
        {
            new()
            {
                Item = "PATH",
                CurrentValue = DisplayValue(currentPath),
                NewValue = DisplayValue(desired.PathValue),
                Action = desired.RemovedOracleEntries.Count == 0
                    ? "将所选客户端放到 PATH 首位"
                    : $"替换 {desired.RemovedOracleEntries.Count} 个旧 Oracle PATH 项"
            },
            new()
            {
                Item = "TNS_ADMIN",
                CurrentValue = DisplayValue(currentTns),
                NewValue = DisplayValue(desired.TnsAdmin),
                Action = "指向所选客户端的网络配置目录"
            },
            new()
            {
                Item = "ORACLE_HOME",
                CurrentValue = DisplayValue(currentHome),
                NewValue = DisplayValue(desired.OracleHome),
                Action = setOracleHome ? "设置兼容变量" : "清除兼容变量"
            }
        };

        var warnings = new List<string>();
        if (target == EnvironmentVariableTarget.User && GetOracleEntries(EnvironmentVariableTarget.Machine).Count > 0)
            warnings.Add("系统 PATH 中仍有 Oracle 客户端，它通常会优先于当前用户 PATH。建议确认实际生效顺序。");
        if (desired.PathValue.Length > 2047)
            warnings.Add($"{scope} PATH 长度为 {desired.PathValue.Length} 个字符，部分旧程序可能无法读取过长的 PATH。");
        if (client.Architecture == "32 位" && Environment.Is64BitOperatingSystem)
            warnings.Add("已选择 32 位客户端；它只适用于需要 32 位 OCI 的程序。64 位程序无法加载该 OCI。");
        if (!Directory.Exists(desired.TnsAdmin))
            warnings.Add("目标 TNS 目录尚不存在；切换可以完成，但连接配置需要后续创建或导入。");

        return new EnvironmentChangePreview
        {
            Client = client,
            Target = target,
            Changes = changes,
            Warnings = warnings
        };
    }

    public static string CreateBackup(EnvironmentVariableTarget target)
    {
        Directory.CreateDirectory(BackupDirectory);
        var backup = new EnvironmentBackup
        {
            CreatedAt = DateTime.Now,
            Scope = target.ToString(),
            PathValue = Environment.GetEnvironmentVariable("Path", target),
            OracleHome = Environment.GetEnvironmentVariable("ORACLE_HOME", target),
            TnsAdmin = Environment.GetEnvironmentVariable("TNS_ADMIN", target)
        };
        string file = Path.Combine(BackupDirectory, $"env-{DateTime.Now:yyyyMMdd-HHmmssfff}-{target}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
        return file;
    }

    public static string? GetLatestBackup()
    {
        if (!Directory.Exists(BackupDirectory)) return null;
        return Directory.EnumerateFiles(BackupDirectory, "env-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    public static IReadOnlyList<EnvironmentBackupRecord> GetBackups()
    {
        if (!Directory.Exists(BackupDirectory)) return Array.Empty<EnvironmentBackupRecord>();
        var result = new List<EnvironmentBackupRecord>();
        foreach (string file in Directory.EnumerateFiles(BackupDirectory, "env-*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                EnvironmentBackup? backup = JsonSerializer.Deserialize<EnvironmentBackup>(File.ReadAllText(file));
                if (backup is not null) result.Add(new EnvironmentBackupRecord { FilePath = file, Backup = backup });
            }
            catch { }
        }
        return result;
    }

    public static ApplyResult Restore(string backupFile)
    {
        string? safetyBackup = null;
        try
        {
            EnvironmentBackup backup = ReadBackup(backupFile);
            var target = Enum.Parse<EnvironmentVariableTarget>(backup.Scope);
            safetyBackup = CreateBackup(target);
            WriteEnvironment(target, backup.PathValue, backup.OracleHome, backup.TnsAdmin);
            VerifyEnvironment(target, backup.PathValue, backup.OracleHome, backup.TnsAdmin);
            BroadcastEnvironmentChange();
            return new ApplyResult
            {
                Success = true,
                BackupPath = safetyBackup,
                Message = $"已恢复 {backup.CreatedAt:yyyy-MM-dd HH:mm:ss} 的{TargetName(target)}环境变量。\n恢复前的配置也已自动备份。"
            };
        }
        catch (Exception ex)
        {
            string rollbackError = string.Empty;
            bool rolledBack = safetyBackup is not null && TryRestoreSnapshot(safetyBackup, out rollbackError);
            string rollbackText = rolledBack
                ? "\n已自动回滚到恢复操作前的环境。"
                : safetyBackup is null ? string.Empty : $"\n自动回滚失败：{rollbackError}";
            return new ApplyResult { Success = false, BackupPath = safetyBackup, RolledBack = rolledBack, Message = "恢复失败：" + ex.Message + rollbackText };
        }
    }

    public static EnvironmentVariableTarget ReadBackupTarget(string file)
    {
        var backup = JsonSerializer.Deserialize<EnvironmentBackup>(File.ReadAllText(file));
        return Enum.TryParse(backup?.Scope, out EnvironmentVariableTarget result) ? result : EnvironmentVariableTarget.User;
    }

    public static string CreateOperationResultPath()
    {
        Directory.CreateDirectory(ResultDirectory);
        return Path.Combine(ResultDirectory, $"result-{Guid.NewGuid():N}.json");
    }

    public static void WriteOperationResult(string file, ApplyResult result)
    {
        string? directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(result));
        File.Move(temporary, file, true);
    }

    public static bool TryReadOperationResult(string file, out ApplyResult? result)
    {
        result = null;
        if (!File.Exists(file)) return false;
        try
        {
            result = JsonSerializer.Deserialize<ApplyResult>(File.ReadAllText(file));
            File.Delete(file);
            return result is not null;
        }
        catch (IOException) { return false; }
        catch { return false; }
    }

    public static string? FindEffectiveOraclePath(IEnumerable<OracleClientInfo> clients)
    {
        return DetectOracleEnvironment(clients).ActiveClient?.PathDirectory;
    }

    public static OracleEnvironmentStatus DetectOracleEnvironment(IEnumerable<OracleClientInfo> source)
    {
        List<OracleClientInfo> clients = source.ToList();
        var lookup = clients.ToDictionary(x => OracleScanner.Normalize(x.PathDirectory), StringComparer.OrdinalIgnoreCase);
        var invalid = new List<(string Path, string Source)>();
        var issues = new List<EnvironmentIssue>();
        var validPathClients = new List<(OracleClientInfo Client, string Scope)>();
        OracleClientInfo? active = null;
        string activeSource = string.Empty;

        // Windows 为新进程合并系统与用户 PATH；不存在的条目会被跳过，因此继续查找后续有效客户端。
        foreach (EnvironmentVariableTarget target in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
        {
            string pathValue = Environment.GetEnvironmentVariable("Path", target) ?? string.Empty;
            foreach (string entry in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!OracleScanner.LooksLikeOraclePath(entry)) continue;
                string normalized = OracleScanner.Normalize(entry);
                if (lookup.TryGetValue(normalized, out OracleClientInfo? client) && Directory.Exists(normalized))
                {
                    string scope = target == EnvironmentVariableTarget.Machine ? "系统" : "当前用户";
                    validPathClients.Add((client, scope));
                    active ??= client;
                    if (string.IsNullOrEmpty(activeSource)) activeSource = target == EnvironmentVariableTarget.Machine ? "系统 PATH" : "用户 PATH";
                }
                else if (!Directory.Exists(normalized))
                {
                    string scope = target == EnvironmentVariableTarget.Machine ? "系统" : "当前用户";
                    invalid.Add((normalized, scope + " PATH"));
                    issues.Add(new EnvironmentIssue
                    {
                        Severity = EnvironmentIssueSeverity.Error,
                        Item = "PATH",
                        Scope = scope,
                        CurrentValue = normalized,
                        Problem = "目录不存在，任何程序都无法从这里加载 OCI。",
                        Recommendation = "删除该 PATH 条目，或切换到一个实际存在的客户端目录。"
                    });
                }
            }
        }

        foreach (var extra in validPathClients.Skip(1).Where(x => !string.Equals(x.Client.PathDirectory, active?.PathDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new EnvironmentIssue
            {
                Severity = EnvironmentIssueSeverity.Warning,
                Item = "PATH",
                Scope = extra.Scope,
                CurrentValue = extra.Client.PathDirectory,
                Problem = "PATH 中存在多个 Oracle 客户端，实际加载结果可能受顺序影响。",
                Recommendation = "只保留需要作为默认版本的客户端路径。"
            });
        }

        foreach (EnvironmentVariableTarget target in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
        {
            string? home = Environment.GetEnvironmentVariable("ORACLE_HOME", target);
            if (string.IsNullOrWhiteSpace(home)) continue;
            string normalized = OracleScanner.Normalize(home);
            OracleClientInfo? homeClient = clients.FirstOrDefault(x =>
                string.Equals(OracleScanner.Normalize(x.OracleHome), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(OracleScanner.Normalize(x.PathDirectory), normalized, StringComparison.OrdinalIgnoreCase));
            if (homeClient is not null && Directory.Exists(normalized))
            {
                active ??= homeClient;
                if (string.IsNullOrEmpty(activeSource)) activeSource = target == EnvironmentVariableTarget.Machine ? "系统 ORACLE_HOME" : "用户 ORACLE_HOME";
                if (active is not null && !string.Equals(homeClient.PathDirectory, active.PathDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new EnvironmentIssue
                    {
                        Severity = EnvironmentIssueSeverity.Warning,
                        Item = "ORACLE_HOME",
                        Scope = target == EnvironmentVariableTarget.Machine ? "系统" : "当前用户",
                        CurrentValue = normalized,
                        Problem = $"与 PATH 当前指向的 Oracle {active.Version} 不一致。",
                        Recommendation = "将 ORACLE_HOME 调整为当前客户端，或对 Instant Client 清除此变量。"
                    });
                }
            }
            else if (!Directory.Exists(normalized))
            {
                string scope = target == EnvironmentVariableTarget.Machine ? "系统" : "当前用户";
                invalid.Add((normalized, scope + " ORACLE_HOME"));
                issues.Add(new EnvironmentIssue
                {
                    Severity = EnvironmentIssueSeverity.Error,
                    Item = "ORACLE_HOME",
                    Scope = scope,
                    CurrentValue = normalized,
                    Problem = "变量指向不存在的目录。",
                    Recommendation = "清除此变量；仅在旧程序明确要求时才为 Instant Client 设置它。"
                });
            }
        }

        foreach (EnvironmentVariableTarget target in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
        {
            string? tnsAdmin = Environment.GetEnvironmentVariable("TNS_ADMIN", target);
            if (string.IsNullOrWhiteSpace(tnsAdmin)) continue;
            string normalized = OracleScanner.Normalize(tnsAdmin);
            if (!Directory.Exists(normalized))
            {
                string scope = target == EnvironmentVariableTarget.Machine ? "系统" : "当前用户";
                invalid.Add((normalized, scope + " TNS_ADMIN"));
                issues.Add(new EnvironmentIssue
                {
                    Severity = EnvironmentIssueSeverity.Error,
                    Item = "TNS_ADMIN",
                    Scope = scope,
                    CurrentValue = normalized,
                    Problem = "网络配置目录不存在。",
                    Recommendation = "改为所选客户端的 network\\admin 目录。"
                });
            }
            else if (!File.Exists(Path.Combine(normalized, "tnsnames.ora")) && !File.Exists(Path.Combine(normalized, "sqlnet.ora")))
            {
                issues.Add(new EnvironmentIssue
                {
                    Severity = EnvironmentIssueSeverity.Warning,
                    Item = "TNS_ADMIN",
                    Scope = target == EnvironmentVariableTarget.Machine ? "系统" : "当前用户",
                    CurrentValue = normalized,
                    Problem = "目录存在，但未找到 tnsnames.ora 或 sqlnet.ora。",
                    Recommendation = "复制有效的网络配置文件，或改为客户端已有的 network\\admin 目录。"
                });
            }
        }

        if (active is not null)
        {
            int warningCount = issues.Count(x => x.Severity != EnvironmentIssueSeverity.Info);
            string warning = warningCount > 0 ? $"；另有 {warningCount} 项需要处理" : string.Empty;
            return new OracleEnvironmentStatus
            {
                State = OracleEnvironmentState.Active,
                ActiveClient = active,
                Summary = $"Oracle {active.Version} · {active.Architecture}",
                Detail = $"通过{activeSource}识别：{active.PathDirectory}{warning}",
                ConfiguredPath = active.PathDirectory,
                HasWarnings = warningCount > 0,
                Issues = issues
            };
        }

        if (invalid.Count > 0)
        {
            string invalidPath = invalid[0].Path;
            OracleClientInfo? suggestion = FindClosestClient(invalidPath, clients);
            string suggestionText = suggestion is null ? "请选择一个可用客户端重新配置。" : $"建议修复为 {suggestion.PathDirectory}";
            return new OracleEnvironmentStatus
            {
                State = OracleEnvironmentState.Invalid,
                SuggestedClient = suggestion,
                Summary = "环境配置失效",
                Detail = $"{invalid[0].Source} 指向不存在的目录：{invalidPath}\n{suggestionText}",
                ConfiguredPath = invalidPath,
                HasWarnings = true,
                Issues = issues
            };
        }

        issues.Add(new EnvironmentIssue
        {
            Severity = EnvironmentIssueSeverity.Warning,
            Item = "默认客户端",
            Scope = "系统/当前用户",
            CurrentValue = "未配置",
            Problem = "PATH 和 ORACLE_HOME 中没有可用的 Oracle 客户端。",
            Recommendation = "在主界面选择一个客户端并执行切换。"
        });
        return new OracleEnvironmentStatus
        {
            State = OracleEnvironmentState.NotConfigured,
            Summary = "尚未配置默认客户端",
            Detail = "PATH 和 ORACLE_HOME 中没有发现可用的 Oracle 客户端。请选择一个版本进行切换。",
            HasWarnings = true,
            Issues = issues
        };
    }

    public static List<string> GetStaleOracleEntries(EnvironmentVariableTarget target)
    {
        return GetOracleEntries(target)
            .Where(x => !Directory.Exists(Environment.ExpandEnvironmentVariables(x.Trim('"'))))
            .ToList();
    }

    public static List<string> GetOracleEntries(EnvironmentVariableTarget target)
    {
        string path = Environment.GetEnvironmentVariable("Path", target) ?? string.Empty;
        return path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(OracleScanner.LooksLikeOraclePath)
            .ToList();
    }

    private static OracleClientInfo? FindClosestClient(string configuredPath, List<OracleClientInfo> clients)
    {
        if (clients.Count == 0) return null;
        string expected = NormalizeName(Path.GetFileName(configuredPath));
        return clients
            .Select(client => new
            {
                Client = client,
                Score = Levenshtein(expected, NormalizeName(Path.GetFileName(client.PathDirectory)))
            })
            .OrderBy(x => x.Score)
            .ThenByDescending(x => x.Client.Version)
            .First().Client;
    }

    private static string NormalizeName(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int Levenshtein(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    public static bool RelaunchElevated(params string[] arguments)
    {
        try
        {
            string executable = Environment.ProcessPath ?? Application.ExecutablePath;
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; }
        catch { return false; }
    }

    private static DesiredEnvironment BuildDesiredEnvironment(
        OracleClientInfo client,
        EnvironmentVariableTarget target,
        bool setOracleHome)
    {
        string oldPath = Environment.GetEnvironmentVariable("Path", target) ?? string.Empty;
        var removed = new List<string>();
        var retained = new List<string>();
        foreach (string entry in oldPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool selected = string.Equals(
                OracleScanner.Normalize(entry),
                OracleScanner.Normalize(client.PathDirectory),
                StringComparison.OrdinalIgnoreCase);
            if (selected || OracleScanner.LooksLikeOraclePath(entry))
                removed.Add(entry);
            else
                retained.Add(entry);
        }

        retained.Insert(0, client.PathDirectory);
        string newPath = string.Join(';', retained.Distinct(StringComparer.OrdinalIgnoreCase));
        return new DesiredEnvironment(
            newPath,
            setOracleHome ? client.OracleHome : null,
            TnsConfigurationManager.GetEffectiveDirectory(client),
            removed);
    }

    private static void WriteEnvironment(
        EnvironmentVariableTarget target,
        string? path,
        string? oracleHome,
        string? tnsAdmin)
    {
        Environment.SetEnvironmentVariable("Path", path, target);
        Environment.SetEnvironmentVariable("TNS_ADMIN", tnsAdmin, target);
        Environment.SetEnvironmentVariable("ORACLE_HOME", oracleHome, target);
    }

    private static void VerifyEnvironment(
        EnvironmentVariableTarget target,
        string? expectedPath,
        string? expectedHome,
        string? expectedTns)
    {
        var mismatches = new List<string>();
        if (!VariableEquals(Environment.GetEnvironmentVariable("Path", target), expectedPath)) mismatches.Add("PATH");
        if (!VariableEquals(Environment.GetEnvironmentVariable("ORACLE_HOME", target), expectedHome)) mismatches.Add("ORACLE_HOME");
        if (!VariableEquals(Environment.GetEnvironmentVariable("TNS_ADMIN", target), expectedTns)) mismatches.Add("TNS_ADMIN");
        if (mismatches.Count > 0)
            throw new InvalidOperationException($"写入后校验失败：{string.Join("、", mismatches)} 与预期值不一致。");
    }

    private static bool TryRestoreSnapshot(string backupFile, out string error)
    {
        try
        {
            EnvironmentBackup backup = ReadBackup(backupFile);
            var target = Enum.Parse<EnvironmentVariableTarget>(backup.Scope);
            WriteEnvironment(target, backup.PathValue, backup.OracleHome, backup.TnsAdmin);
            VerifyEnvironment(target, backup.PathValue, backup.OracleHome, backup.TnsAdmin);
            BroadcastEnvironmentChange();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static EnvironmentBackup ReadBackup(string file)
    {
        return JsonSerializer.Deserialize<EnvironmentBackup>(File.ReadAllText(file))
            ?? throw new InvalidDataException("备份文件内容无效。");
    }

    private static bool VariableEquals(string? left, string? right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
    }

    private static string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "（未设置）" : value;

    private sealed record DesiredEnvironment(
        string PathValue,
        string? OracleHome,
        string TnsAdmin,
        IReadOnlyList<string> RemovedOracleEntries);

    private static string TargetName(EnvironmentVariableTarget target) => target == EnvironmentVariableTarget.Machine ? "系统级" : "当前用户";

    private static void BroadcastEnvironmentChange()
    {
        SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, 3000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
