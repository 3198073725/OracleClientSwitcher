using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OracleClientSwitcher;

internal static class ClientVerificationService
{
    private static readonly string TempDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OracleClientSwitcher", "verification-temp");

    public static async Task<List<ClientVerificationItem>> RunAllAsync(
        OracleClientInfo client,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var items = new List<ClientVerificationItem>();

        progress?.Report("正在检查 OCI 文件和位数…");
        items.Add(CheckOciFile(client));
        items.Add(CheckArchitecture(client));

        progress?.Report("正在分析依赖 DLL…");
        items.AddRange(CheckDependencies(client));

        progress?.Report("正在隔离加载 OCI…");
        items.Add(await ProbeOciAsync(client, cancellationToken));

        progress?.Report("正在检查 SQL*Plus…");
        items.Add(await CheckSqlPlusAsync(client, cancellationToken));

        progress?.Report("正在读取 TNS 配置…");
        items.Add(CheckTnsConfiguration(client));
        int errors = items.Count(x => x.Severity == VerificationSeverity.Error);
        int warnings = items.Count(x => x.Severity == VerificationSeverity.Warning);
        AppLog.Information("验证", "客户端验证", errors == 0 ? "完成" : "发现错误",
            $"Oracle {client.Version} {client.Architecture}；错误 {errors}，警告 {warnings}；{client.PathDirectory}");
        return items;
    }

    public static IReadOnlyList<string> ReadTnsAliases(OracleClientInfo client)
    {
        string directory = TnsConfigurationManager.GetEffectiveDirectory(client);
        return TnsConfigurationManager.ReadAliases(TnsConfigurationManager.LoadFile(directory, "tnsnames.ora"));
    }

    public static async Task<ClientVerificationItem> TestTnsAliasAsync(
        OracleClientInfo client,
        string alias,
        CancellationToken cancellationToken = default)
    {
        string? tnsping = FindExecutable(client, "tnsping.exe");
        if (tnsping is null)
        {
            return new ClientVerificationItem
            {
                Category = "TNS",
                Name = $"服务 {alias}",
                Severity = VerificationSeverity.Warning,
                Summary = "无法测试：未安装 TNSPING",
                Detail = "当前客户端没有找到 tnsping.exe。可以继续使用 OCI，但无法执行网络服务探测。"
            };
        }

        ExternalProcessResult result = await RunProcessAsync(
            tnsping,
            new[] { alias, "2" },
            client.PathDirectory,
            TimeSpan.FromSeconds(20),
            cancellationToken,
            TnsConfigurationManager.GetEffectiveDirectory(client));
        string output = CombineOutput(result);
        if (result.TimedOut)
        {
            return new ClientVerificationItem
            {
                Category = "TNS",
                Name = $"服务 {alias}",
                Severity = VerificationSeverity.Error,
                Summary = "测试超时",
                Detail = "TNSPING 在 20 秒内没有完成。请检查网络、监听地址和防火墙。"
            };
        }

        bool success = result.Started && result.ExitCode == 0;
        return new ClientVerificationItem
        {
            Category = "TNS",
            Name = $"服务 {alias}",
            Severity = success ? VerificationSeverity.Success : VerificationSeverity.Error,
            Summary = success ? "TNS 服务可达" : $"TNSPING 失败（退出码 {result.ExitCode}）",
            Detail = Limit(output, 5000)
        };
    }

    public static ClientVerificationItem CheckTargetExecutable(OracleClientInfo client, string executable)
    {
        string architecture = OracleScanner.ReadPeArchitecture(executable);
        bool known = architecture != "未知" && client.Architecture != "未知";
        bool compatible = known && string.Equals(architecture, client.Architecture, StringComparison.OrdinalIgnoreCase);
        return new ClientVerificationItem
        {
            Category = "兼容性",
            Name = Path.GetFileName(executable),
            Severity = !known ? VerificationSeverity.Warning : compatible ? VerificationSeverity.Success : VerificationSeverity.Error,
            Summary = !known ? "无法识别目标程序位数" : compatible ? $"位数匹配：{architecture}" : $"位数不匹配：程序 {architecture} / OCI {client.Architecture}",
            Detail = !known
                ? executable
                : compatible
                    ? $"目标程序与 Oracle 客户端均为 {architecture}，可以加载对应 OCI。\r\n{executable}"
                    : $"{architecture} 程序不能加载 {client.Architecture} OCI。请选择相同位数的 Oracle 客户端。\r\n{executable}"
        };
    }

    public static OciProbeResult RunOciProbeInCurrentProcess(string ociPath)
    {
        try
        {
            nint handle = NativeLibrary.Load(ociPath);
            NativeLibrary.Free(handle);
            return new OciProbeResult { Success = true, Message = "OCI DLL 已成功加载并安全卸载。" };
        }
        catch (Exception ex)
        {
            return new OciProbeResult
            {
                Success = false,
                ErrorCode = Marshal.GetLastPInvokeError(),
                Message = ex.Message
            };
        }
    }

    private static ClientVerificationItem CheckOciFile(OracleClientInfo client)
    {
        bool exists = File.Exists(client.OciPath);
        return new ClientVerificationItem
        {
            Category = "OCI",
            Name = "OCI 文件",
            Severity = exists ? VerificationSeverity.Success : VerificationSeverity.Error,
            Summary = exists ? "oci.dll 存在" : "oci.dll 缺失",
            Detail = client.OciPath
        };
    }

    private static ClientVerificationItem CheckArchitecture(OracleClientInfo client)
    {
        string os = Environment.Is64BitOperatingSystem ? "64 位 Windows" : "32 位 Windows";
        bool supported = !(client.Architecture == "64 位" && !Environment.Is64BitOperatingSystem);
        return new ClientVerificationItem
        {
            Category = "兼容性",
            Name = "客户端位数",
            Severity = supported ? VerificationSeverity.Success : VerificationSeverity.Error,
            Summary = $"{client.Architecture} 客户端 / {os}",
            Detail = client.Architecture == "32 位" && Environment.Is64BitOperatingSystem
                ? "操作系统支持该客户端；目标程序也必须是 32 位。"
                : supported ? "操作系统支持该客户端位数。" : "当前操作系统无法运行此客户端。"
        };
    }

    private static IEnumerable<ClientVerificationItem> CheckDependencies(OracleClientInfo client)
    {
        IReadOnlyList<string> imports;
        try { imports = ReadImportedLibraries(client.OciPath); }
        catch (Exception ex)
        {
            return new[]
            {
                new ClientVerificationItem
                {
                    Category = "依赖",
                    Name = "PE 依赖分析",
                    Severity = VerificationSeverity.Warning,
                    Summary = "无法解析 OCI 依赖表",
                    Detail = ex.Message
                }
            };
        }

        List<string> missing = imports.Where(x => !CanResolveLibrary(client, x)).ToList();
        List<string> runtimes = imports.Where(IsVisualCppRuntime).ToList();
        List<string> missingRuntimes = runtimes.Where(x => !CanResolveLibrary(client, x)).ToList();
        return new[]
        {
            new ClientVerificationItem
            {
                Category = "依赖",
                Name = "直接依赖 DLL",
                Severity = missing.Count == 0 ? VerificationSeverity.Success : VerificationSeverity.Warning,
                Summary = missing.Count == 0 ? $"{imports.Count} 个直接依赖均可定位" : $"发现 {missing.Count} 个未定位依赖",
                Detail = missing.Count == 0
                    ? "已在客户端目录、Windows 系统目录和当前 PATH 中找到全部直接依赖。"
                    : "未定位：" + string.Join("、", missing) + "\r\n最终以 OCI 隔离加载结果为准。"
            },
            new ClientVerificationItem
            {
                Category = "依赖",
                Name = "VC++ 运行库",
                Severity = missingRuntimes.Count > 0 ? VerificationSeverity.Warning : VerificationSeverity.Success,
                Summary = runtimes.Count == 0 ? "未发现直接 VC++ 运行库依赖" : missingRuntimes.Count == 0 ? "VC++ 运行库可定位" : "VC++ 运行库可能缺失",
                Detail = runtimes.Count == 0 ? "OCI 的直接导入表没有列出 MSVC 运行库；间接依赖由实际加载测试继续验证。" :
                    missingRuntimes.Count == 0 ? string.Join("、", runtimes) : "未定位：" + string.Join("、", missingRuntimes)
            }
        };
    }

    private static async Task<ClientVerificationItem> ProbeOciAsync(OracleClientInfo client, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(TempDirectory);
        string token = Guid.NewGuid().ToString("N");
        string resultFile = Path.Combine(TempDirectory, $"oci-{token}.json");
        string? scriptFile = null;
        ExternalProcessResult processResult;
        try
        {
            if (client.Architecture == "32 位" && Environment.Is64BitOperatingSystem)
            {
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "SysWOW64", "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(powershell))
                    throw new FileNotFoundException("没有找到 Windows 32 位 PowerShell，无法隔离加载 32 位 OCI。", powershell);
                scriptFile = Path.Combine(TempDirectory, $"oci-{token}.ps1");
                File.WriteAllText(scriptFile, PowerShellProbeScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                processResult = await RunProcessAsync(
                    powershell,
                    new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptFile, "-Dll", client.OciPath, "-Result", resultFile },
                    client.PathDirectory,
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
            }
            else
            {
                string executable = Environment.ProcessPath ?? Application.ExecutablePath;
                processResult = await RunProcessAsync(
                    executable,
                    new[] { "--probe-oci", client.OciPath, resultFile },
                    client.PathDirectory,
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
            }

            if (processResult.TimedOut)
                return Verification("OCI", "隔离加载", VerificationSeverity.Error, "OCI 加载超时", "探测进程已终止，客户端 DLL 可能损坏或依赖初始化被阻塞。");
            if (!File.Exists(resultFile))
                return Verification("OCI", "隔离加载", VerificationSeverity.Error, "OCI 加载进程异常退出", Limit(CombineOutput(processResult), 3000));

            OciProbeResult? probe = JsonSerializer.Deserialize<OciProbeResult>(File.ReadAllText(resultFile));
            if (probe is null)
                return Verification("OCI", "隔离加载", VerificationSeverity.Error, "未获得有效探测结果", string.Empty);
            return Verification(
                "OCI", "隔离加载",
                probe.Success ? VerificationSeverity.Success : VerificationSeverity.Error,
                probe.Success ? "OCI DLL 加载成功" : $"OCI DLL 加载失败{(probe.ErrorCode == 0 ? string.Empty : $"（错误 {probe.ErrorCode}）")}",
                probe.Message);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Verification("OCI", "隔离加载", VerificationSeverity.Error, "无法启动 OCI 探测", ex.Message);
        }
        finally
        {
            TryDelete(resultFile);
            if (scriptFile is not null) TryDelete(scriptFile);
        }
    }

    private static async Task<ClientVerificationItem> CheckSqlPlusAsync(OracleClientInfo client, CancellationToken cancellationToken)
    {
        if (!client.HasSqlPlus || !File.Exists(client.SqlPlusPath))
            return Verification("工具", "SQL*Plus", VerificationSeverity.Info, "未安装（不影响 OCI 使用）", client.SqlPlusPath);

        ExternalProcessResult result = await RunProcessAsync(
            client.SqlPlusPath,
            new[] { "-V" },
            client.PathDirectory,
            TimeSpan.FromSeconds(12),
            cancellationToken);
        string output = CombineOutput(result);
        if (result.TimedOut)
            return Verification("工具", "SQL*Plus", VerificationSeverity.Error, "版本检测超时", "sqlplus -V 在 12 秒内没有结束，进程已终止。");
        bool success = result.Started && result.ExitCode == 0;
        return Verification("工具", "SQL*Plus", success ? VerificationSeverity.Success : VerificationSeverity.Warning,
            success ? "可正常启动" : $"启动失败（退出码 {result.ExitCode}）", Limit(output, 3000));
    }

    private static ClientVerificationItem CheckTnsConfiguration(OracleClientInfo client)
    {
        string directory = TnsConfigurationManager.GetEffectiveDirectory(client);
        string file = Path.Combine(directory, "tnsnames.ora");
        if (!Directory.Exists(directory))
            return Verification("TNS", "网络配置", VerificationSeverity.Warning, "TNS 目录不存在", directory);
        if (!File.Exists(file))
            return Verification("TNS", "网络配置", VerificationSeverity.Warning, "未找到 tnsnames.ora", file);
        IReadOnlyList<string> aliases = ReadTnsAliases(client);
        return Verification("TNS", "网络配置", aliases.Count > 0 ? VerificationSeverity.Success : VerificationSeverity.Warning,
            aliases.Count > 0 ? $"发现 {aliases.Count} 个服务别名" : "没有解析到服务别名", file);
    }

    private static async Task<ExternalProcessResult> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string clientDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? tnsAdmin = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = clientDirectory,
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        string inheritedPath = startInfo.Environment.TryGetValue("PATH", out string? value) ? value ?? string.Empty : string.Empty;
        startInfo.Environment["PATH"] = clientDirectory + ";" + inheritedPath;
        if (!string.IsNullOrWhiteSpace(tnsAdmin)) startInfo.Environment["TNS_ADMIN"] = tnsAdmin;
        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return new ExternalProcessResult { Started = false, Error = "进程未能启动。" };
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
                return new ExternalProcessResult
                {
                    Started = true,
                    ExitCode = process.ExitCode,
                    Output = await outputTask,
                    Error = await errorTask
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(true); } catch { }
                return new ExternalProcessResult
                {
                    Started = true,
                    TimedOut = true,
                    ExitCode = -1,
                    Output = await SafeRead(outputTask),
                    Error = await SafeRead(errorTask)
                };
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                throw;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ExternalProcessResult { Started = false, ExitCode = -1, Error = ex.Message };
        }
        finally { process.Dispose(); }
    }

    private static IReadOnlyList<string> ReadImportedLibraries(string file)
    {
        using var stream = File.OpenRead(file);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        stream.Position = 0x3c;
        int peOffset = reader.ReadInt32();
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550) throw new InvalidDataException("不是有效的 PE 文件。");
        reader.ReadUInt16();
        ushort sectionCount = reader.ReadUInt16();
        stream.Position += 12;
        ushort optionalSize = reader.ReadUInt16();
        stream.Position += 2;
        long optionalOffset = stream.Position;
        ushort magic = reader.ReadUInt16();
        int dataDirectoryOffset = magic == 0x20b ? 112 : magic == 0x10b ? 96 : throw new InvalidDataException("未知 PE 可选头格式。");
        stream.Position = optionalOffset + dataDirectoryOffset + 8;
        uint importRva = reader.ReadUInt32();
        if (importRva == 0) return Array.Empty<string>();

        stream.Position = optionalOffset + optionalSize;
        var sections = new List<(uint VirtualAddress, uint VirtualSize, uint RawSize, uint RawOffset)>();
        for (int i = 0; i < sectionCount; i++)
        {
            stream.Position += 8;
            uint virtualSize = reader.ReadUInt32();
            uint virtualAddress = reader.ReadUInt32();
            uint rawSize = reader.ReadUInt32();
            uint rawOffset = reader.ReadUInt32();
            stream.Position += 16;
            sections.Add((virtualAddress, virtualSize, rawSize, rawOffset));
        }

        long RvaToOffset(uint rva)
        {
            foreach (var section in sections)
            {
                uint size = Math.Max(section.VirtualSize, section.RawSize);
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
                    return section.RawOffset + (rva - section.VirtualAddress);
            }
            throw new InvalidDataException($"无法映射 RVA 0x{rva:X}。");
        }

        var libraries = new List<string>();
        long descriptor = RvaToOffset(importRva);
        for (int i = 0; i < 1024; i++)
        {
            stream.Position = descriptor + i * 20L;
            uint originalThunk = reader.ReadUInt32();
            uint timeStamp = reader.ReadUInt32();
            uint forwarder = reader.ReadUInt32();
            uint nameRva = reader.ReadUInt32();
            uint firstThunk = reader.ReadUInt32();
            if (originalThunk == 0 && timeStamp == 0 && forwarder == 0 && nameRva == 0 && firstThunk == 0) break;
            if (nameRva == 0) continue;
            stream.Position = RvaToOffset(nameRva);
            var bytes = new List<byte>();
            for (int n = 0; n < 512; n++)
            {
                byte b = reader.ReadByte();
                if (b == 0) break;
                bytes.Add(b);
            }
            string name = Encoding.ASCII.GetString(bytes.ToArray());
            if (name.Length > 0 && !libraries.Contains(name, StringComparer.OrdinalIgnoreCase)) libraries.Add(name);
        }
        return libraries.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool CanResolveLibrary(OracleClientInfo client, string library)
    {
        if (library.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) ||
            library.StartsWith("ext-ms-win-", StringComparison.OrdinalIgnoreCase)) return true;
        var directories = new List<string>
        {
            client.PathDirectory,
            Path.Combine(client.OracleHome, "bin"),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            client.Architecture == "32 位" && Environment.Is64BitOperatingSystem
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64")
                : Environment.SystemDirectory
        };
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        directories.AddRange(path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return directories.Distinct(StringComparer.OrdinalIgnoreCase).Any(x =>
        {
            try { return File.Exists(Path.Combine(Environment.ExpandEnvironmentVariables(x.Trim('"')), library)); }
            catch { return false; }
        });
    }

    private static bool IsVisualCppRuntime(string value)
    {
        return value.StartsWith("msvcr", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("msvcp", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("vcruntime", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("ucrtbase", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindExecutable(OracleClientInfo client, string name)
    {
        string direct = Path.Combine(client.PathDirectory, name);
        if (File.Exists(direct)) return direct;
        string bin = Path.Combine(client.OracleHome, "bin", name);
        return File.Exists(bin) ? bin : null;
    }

    private static ClientVerificationItem Verification(string category, string name, VerificationSeverity severity, string summary, string detail)
        => new() { Category = category, Name = name, Severity = severity, Summary = summary, Detail = detail };

    private static string CombineOutput(ExternalProcessResult result)
    {
        return string.Join(Environment.NewLine, new[] { result.Output.Trim(), result.Error.Trim() }.Where(x => x.Length > 0));
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    private static async Task<string> SafeRead(Task<string> task)
    {
        try { return await task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch { return string.Empty; }
    }

    private static string StripComment(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            if (line[i] == '#' && !quoted) return line[..i];
        }
        return line;
    }

    private static int ParenthesisDelta(string value)
    {
        int delta = 0;
        bool quoted = false;
        foreach (char c in value)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && c == '(') delta++;
            else if (!quoted && c == ')') delta--;
        }
        return delta;
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { }
    }

    private const string PowerShellProbeScript = """
param([string]$Dll, [string]$Result)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class OciLoader {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr LoadLibraryW(string path);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool FreeLibrary(IntPtr module);
}
'@
$handle = [OciLoader]::LoadLibraryW($Dll)
if ($handle -eq [IntPtr]::Zero) {
    $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    $message = (New-Object ComponentModel.Win32Exception($code)).Message
    $payload = @{ Success = $false; ErrorCode = $code; Message = $message }
} else {
    [OciLoader]::FreeLibrary($handle) | Out-Null
    $payload = @{ Success = $true; ErrorCode = 0; Message = 'OCI DLL 已成功加载并安全卸载。' }
}
$payload | ConvertTo-Json -Compress | Set-Content -LiteralPath $Result -Encoding UTF8
""";
}
