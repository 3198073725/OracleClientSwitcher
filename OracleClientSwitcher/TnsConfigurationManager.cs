using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OracleClientSwitcher;

internal static class TnsConfigurationManager
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OracleClientSwitcher");
    private static readonly string SettingsFile = Path.Combine(DataDirectory, "settings.json");
    private static readonly string BackupDirectory = Path.Combine(DataDirectory, "tns-backups");
    private static readonly object SettingsLock = new();

    public static OracleSwitcherSettings LoadSettings()
    {
        lock (SettingsLock)
        {
            try
            {
                if (!File.Exists(SettingsFile)) return new OracleSwitcherSettings();
                return JsonSerializer.Deserialize<OracleSwitcherSettings>(File.ReadAllText(SettingsFile)) ?? new OracleSwitcherSettings();
            }
            catch { return new OracleSwitcherSettings(); }
        }
    }

    public static TnsOperationResult SaveSettings(bool useSharedDirectory, string? sharedDirectory)
    {
        try
        {
            string? normalized = null;
            if (useSharedDirectory)
            {
                if (string.IsNullOrWhiteSpace(sharedDirectory))
                    return new TnsOperationResult { Success = false, Message = "请选择公共 TNS 目录。" };
                normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(sharedDirectory.Trim().Trim('"')));
                Directory.CreateDirectory(normalized);
            }

            lock (SettingsLock)
            {
                Directory.CreateDirectory(DataDirectory);
                var settings = new OracleSwitcherSettings
                {
                    UseSharedTnsDirectory = useSharedDirectory,
                    SharedTnsDirectory = normalized
                };
                string temporary = SettingsFile + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, SettingsFile, true);
            }
            return new TnsOperationResult
            {
                Success = true,
                Message = useSharedDirectory
                    ? $"已启用公共 TNS 目录：{normalized}\n下次切换客户端时会保持使用该目录。"
                    : "已关闭公共 TNS 目录；切换时将使用各客户端自己的 network\\admin。"
            };
        }
        catch (Exception ex)
        {
            return new TnsOperationResult { Success = false, Message = "保存设置失败：" + ex.Message };
        }
    }

    public static string GetEffectiveDirectory(OracleClientInfo client)
    {
        OracleSwitcherSettings settings = LoadSettings();
        if (settings.UseSharedTnsDirectory && !string.IsNullOrWhiteSpace(settings.SharedTnsDirectory))
        {
            try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.SharedTnsDirectory)); }
            catch { }
        }
        return client.TnsAdmin;
    }

    public static string LoadFile(string directory, string fileName)
    {
        string file = Path.Combine(directory, fileName);
        return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
    }

    public static TnsOperationResult SaveFile(string directory, string fileName, string content)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, fileName);
            string? backup = null;
            if (File.Exists(target))
            {
                Directory.CreateDirectory(BackupDirectory);
                string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(target)))[..10];
                string backupName = $"{Path.GetFileNameWithoutExtension(fileName)}-{identity}-{DateTime.Now:yyyyMMdd-HHmmssfff}{Path.GetExtension(fileName)}";
                backup = Path.Combine(BackupDirectory, backupName);
                File.Copy(target, backup, true);
            }

            string temporary = target + $".tmp-{Guid.NewGuid():N}";
            try
            {
                File.WriteAllText(temporary, NormalizeNewLines(content), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temporary, target, true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
            return new TnsOperationResult
            {
                Success = true,
                BackupPath = backup,
                Message = backup is null ? $"已创建 {fileName}。" : $"已保存 {fileName}，原文件已备份。"
            };
        }
        catch (Exception ex)
        {
            return new TnsOperationResult { Success = false, Message = $"保存 {fileName} 失败：{ex.Message}" };
        }
    }

    public static IReadOnlyList<TnsAliasEntry> ParseAliasEntries(string content)
    {
        string[] lines = NormalizeNewLines(content).Split("\r\n");
        var entries = new List<TnsAliasEntry>();
        for (int i = 0; i < lines.Length; i++)
        {
            string clean = StripComment(lines[i]).Trim();
            if (clean.Length == 0 || clean.StartsWith("IFILE", StringComparison.OrdinalIgnoreCase)) continue;
            int equals = clean.IndexOf('=');
            if (equals <= 0) continue;
            string left = clean[..equals].Trim();
            if (left.Contains('(') || left.Contains(')')) continue;
            string[] aliases = left.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (aliases.Length == 0) continue;

            int start = i;
            int end = i;
            int depth = ParenthesisDelta(clean);
            bool sawOpening = clean.Contains('(');
            string right = clean[(equals + 1)..].Trim();
            if (right.Length == 0 || sawOpening)
            {
                for (int j = i + 1; j < lines.Length; j++)
                {
                    string next = StripComment(lines[j]).Trim();
                    end = j;
                    if (next.Contains('(')) sawOpening = true;
                    depth += ParenthesisDelta(next);
                    if (sawOpening && depth <= 0) break;
                    if (!sawOpening && next.Contains('=') && !next.StartsWith("#"))
                    {
                        end = j - 1;
                        break;
                    }
                }
            }
            string text = string.Join("\r\n", lines[start..(end + 1)]).TrimEnd();
            entries.Add(new TnsAliasEntry { Aliases = aliases, Text = text, StartLine = start + 1 });
            i = end;
        }
        return entries;
    }

    public static IReadOnlyList<string> ReadAliases(string content)
    {
        return ParseAliasEntries(content)
            .SelectMany(x => x.Aliases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<TnsValidationIssue> ValidateTnsNames(string content)
    {
        var issues = new List<TnsValidationIssue>();
        string[] lines = NormalizeNewLines(content).Split("\r\n");
        int depth = 0;
        bool quoted = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = StripComment(lines[i]);
            foreach (char c in line)
            {
                if (c == '"') quoted = !quoted;
                if (quoted) continue;
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth < 0)
                    {
                        issues.Add(new TnsValidationIssue { Severity = TnsIssueSeverity.Error, Line = i + 1, Message = "出现了没有对应左括号的右括号。" });
                        depth = 0;
                    }
                }
            }
        }
        if (depth > 0) issues.Add(new TnsValidationIssue { Severity = TnsIssueSeverity.Error, Line = lines.Length, Message = $"还有 {depth} 个左括号没有闭合。" });
        if (quoted) issues.Add(new TnsValidationIssue { Severity = TnsIssueSeverity.Error, Line = lines.Length, Message = "存在没有闭合的双引号。" });

        IReadOnlyList<TnsAliasEntry> entries = ParseAliasEntries(content);
        foreach (IGrouping<string, (string Alias, int Line)> duplicate in entries
                     .SelectMany(x => x.Aliases.Select(alias => (Alias: alias, Line: x.StartLine)))
                     .GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
        {
            issues.Add(new TnsValidationIssue
            {
                Severity = TnsIssueSeverity.Error,
                Line = duplicate.Skip(1).First().Line,
                Message = $"服务别名“{duplicate.Key}”重复定义。"
            });
        }
        foreach (var alias in entries.SelectMany(x => x.Aliases.Select(alias => (Alias: alias, Line: x.StartLine))))
        {
            if (!Regex.IsMatch(alias.Alias, @"^[A-Za-z0-9_.-]+$"))
                issues.Add(new TnsValidationIssue { Severity = TnsIssueSeverity.Warning, Line = alias.Line, Message = $"服务别名“{alias.Alias}”包含非常规字符。" });
        }
        if (entries.Count == 0 && !string.IsNullOrWhiteSpace(content))
            issues.Add(new TnsValidationIssue { Severity = TnsIssueSeverity.Warning, Line = 1, Message = "没有解析到有效的服务别名。" });
        if (issues.Count == 0)
            issues.Add(new TnsValidationIssue { Severity = TnsIssueSeverity.Info, Line = 0, Message = $"格式检查通过，共发现 {entries.SelectMany(x => x.Aliases).Count()} 个服务别名。" });
        return issues;
    }

    public static TnsServiceDefinition ParseService(TnsAliasEntry entry)
    {
        string text = entry.Text;
        MatchCollection hosts = Regex.Matches(text, @"\(\s*HOST\s*=\s*([^\)]+)\)", RegexOptions.IgnoreCase);
        MatchCollection ports = Regex.Matches(text, @"\(\s*PORT\s*=\s*([^\)]+)\)", RegexOptions.IgnoreCase);
        MatchCollection protocols = Regex.Matches(text, @"\(\s*PROTOCOL\s*=\s*([^\)]+)\)", RegexOptions.IgnoreCase);
        MatchCollection services = Regex.Matches(text, @"\(\s*SERVICE_NAME\s*=\s*([^\)]+)\)", RegexOptions.IgnoreCase);
        MatchCollection sids = Regex.Matches(text, @"\(\s*SID\s*=\s*([^\)]+)\)", RegexOptions.IgnoreCase);
        List<TnsEndpoint> endpoints = ParseEndpoints(text);
        bool failover = ReadBoolean(text, "FAILOVER");
        bool loadBalance = ReadBoolean(text, "LOAD_BALANCE");
        int connectTimeout = ReadInteger(text, "CONNECT_TIMEOUT");
        int retryCount = ReadInteger(text, "RETRY_COUNT");
        string walletDirectory = ReadValue(text, "MY_WALLET_DIRECTORY");
        var allowedKeys = new HashSet<string>(new[]
        {
            "DESCRIPTION", "ADDRESS_LIST", "ADDRESS", "PROTOCOL", "HOST", "PORT", "CONNECT_DATA", "SERVICE_NAME", "SID",
            "FAILOVER", "LOAD_BALANCE", "CONNECT_TIMEOUT", "TRANSPORT_CONNECT_TIMEOUT", "RETRY_COUNT", "SECURITY", "MY_WALLET_DIRECTORY", "SSL_SERVER_DN_MATCH", "SSL_SERVER_CERT_DN"
        }, StringComparer.OrdinalIgnoreCase);
        List<string> unsupported = Regex.Matches(text, @"\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*=")
            .Select(x => x.Groups[1].Value)
            .Where(x => !allowedKeys.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool containsComments = Regex.IsMatch(text, @"(?m)^\s*#");
        string protocolValue = protocols.Cast<Match>().FirstOrDefault()?.Groups[1].Value.Trim().ToUpperInvariant() ?? "TCP";
        bool supportedProtocol = protocolValue is "TCP" or "TCPS";
        bool validPort = int.TryParse(ports.Cast<Match>().FirstOrDefault()?.Groups[1].Value.Trim(), out int port) && port is >= 1 and <= 65535;
        string connectValue = (services.Count > 0 ? services[0].Groups[1].Value : sids.Count > 0 ? sids[0].Groups[1].Value : string.Empty).Trim();
        bool hasAdvancedOptions = Regex.IsMatch(text,
            @"\(\s*(FAILOVER|LOAD_BALANCE|CONNECT_TIMEOUT|TRANSPORT_CONNECT_TIMEOUT|RETRY_COUNT|SECURITY|MY_WALLET_DIRECTORY)\s*=",
            RegexOptions.IgnoreCase);
        bool shapeIsSimple = entry.Aliases.Count == 1 && hosts.Count == 1 && ports.Count == 1 && protocols.Count == 1 &&
                             services.Count + sids.Count == 1 && unsupported.Count == 0 && !containsComments &&
                             supportedProtocol && validPort && connectValue.Length > 0 && !hasAdvancedOptions;
        string limitation = shapeIsSimple ? string.Empty :
            entry.Aliases.Count > 1 ? "该条目同时定义了多个别名，请在高级源码中编辑。" :
            hosts.Count > 1 ? "该条目包含多个地址或主机，请在高级源码中编辑。" :
            unsupported.Count > 0 ? $"该条目包含高级参数：{string.Join("、", unsupported)}。请在高级源码中编辑。" :
            containsComments ? "该条目包含注释，为避免丢失说明，请在高级源码中编辑。" :
            !supportedProtocol ? $"可视化编辑暂不支持 {protocolValue} 协议，请在高级源码中编辑。" :
            !validPort ? "该条目的端口不是 1–65535 范围内的整数，请在高级源码中编辑。" :
            "该条目结构较复杂或字段不完整，请在高级源码中编辑。";
        return new TnsServiceDefinition
        {
            SourceEntry = entry,
            Alias = entry.Aliases.FirstOrDefault() ?? string.Empty,
            Protocol = protocolValue,
            Host = hosts.Cast<Match>().FirstOrDefault()?.Groups[1].Value.Trim() ?? string.Empty,
            Port = port > 0 ? port : 1521,
            ConnectName = connectValue,
            UsesSid = sids.Count > 0,
            IsSimpleEditable = shapeIsSimple,
            Limitation = limitation,
            Endpoints = endpoints,
            Failover = failover,
            LoadBalance = loadBalance,
            ConnectTimeoutSeconds = connectTimeout,
            RetryCount = retryCount,
            WalletDirectory = walletDirectory
        };
    }

    public static string BuildSimpleService(string alias, string protocol, string host, int port, string connectName, bool usesSid)
    {
        string connectKey = usesSid ? "SID" : "SERVICE_NAME";
        return $"{alias.Trim()} =\r\n" +
               "  (DESCRIPTION =\r\n" +
               "    (ADDRESS_LIST =\r\n" +
               $"      (ADDRESS = (PROTOCOL = {protocol.Trim().ToUpperInvariant()})(HOST = {host.Trim()})(PORT = {port}))\r\n" +
               "    )\r\n" +
               "    (CONNECT_DATA =\r\n" +
               $"      ({connectKey} = {connectName.Trim()})\r\n" +
               "    )\r\n" +
               "  )";
    }

    public static TnsAdvancedDefinition ToAdvancedDefinition(TnsServiceDefinition service)
    {
        return new TnsAdvancedDefinition
        {
            Alias = service.Alias,
            Endpoints = service.Endpoints.Count > 0
                ? service.Endpoints.Select(x => new TnsEndpoint { Protocol = x.Protocol, Host = x.Host, Port = x.Port }).ToList()
                : new List<TnsEndpoint> { new() { Protocol = service.Protocol, Host = service.Host, Port = service.Port } },
            ConnectName = service.ConnectName,
            UsesSid = service.UsesSid,
            Failover = service.Failover,
            LoadBalance = service.LoadBalance,
            ConnectTimeoutSeconds = service.ConnectTimeoutSeconds,
            RetryCount = service.RetryCount,
            WalletDirectory = service.WalletDirectory
        };
    }

    public static string BuildAdvancedService(TnsAdvancedDefinition service)
    {
        if (string.IsNullOrWhiteSpace(service.Alias)) throw new InvalidOperationException("请填写服务名。");
        if (service.Endpoints.Count == 0) throw new InvalidOperationException("至少需要一个主机地址。");
        if (service.Endpoints.Any(x => string.IsNullOrWhiteSpace(x.Host) || x.Port is < 1 or > 65535 ||
                                       !string.Equals(x.Protocol, "TCP", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.Protocol, "TCPS", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("地址中的协议、主机或端口无效。");
        if (string.IsNullOrWhiteSpace(service.ConnectName)) throw new InvalidOperationException("请填写服务标识。");

        string connectKey = service.UsesSid ? "SID" : "SERVICE_NAME";
        var builder = new StringBuilder();
        builder.AppendLine($"{service.Alias.Trim()} =");
        builder.AppendLine("  (DESCRIPTION =");
        if (service.ConnectTimeoutSeconds > 0) builder.AppendLine($"    (CONNECT_TIMEOUT = {service.ConnectTimeoutSeconds})");
        if (service.RetryCount > 0) builder.AppendLine($"    (RETRY_COUNT = {service.RetryCount})");
        builder.AppendLine("    (ADDRESS_LIST =");
        builder.AppendLine($"      (LOAD_BALANCE = {(service.LoadBalance ? "ON" : "OFF")})");
        builder.AppendLine($"      (FAILOVER = {(service.Failover ? "ON" : "OFF")})");
        foreach (TnsEndpoint endpoint in service.Endpoints)
            builder.AppendLine($"      (ADDRESS = (PROTOCOL = {endpoint.Protocol.Trim().ToUpperInvariant()})(HOST = {endpoint.Host.Trim()})(PORT = {endpoint.Port}))");
        builder.AppendLine("    )");
        builder.AppendLine("    (CONNECT_DATA =");
        builder.AppendLine($"      ({connectKey} = {service.ConnectName.Trim()})");
        builder.AppendLine("    )");
        if (!string.IsNullOrWhiteSpace(service.WalletDirectory))
        {
            builder.AppendLine("    (SECURITY =");
            builder.AppendLine($"      (MY_WALLET_DIRECTORY = {service.WalletDirectory.Trim()})");
            builder.AppendLine("      (SSL_SERVER_DN_MATCH = YES)");
            builder.AppendLine("    )");
        }
        builder.Append("  )");
        return builder.ToString();
    }

    public static string ReplaceEntry(string content, TnsAliasEntry entry, string? replacement)
    {
        string normalized = NormalizeNewLines(content);
        string source = NormalizeNewLines(entry.Text);
        int index = normalized.IndexOf(source, StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException("无法在当前编辑内容中定位该服务，请重新加载后再试。");
        string value = replacement is null ? string.Empty : NormalizeNewLines(replacement);
        string result = normalized.Remove(index, source.Length).Insert(index, value);
        return Regex.Replace(result, @"(\r\n){3,}", "\r\n\r\n").Trim() + "\r\n";
    }

    public static string MergeTnsNames(string existing, string imported, out int added, out int skipped)
    {
        var existingAliases = new HashSet<string>(ReadAliases(existing), StringComparer.OrdinalIgnoreCase);
        var blocks = new List<string>();
        added = 0;
        skipped = 0;
        foreach (TnsAliasEntry entry in ParseAliasEntries(imported))
        {
            if (entry.Aliases.Any(existingAliases.Contains))
            {
                skipped += entry.Aliases.Count;
                continue;
            }
            blocks.Add(entry.Text);
            foreach (string alias in entry.Aliases)
            {
                existingAliases.Add(alias);
                added++;
            }
        }
        if (blocks.Count == 0) return NormalizeNewLines(existing);
        return NormalizeNewLines(existing).TrimEnd() + "\r\n\r\n" + string.Join("\r\n\r\n", blocks) + "\r\n";
    }

    public static IReadOnlyList<TnsOperationResult> SyncConfiguration(
        string sourceDirectory,
        IEnumerable<OracleClientInfo> targets,
        bool syncTnsNames,
        bool syncSqlNet)
    {
        var results = new List<TnsOperationResult>();
        var processedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OracleClientInfo target in targets)
        {
            string directory = target.TnsAdmin;
            if (!processedDirectories.Add(OracleScanner.Normalize(directory))) continue;
            foreach (string fileName in new[] { syncTnsNames ? "tnsnames.ora" : null, syncSqlNet ? "sqlnet.ora" : null }.Where(x => x is not null).Cast<string>())
            {
                string source = Path.Combine(sourceDirectory, fileName);
                if (!File.Exists(source))
                {
                    results.Add(new TnsOperationResult { Success = false, Message = $"源目录没有 {fileName}，已跳过 {target.PathDirectory}。" });
                    continue;
                }
                TnsOperationResult saved = SaveFile(directory, fileName, File.ReadAllText(source));
                results.Add(new TnsOperationResult
                {
                    Success = saved.Success,
                    BackupPath = saved.BackupPath,
                    Message = saved.Success ? $"Oracle {target.Version} {target.Architecture}：{fileName} 已同步。" : $"Oracle {target.Version} {target.Architecture}：{saved.Message}"
                });
            }
        }
        return results;
    }

    private static string NormalizeNewLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
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

    private static List<TnsEndpoint> ParseEndpoints(string text)
    {
        var endpoints = new List<TnsEndpoint>();
        MatchCollection addresses = Regex.Matches(text,
            @"\(\s*ADDRESS\s*=\s*(?<body>(?:\s*\(\s*(?:PROTOCOL|HOST|PORT)\s*=\s*[^\)]+\)\s*)+)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match address in addresses)
        {
            string body = address.Groups["body"].Value;
            string protocol = ReadValue(body, "PROTOCOL").ToUpperInvariant();
            string host = ReadValue(body, "HOST");
            int port = ReadInteger(body, "PORT");
            if (host.Length > 0)
                endpoints.Add(new TnsEndpoint { Protocol = protocol is "TCP" or "TCPS" ? protocol : "TCP", Host = host, Port = port is >= 1 and <= 65535 ? port : 1521 });
        }
        if (endpoints.Count > 0) return endpoints;

        MatchCollection hosts = Regex.Matches(text, @"\(\s*HOST\s*=\s*([^\)]+)\)", RegexOptions.IgnoreCase);
        MatchCollection ports = Regex.Matches(text, @"\(\s*PORT\s*=\s*([^\)]+)\)", RegexOptions.IgnoreCase);
        MatchCollection protocols = Regex.Matches(text, @"\(\s*PROTOCOL\s*=\s*([^\)]+)\)", RegexOptions.IgnoreCase);
        for (int index = 0; index < hosts.Count; index++)
        {
            string protocol = index < protocols.Count ? protocols[index].Groups[1].Value.Trim().ToUpperInvariant() : "TCP";
            int port = index < ports.Count && int.TryParse(ports[index].Groups[1].Value.Trim(), out int parsed) ? parsed : 1521;
            endpoints.Add(new TnsEndpoint { Protocol = protocol is "TCP" or "TCPS" ? protocol : "TCP", Host = hosts[index].Groups[1].Value.Trim(), Port = port });
        }
        return endpoints;
    }

    private static bool ReadBoolean(string text, string key)
    {
        string value = ReadValue(text, key);
        return value.Equals("ON", StringComparison.OrdinalIgnoreCase) || value.Equals("YES", StringComparison.OrdinalIgnoreCase) || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInteger(string text, string key) => int.TryParse(ReadValue(text, key), out int value) ? value : 0;

    private static string ReadValue(string text, string key)
    {
        Match match = Regex.Match(text, $@"\(\s*{Regex.Escape(key)}\s*=\s*([^\)]+)\)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim().Trim('"') : string.Empty;
    }
}
