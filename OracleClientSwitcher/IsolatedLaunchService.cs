using System.Diagnostics;
using System.Text.Json;

namespace OracleClientSwitcher;

internal static class IsolatedLaunchService
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OracleClientSwitcher");
    private static readonly string ProfilesFile = Path.Combine(DataDirectory, "isolated-launch-profiles.json");
    private static readonly object ProfilesLock = new();

    public static IsolatedLaunchPlan BuildPlan(
        OracleClientInfo client,
        string executablePath,
        string? arguments,
        string? workingDirectory,
        bool setOracleHome)
    {
        string executable = Path.GetFullPath(Environment.ExpandEnvironmentVariables(executablePath.Trim().Trim('"')));
        string working = string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetDirectoryName(executable) ?? client.PathDirectory
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(workingDirectory.Trim().Trim('"')));
        string targetArchitecture = File.Exists(executable) ? OracleScanner.ReadPeArchitecture(executable) : "未知";
        bool known = targetArchitecture != "未知" && client.Architecture != "未知";
        bool compatible = known && string.Equals(targetArchitecture, client.Architecture, StringComparison.OrdinalIgnoreCase);

        return new IsolatedLaunchPlan
        {
            Client = client,
            ExecutablePath = executable,
            Arguments = arguments?.Trim() ?? string.Empty,
            WorkingDirectory = working,
            PathValue = BuildIsolatedPath(client),
            TnsAdmin = TnsConfigurationManager.GetEffectiveDirectory(client),
            OracleHome = setOracleHome ? client.OracleHome : null,
            TargetArchitecture = targetArchitecture,
            ArchitectureKnown = known,
            ArchitectureCompatible = compatible
        };
    }

    public static IsolatedLaunchResult Launch(IsolatedLaunchPlan plan)
    {
        if (!File.Exists(plan.ExecutablePath))
            return Failure("目标程序不存在，请重新选择可执行文件。", plan);
        if (!string.Equals(Path.GetExtension(plan.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            return Failure("隔离启动目前仅支持 Windows .exe 程序。", plan);
        if (!Directory.Exists(plan.WorkingDirectory))
            return Failure("工作目录不存在，请重新选择。", plan);
        if (plan.ArchitectureKnown && !plan.ArchitectureCompatible)
            return Failure($"位数不匹配：目标程序为 {plan.TargetArchitecture}，Oracle 客户端为 {plan.Client.Architecture}。", plan);

        try
        {
            ProcessStartInfo startInfo = CreateStartInfo(plan);
            Process? process = Process.Start(startInfo);
            if (process is null) return Failure("Windows 未能启动目标程序。", plan);
            RecordProfile(new IsolatedLaunchProfile
            {
                ExecutablePath = plan.ExecutablePath,
                Arguments = plan.Arguments,
                WorkingDirectory = plan.WorkingDirectory,
                SetOracleHome = plan.OracleHome is not null,
                LastUsedAt = DateTime.Now
            });
            var result = new IsolatedLaunchResult
            {
                Success = true,
                ProcessId = process.Id,
                Message = $"已使用 Oracle {plan.Client.Version} {plan.Client.Architecture} 隔离启动 {Path.GetFileName(plan.ExecutablePath)}。"
            };
            AppLog.Information("隔离启动", Path.GetFileName(plan.ExecutablePath), "成功", result.Message);
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Error("隔离启动", Path.GetFileName(plan.ExecutablePath), ex);
            return Failure("启动失败：" + ex.Message, plan);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(IsolatedLaunchPlan plan)
    {
        var startInfo = new ProcessStartInfo(plan.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = plan.WorkingDirectory,
            Arguments = plan.Arguments
        };
        startInfo.Environment["PATH"] = plan.PathValue;
        startInfo.Environment["TNS_ADMIN"] = plan.TnsAdmin;
        if (plan.OracleHome is null) startInfo.Environment.Remove("ORACLE_HOME");
        else startInfo.Environment["ORACLE_HOME"] = plan.OracleHome;
        return startInfo;
    }

    public static IReadOnlyList<IsolatedLaunchProfile> LoadProfiles()
    {
        lock (ProfilesLock)
        {
            try
            {
                if (!File.Exists(ProfilesFile)) return Array.Empty<IsolatedLaunchProfile>();
                return (JsonSerializer.Deserialize<List<IsolatedLaunchProfile>>(File.ReadAllText(ProfilesFile)) ?? new())
                    .Where(x => !string.IsNullOrWhiteSpace(x.ExecutablePath))
                    .OrderByDescending(x => x.LastUsedAt)
                    .Take(10)
                    .ToList();
            }
            catch { return Array.Empty<IsolatedLaunchProfile>(); }
        }
    }

    public static string BuildEnvironmentReport(IsolatedLaunchPlan plan)
    {
        return $"目标程序：{plan.ExecutablePath}\r\n" +
               $"程序位数：{plan.TargetArchitecture}\r\n" +
               $"Oracle：{plan.Client.Version} · {plan.Client.Architecture}\r\n" +
               $"工作目录：{plan.WorkingDirectory}\r\n\r\n" +
               $"PATH={plan.PathValue}\r\n" +
               $"TNS_ADMIN={plan.TnsAdmin}\r\n" +
               $"ORACLE_HOME={plan.OracleHome ?? "（清除，不传给目标程序）"}";
    }

    private static string BuildIsolatedPath(OracleClientInfo client)
    {
        string inherited = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var values = new List<string> { client.PathDirectory };
        values.AddRange(inherited.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().Trim('"'))
            .Where(x => x.Length > 0 && !OracleScanner.LooksLikeOraclePath(x)));
        return string.Join(';', values.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static void RecordProfile(IsolatedLaunchProfile profile)
    {
        lock (ProfilesLock)
        {
            try
            {
                List<IsolatedLaunchProfile> profiles = LoadProfiles().ToList();
                profiles.RemoveAll(x => string.Equals(OracleScanner.Normalize(x.ExecutablePath), OracleScanner.Normalize(profile.ExecutablePath), StringComparison.OrdinalIgnoreCase));
                profiles.Insert(0, profile);
                Directory.CreateDirectory(DataDirectory);
                string temporary = ProfilesFile + $".tmp-{Guid.NewGuid():N}";
                try
                {
                    File.WriteAllText(temporary, JsonSerializer.Serialize(profiles.Take(10), new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(temporary, ProfilesFile, true);
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
            catch { }
        }
    }

    private static IsolatedLaunchResult Failure(string message, IsolatedLaunchPlan? plan = null)
    {
        AppLog.Warning("隔离启动", plan is null ? "启动" : Path.GetFileName(plan.ExecutablePath), message);
        return new IsolatedLaunchResult { Success = false, Message = message };
    }
}
