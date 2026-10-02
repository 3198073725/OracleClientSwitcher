using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Reflection;
using System.Diagnostics;

namespace OracleClientSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleUnhandledException(e.Exception, true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => HandleUnhandledException(e.ExceptionObject as Exception ?? new Exception("未知的非托管异常"), false);
        TaskScheduler.UnobservedTaskException += (_, e) => { AppLog.Error("程序", "未观察到的任务异常", e.Exception); e.SetObserved(); };

        if (TryRunHelper(args)) return;
        CompatibilityCheckResult compatibility = CompatibilityService.Check();
        AppLog.Information("程序", "兼容性检查", compatibility.Summary, string.Join("；", compatibility.Warnings));
        if (!compatibility.Supported)
        {
            MessageBox.Show(compatibility.Summary + Environment.NewLine + string.Join(Environment.NewLine, compatibility.Warnings),
                "系统不兼容", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        string? newer = CompatibilityService.FindNewerLocalVersion();
        if (newer is not null)
            MessageBox.Show($"检测到同一目录中有更新版本：\n{Path.GetFileName(newer)}\n\n建议关闭当前版本后使用新版本。",
                "发现更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
        AppLog.Information("程序", "启动", "成功", $"版本 {Assembly.GetExecutingAssembly().GetName().Version}");
        Application.Run(new MainForm());
    }

    private static void HandleUnhandledException(Exception exception, bool canContinue)
    {
        AppLog.Error("程序", "未处理异常", exception);
        if (!canContinue) return;
        try
        {
            MessageBox.Show("程序遇到了问题，但错误已经被记录，避免了 Windows 原始错误弹窗。\n\n" +
                            "请打开“日志报告”生成诊断包。\n\n错误摘要：" + AppLog.Redact(exception.Message),
                "Oracle 客户端切换器", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }

    private static bool TryRunHelper(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--probe-oci")
        {
            OciProbeResult result = ClientVerificationService.RunOciProbeInCurrentProcess(args[1]);
            File.WriteAllText(args[2], JsonSerializer.Serialize(result));
            return true;
        }

        if (args.Length >= 3 && args[0] == "--verification-report")
        {
            OracleClientInfo? client = OracleScanner.CreateClientInfo(args[1]);
            List<ClientVerificationItem> results = client is null
                ? new List<ClientVerificationItem>
                {
                    new() { Category = "客户端", Name = "目录", Severity = VerificationSeverity.Error, Summary = "不是有效的 Oracle 客户端", Detail = args[1] }
                }
                : ClientVerificationService.RunAllAsync(client).GetAwaiter().GetResult();
            File.WriteAllText(args[2], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 3 && args[0] == "--tns-validate-report")
        {
            string content = File.Exists(args[1]) ? File.ReadAllText(args[1]) : string.Empty;
            var report = new
            {
                Aliases = TnsConfigurationManager.ReadAliases(content),
                Issues = TnsConfigurationManager.ValidateTnsNames(content)
            };
            File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 4 && args[0] == "--tns-merge-report")
        {
            string existing = File.ReadAllText(args[1]);
            string imported = File.ReadAllText(args[2]);
            string merged = TnsConfigurationManager.MergeTnsNames(existing, imported, out int added, out int skipped);
            var report = new
            {
                Added = added,
                Skipped = skipped,
                Aliases = TnsConfigurationManager.ReadAliases(merged),
                Issues = TnsConfigurationManager.ValidateTnsNames(merged),
                Merged = merged
            };
            File.WriteAllText(args[3], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 3 && args[0] == "--tns-visual-report")
        {
            string content = File.Exists(args[1]) ? File.ReadAllText(args[1]) : string.Empty;
            List<TnsServiceDefinition> services = TnsConfigurationManager.ParseAliasEntries(content)
                .Select(TnsConfigurationManager.ParseService)
                .ToList();
            var report = new
            {
                Total = services.Count,
                SimpleEditable = services.Count(x => x.IsSimpleEditable),
                Advanced = services.Count(x => !x.IsSimpleEditable),
                Services = services.Select(x => new
                {
                    x.Alias,
                    x.Protocol,
                    x.Host,
                    x.Port,
                    ConnectType = x.UsesSid ? "SID" : "SERVICE_NAME",
                    x.ConnectName,
                    x.IsSimpleEditable,
                    x.Limitation
                }),
                Issues = TnsConfigurationManager.ValidateTnsNames(content)
            };
            File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 3 && args[0] == "--tns-visual-roundtrip-report")
        {
            string content = File.Exists(args[1]) ? File.ReadAllText(args[1]) : string.Empty;
            TnsServiceDefinition? service = TnsConfigurationManager.ParseAliasEntries(content)
                .Select(TnsConfigurationManager.ParseService)
                .FirstOrDefault(x => x.IsSimpleEditable);
            string updated = content;
            if (service is not null)
            {
                string rebuilt = TnsConfigurationManager.BuildSimpleService(service.Alias, service.Protocol, service.Host,
                    service.Port, service.ConnectName, service.UsesSid);
                updated = TnsConfigurationManager.ReplaceEntry(content, service.SourceEntry, rebuilt);
            }
            var report = new
            {
                FoundEditableService = service is not null,
                Service = service?.Alias,
                AliasesPreserved = TnsConfigurationManager.ReadAliases(content)
                    .SequenceEqual(TnsConfigurationManager.ReadAliases(updated), StringComparer.OrdinalIgnoreCase),
                Issues = TnsConfigurationManager.ValidateTnsNames(updated)
            };
            File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 2 && args[0] == "--tns-interaction-report")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleClientInfo? client = clients.FirstOrDefault(x => File.Exists(Path.Combine(TnsConfigurationManager.GetEffectiveDirectory(x), "tnsnames.ora"))) ?? clients.FirstOrDefault();
            if (client is null) return true;
            using var form = new TnsConfigurationForm(client, clients);
            form.Show();
            PumpUi(700);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(TnsConfigurationForm);
            var list = (ListBox)type.GetField("serviceList", flags)!.GetValue(form)!;
            var connectType = (ComboBox)type.GetField("connectTypeCombo", flags)!.GetValue(form)!;
            var sqlNet = (RichTextBox)type.GetField("sqlNetEditor", flags)!.GetValue(form)!;
            var tns = (RichTextBox)type.GetField("tnsEditor", flags)!.GetValue(form)!;
            FieldInfo visualDirty = type.GetField("visualDraftDirty", flags)!;

            object? beforeFormEdit = list.SelectedItem;
            int originalType = connectType.SelectedIndex;
            connectType.SelectedIndex = originalType == 0 ? 1 : 0;
            PumpUi(700);
            bool formEditPreservedList = ReferenceEquals(beforeFormEdit, list.SelectedItem);

            visualDirty.SetValue(form, false);
            object? beforeSqlNetEdit = list.SelectedItem;
            sqlNet.AppendText(" ");
            PumpUi(700);
            bool sqlNetEditPreservedList = ReferenceEquals(beforeSqlNetEdit, list.SelectedItem);

            object? beforeSourceEdit = list.SelectedItem;
            tns.AppendText("\r\n");
            PumpUi(700);
            bool tnsSourceEditRefreshedList = !ReferenceEquals(beforeSourceEdit, list.SelectedItem);

            visualDirty.SetValue(form, false);
            var report = new
            {
                ServiceCount = list.Items.Count,
                FormEditPreservedList = formEditPreservedList,
                SqlNetEditPreservedList = sqlNetEditPreservedList,
                TnsSourceEditRefreshedList = tnsSourceEditRefreshedList,
                Passed = formEditPreservedList && sqlNetEditPreservedList && tnsSourceEditRefreshedList
            };
            form.Hide();
            File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 2 && args[0] == "--tns-logic-report")
        {
            string simple = TnsConfigurationManager.BuildSimpleService("APPDB", "TCP", "db.example.local", 1521, "app", false) + "\r\n";
            string sid = TnsConfigurationManager.BuildSimpleService("LEGACY", "TCPS", "10.0.0.8", 2484, "ORCL", true) + "\r\n";
            string complex = "RACDB =\r\n  (DESCRIPTION =\r\n    (ADDRESS_LIST =\r\n      (ADDRESS = (PROTOCOL = TCP)(HOST = node1)(PORT = 1521))\r\n      (ADDRESS = (PROTOCOL = TCP)(HOST = node2)(PORT = 1521))\r\n    )\r\n    (CONNECT_DATA = (SERVICE_NAME = rac))\r\n  )\r\n";
            string commented = "NOTED =\r\n  (DESCRIPTION =\r\n    # Keep this routing note\r\n    (ADDRESS = (PROTOCOL = TCP)(HOST = noted-host)(PORT = 1521))\r\n    (CONNECT_DATA = (SERVICE_NAME = noted))\r\n  )\r\n";
            TnsServiceDefinition simpleService = TnsConfigurationManager.ParseService(TnsConfigurationManager.ParseAliasEntries(simple).Single());
            TnsServiceDefinition sidService = TnsConfigurationManager.ParseService(TnsConfigurationManager.ParseAliasEntries(sid).Single());
            TnsServiceDefinition complexService = TnsConfigurationManager.ParseService(TnsConfigurationManager.ParseAliasEntries(complex).Single());
            TnsServiceDefinition commentedService = TnsConfigurationManager.ParseService(TnsConfigurationManager.ParseAliasEntries(commented).Single());
            string replaced = TnsConfigurationManager.ReplaceEntry(simple, simpleService.SourceEntry,
                TnsConfigurationManager.BuildSimpleService("APPDB", "TCP", "db2.example.local", 1522, "APP2", false));
            string merged = TnsConfigurationManager.MergeTnsNames(simple, simple + "\r\n" + sid, out int added, out int skipped);
            bool duplicateDetected = TnsConfigurationManager.ValidateTnsNames(simple + "\r\n" + simple)
                .Any(x => x.Severity == TnsIssueSeverity.Error && x.Message.Contains("重复"));
            bool replacementValid = !TnsConfigurationManager.ValidateTnsNames(replaced).Any(x => x.Severity == TnsIssueSeverity.Error) &&
                                    TnsConfigurationManager.ParseService(TnsConfigurationManager.ParseAliasEntries(replaced).Single()).Host == "db2.example.local";
            var report = new
            {
                SimpleServiceEditable = simpleService.IsSimpleEditable,
                SidAndTcpsParsed = sidService.IsSimpleEditable && sidService.UsesSid && sidService.Protocol == "TCPS" && sidService.Port == 2484,
                MultiAddressProtected = !complexService.IsSimpleEditable,
                CommentedEntryProtected = !commentedService.IsSimpleEditable,
                DuplicateDetected = duplicateDetected,
                ReplacementValid = replacementValid,
                MergeAdded = added,
                MergeSkipped = skipped,
                MergeAliases = TnsConfigurationManager.ReadAliases(merged),
                Passed = simpleService.IsSimpleEditable && sidService.IsSimpleEditable && sidService.UsesSid &&
                         !complexService.IsSimpleEditable && !commentedService.IsSimpleEditable && duplicateDetected &&
                         replacementValid && added == 1 && skipped == 1 && TnsConfigurationManager.ReadAliases(merged).Count == 2
            };
            File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 2 && args[0] == "--isolated-launch-report")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            string targetArchitecture = OracleScanner.ReadPeArchitecture(target);
            OracleClientInfo? client = clients.FirstOrDefault(x => x.Architecture == targetArchitecture) ?? clients.FirstOrDefault();
            if (client is null) return true;
            string processPathBefore = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string? processHomeBefore = Environment.GetEnvironmentVariable("ORACLE_HOME");
            string? processTnsBefore = Environment.GetEnvironmentVariable("TNS_ADMIN");
            IsolatedLaunchPlan plan = IsolatedLaunchService.BuildPlan(client, target, string.Empty, Path.GetDirectoryName(target), false);
            ProcessStartInfo startInfo = IsolatedLaunchService.CreateStartInfo(plan);
            IsolatedLaunchPlan homePlan = IsolatedLaunchService.BuildPlan(client, target, string.Empty, Path.GetDirectoryName(target), true);
            ProcessStartInfo homeStartInfo = IsolatedLaunchService.CreateStartInfo(homePlan);
            string[] entries = plan.PathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool firstPath = entries.Length > 0 && string.Equals(OracleScanner.Normalize(entries[0]), OracleScanner.Normalize(client.PathDirectory), StringComparison.OrdinalIgnoreCase);
            bool otherOracleRemoved = entries.Skip(1).All(x => !OracleScanner.LooksLikeOraclePath(x));
            bool childInjected = startInfo.UseShellExecute == false &&
                                 startInfo.Environment["PATH"] == plan.PathValue &&
                                 startInfo.Environment["TNS_ADMIN"] == plan.TnsAdmin &&
                                 !startInfo.Environment.ContainsKey("ORACLE_HOME");
            bool globalUnchanged = processPathBefore == (Environment.GetEnvironmentVariable("PATH") ?? string.Empty) &&
                                   processHomeBefore == Environment.GetEnvironmentVariable("ORACLE_HOME") &&
                                   processTnsBefore == Environment.GetEnvironmentVariable("TNS_ADMIN");
            var report = new
            {
                Target = target,
                plan.TargetArchitecture,
                OracleArchitecture = client.Architecture,
                plan.ArchitectureCompatible,
                SelectedClientIsFirstPath = firstPath,
                OtherOraclePathsRemoved = otherOracleRemoved,
                TnsAdmin = plan.TnsAdmin,
                OracleHomeClearedForChild = plan.OracleHome is null,
                ChildEnvironmentInjected = childInjected,
                OptionalOracleHomeInjected = homeStartInfo.Environment["ORACLE_HOME"] == client.OracleHome,
                GlobalEnvironmentUnchanged = globalUnchanged,
                Passed = plan.ArchitectureCompatible && firstPath && otherOracleRemoved && childInjected &&
                         homeStartInfo.Environment["ORACLE_HOME"] == client.OracleHome && globalUnchanged
            };
            File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 2 && args[0] == "--scan-behavior-report")
        {
            var firstTimer = Stopwatch.StartNew();
            List<OracleClientInfo> fresh = OracleScanner.ScanAutomaticallyAsync(forceRefresh: true).GetAwaiter().GetResult();
            firstTimer.Stop();
            var cacheTimer = Stopwatch.StartNew();
            List<OracleClientInfo> cached = OracleScanner.ScanAutomaticallyAsync(forceRefresh: false).GetAwaiter().GetResult();
            cacheTimer.Stop();
            OracleScanSettings scanSettings = ScanConfigurationManager.LoadSettings();
            bool cancellationHonored = false;
            using (var cancelSource = new CancellationTokenSource())
            {
                cancelSource.Cancel();
                try { OracleScanner.ScanAutomaticallyAsync(cancellationToken: cancelSource.Token, forceRefresh: true).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancellationHonored = true; }
            }
            bool sameClients = fresh.Select(x => OracleScanner.Normalize(x.PathDirectory)).OrderBy(x => x)
                .SequenceEqual(cached.Select(x => OracleScanner.Normalize(x.PathDirectory)).OrderBy(x => x), StringComparer.OrdinalIgnoreCase);
            bool sourcesPresent = fresh.All(x => !string.IsNullOrWhiteSpace(x.DiscoverySource));
            bool cacheMarked = cached.All(x => x.DiscoverySource.Contains("缓存", StringComparison.OrdinalIgnoreCase));
            var report = new
            {
                Mode = scanSettings.Mode.ToString(),
                scanSettings.UseCache,
                FreshCount = fresh.Count,
                CachedCount = cached.Count,
                SameClients = sameClients,
                SourcesPresent = sourcesPresent,
                CacheMarked = cacheMarked,
                CancellationHonored = cancellationHonored,
                FreshMilliseconds = firstTimer.ElapsedMilliseconds,
                CachedMilliseconds = cacheTimer.ElapsedMilliseconds,
                Passed = fresh.Count > 0 && sameClients && sourcesPresent && cancellationHonored && (!scanSettings.UseCache || cacheMarked)
            };
            File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 2 && args[0] == "--advanced-tns-report")
        {
            var definition = new TnsAdvancedDefinition
            {
                Alias = "RAC_TCPS",
                ConnectName = "APPDB",
                Failover = true,
                LoadBalance = true,
                ConnectTimeoutSeconds = 8,
                RetryCount = 3,
                WalletDirectory = @"C:\Oracle\wallet",
                Endpoints = new List<TnsEndpoint>
                {
                    new() { Protocol = "TCPS", Host = "scan01.example.local", Port = 2484 },
                    new() { Protocol = "TCPS", Host = "scan02.example.local", Port = 2484 }
                }
            };
            string content = TnsConfigurationManager.BuildAdvancedService(definition);
            TnsServiceDefinition parsed = TnsConfigurationManager.ParseService(TnsConfigurationManager.ParseAliasEntries(content).Single());
            var report = new
            {
                parsed.Alias,
                EndpointCount = parsed.Endpoints.Count,
                parsed.Failover,
                parsed.LoadBalance,
                parsed.ConnectTimeoutSeconds,
                parsed.RetryCount,
                parsed.WalletDirectory,
                Protocols = parsed.Endpoints.Select(x => x.Protocol).ToList(),
                ValidationErrors = TnsConfigurationManager.ValidateTnsNames(content).Count(x => x.Severity == TnsIssueSeverity.Error),
                Passed = parsed.Endpoints.Count == 2 && parsed.Failover && parsed.LoadBalance && parsed.ConnectTimeoutSeconds == 8 &&
                         parsed.RetryCount == 3 && parsed.WalletDirectory == definition.WalletDirectory && parsed.Endpoints.All(x => x.Protocol == "TCPS")
            };
            File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 2 && args[0] == "--diagnostic-report")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleEnvironmentStatus status = EnvironmentManager.DetectOracleEnvironment(clients);
            DiagnosticReportService.Create(args[1], clients, status);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--feature-report")
        {
            var testClients = new List<OracleClientInfo>
            {
                new() { PathDirectory = @"D:\Oracle\19", OracleHome = @"D:\Oracle\19", Version = "19.30.0.0", Architecture = "64 位", ClientType = "Instant Client", TnsAdmin = @"D:\Oracle\19\network\admin" },
                new() { PathDirectory = @"D:\Oracle\11", OracleHome = @"D:\Oracle\11", Version = "11.2.0.1", Architecture = "64 位", ClientType = "Instant Client", TnsAdmin = @"D:\Oracle\11\network\admin" },
                new() { PathDirectory = @"D:\Oracle\hidden", OracleHome = @"D:\Oracle\hidden", Version = "12.2.0.1", Architecture = "32 位", ClientType = "完整客户端", TnsAdmin = @"D:\Oracle\hidden\network\admin" }
            };
            var profileSettings = new ClientProfileSettings
            {
                SortMode = ClientSortMode.Favorite,
                Profiles = new List<OracleClientProfile>
                {
                    new() { Path = testClients[1].PathDirectory, DisplayName = "Navicat 11g 64位", Favorite = true, Notes = "旧系统专用" },
                    new() { Path = testClients[2].PathDirectory, Hidden = true }
                }
            };
            List<OracleClientInfo> visible = ClientProfileManager.SortAndFilter(testClients, profileSettings).ToList();
            string privatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "secret.txt");
            string redacted = AppLog.Redact(privatePath);
            CompatibilityCheckResult compatibility = CompatibilityService.Check();
            var report = new
            {
                VisibleCount = visible.Count,
                FavoriteFirst = visible.FirstOrDefault()?.PathDirectory == testClients[1].PathDirectory,
                DisplayName = ClientProfileManager.GetDisplayName(profileSettings, testClients[1]),
                HiddenFiltered = visible.All(x => x.PathDirectory != testClients[2].PathDirectory),
                RedactedPath = redacted,
                UserNameHidden = !redacted.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase),
                compatibility.Supported,
                compatibility.Summary,
                Passed = visible.Count == 2 && visible.FirstOrDefault()?.PathDirectory == testClients[1].PathDirectory &&
                         visible.All(x => x.PathDirectory != testClients[2].PathDirectory) && !redacted.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase)
            };
            File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        if (args.Length >= 2 && args[0] == "--diagnostics-snapshot")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleEnvironmentStatus status = EnvironmentManager.DetectOracleEnvironment(clients);
            using var form = new EnvironmentDiagnosticsForm(status);
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--ui-snapshot")
        {
            using var form = new MainForm();
            if (args.Length >= 4 && int.TryParse(args[2], out int width) && int.TryParse(args[3], out int height))
                form.Size = new Size(width, height);
            CaptureForm(form, args[1], 3);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--change-preview-snapshot")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleClientInfo? client = clients.FirstOrDefault();
            if (client is null) return true;
            EnvironmentChangePreview preview = EnvironmentManager.BuildChangePreview(client, EnvironmentVariableTarget.User, client.ClientType == "完整客户端");
            using var form = new EnvironmentChangePreviewForm(preview);
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--change-details-snapshot")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleClientInfo? client = clients.FirstOrDefault();
            if (client is null) return true;
            EnvironmentChangePreview preview = EnvironmentManager.BuildChangePreview(client, EnvironmentVariableTarget.User, client.ClientType == "完整客户端");
            EnvironmentChangeItem? pathChange = preview.Changes.FirstOrDefault(x => x.Item == "PATH");
            if (pathChange is null) return true;
            using var form = new EnvironmentChangeDetailsForm(pathChange);
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--backup-history-snapshot")
        {
            using var form = new BackupHistoryForm();
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--verification-snapshot")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleClientInfo? client = clients.FirstOrDefault();
            if (client is null) return true;
            using var form = new ClientVerificationForm(client, autoRun: false);
            form.SetPreviewResults(new[]
            {
                new ClientVerificationItem { Category = "OCI", Name = "OCI 文件", Severity = VerificationSeverity.Success, Summary = "oci.dll 存在", Detail = client.OciPath },
                new ClientVerificationItem { Category = "兼容性", Name = "客户端位数", Severity = VerificationSeverity.Success, Summary = $"{client.Architecture} 客户端 / 64 位 Windows", Detail = "操作系统支持该客户端位数。" },
                new ClientVerificationItem { Category = "依赖", Name = "直接依赖 DLL", Severity = VerificationSeverity.Success, Summary = "18 个直接依赖均可定位", Detail = "已找到全部直接依赖。" },
                new ClientVerificationItem { Category = "依赖", Name = "VC++ 运行库", Severity = VerificationSeverity.Warning, Summary = "VC++ 运行库需要确认", Detail = "最终以 OCI 隔离加载结果为准。" },
                new ClientVerificationItem { Category = "OCI", Name = "隔离加载", Severity = VerificationSeverity.Success, Summary = "OCI DLL 加载成功", Detail = "OCI DLL 已成功加载并安全卸载。" },
                new ClientVerificationItem { Category = "工具", Name = "SQL*Plus", Severity = VerificationSeverity.Info, Summary = "未安装（不影响 OCI 使用）", Detail = client.SqlPlusPath },
                new ClientVerificationItem { Category = "TNS", Name = "网络配置", Severity = VerificationSeverity.Warning, Summary = "未找到 tnsnames.ora", Detail = Path.Combine(client.TnsAdmin, "tnsnames.ora") }
            });
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--tns-config-snapshot")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleClientInfo? client = clients.FirstOrDefault(x => File.Exists(Path.Combine(TnsConfigurationManager.GetEffectiveDirectory(x), "tnsnames.ora"))) ?? clients.FirstOrDefault();
            if (client is null) return true;
            using var form = new TnsConfigurationForm(client, clients);
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--tns-sync-snapshot")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleClientInfo? client = clients.FirstOrDefault();
            if (client is null) return true;
            using var form = new TnsSyncForm(client, clients);
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--isolated-launch-snapshot")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            string architecture = OracleScanner.ReadPeArchitecture(target);
            OracleClientInfo? client = clients.FirstOrDefault(x => x.Architecture == architecture) ?? clients.FirstOrDefault();
            if (client is null) return true;
            using var form = new IsolatedLaunchForm(client, target);
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--scan-settings-snapshot")
        {
            using var form = new ScanSettingsForm();
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--advanced-tns-snapshot")
        {
            var definition = new TnsAdvancedDefinition
            {
                Alias = "RAC_PROD",
                ConnectName = "PRODDB",
                Failover = true,
                LoadBalance = true,
                ConnectTimeoutSeconds = 8,
                RetryCount = 3,
                WalletDirectory = @"C:\Oracle\wallet",
                Endpoints = new List<TnsEndpoint>
                {
                    new() { Protocol = "TCPS", Host = "scan01.example.local", Port = 2484 },
                    new() { Protocol = "TCPS", Host = "scan02.example.local", Port = 2484 }
                }
            };
            using var form = new AdvancedTnsServiceForm(definition);
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--client-profile-snapshot")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleClientInfo? client = clients.FirstOrDefault();
            if (client is null) return true;
            using var form = new ClientProfileForm(client, ClientProfileManager.Load());
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--logs-snapshot")
        {
            List<OracleClientInfo> clients = OracleScanner.ScanAutomaticallyAsync().GetAwaiter().GetResult();
            OracleEnvironmentStatus status = EnvironmentManager.DetectOracleEnvironment(clients);
            AppLog.Information("预览", "日志界面", "示例", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            using var form = new LogViewerForm(clients, status);
            CaptureForm(form, args[1], 1);
            return true;
        }

        if (args.Length >= 3 && args[0] == "--apply-system")
        {
            OracleClientInfo? client = OracleScanner.CreateClientInfo(args[1]);
            bool setHome = bool.TryParse(args[2], out bool value) && value;
            ApplyResult result = client is null
                ? new ApplyResult { Success = false, Message = "指定目录不是有效的 Oracle 客户端。" }
                : EnvironmentManager.Apply(client, EnvironmentVariableTarget.Machine, setHome);
            string? resultFile = GetOptionValue(args, "--result-file");
            bool reported = TryReportResult(resultFile, result);
            if (!reported && !result.Success)
                MessageBox.Show(result.Message, "切换失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return true;
        }

        if (args.Length >= 2 && args[0] == "--restore")
        {
            ApplyResult result = EnvironmentManager.Restore(args[1]);
            string? resultFile = GetOptionValue(args, "--result-file");
            if (!TryReportResult(resultFile, result))
                MessageBox.Show(result.Message, result.Success ? "恢复成功" : "恢复失败",
                    MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return true;
        }
        return false;
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static bool TryReportResult(string? resultFile, ApplyResult result)
    {
        if (string.IsNullOrWhiteSpace(resultFile)) return false;
        try
        {
            EnvironmentManager.WriteOperationResult(resultFile, result);
            return true;
        }
        catch { return false; }
    }

    private static void CaptureForm(Form form, string outputPath, int waitSeconds)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(40, 40);
        form.Show();
        form.Activate();
        DateTime until = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
        form.TopMost = true;
        form.BringToFront();
        form.Activate();
        Application.DoEvents();
        Thread.Sleep(120);
        using var bitmap = new Bitmap(form.Width, form.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(form.Left, form.Top, 0, 0, form.Size);
        bitmap.Save(outputPath, ImageFormat.Png);
        form.Close();
    }

    private static void PumpUi(int milliseconds)
    {
        DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }
}
