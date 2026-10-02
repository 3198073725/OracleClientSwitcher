namespace OracleClientSwitcher;

internal sealed class OracleClientInfo
{
    public required string PathDirectory { get; init; }
    public required string OracleHome { get; init; }
    public required string Version { get; init; }
    public required string Architecture { get; init; }
    public required string ClientType { get; init; }
    public required string TnsAdmin { get; init; }
    public bool HasSqlPlus { get; init; }
    public bool IsLite { get; init; }
    public string DiscoverySource { get; init; } = "指定目录";
    public string OciPath => Path.Combine(PathDirectory, "oci.dll");
    public string GeneziPath => Path.Combine(PathDirectory, "genezi.exe");
    public string SqlPlusPath => Path.Combine(PathDirectory, "sqlplus.exe");
}

internal enum OracleScanMode
{
    Quick,
    Standard,
    Full
}

internal sealed class OracleScanSettings
{
    public OracleScanMode Mode { get; set; } = OracleScanMode.Standard;
    public List<string> SelectedDriveRoots { get; set; } = new();
    public List<string> CustomDirectories { get; set; } = new();
    public List<string> ExcludedDirectories { get; set; } = new();
    public bool UseCache { get; set; } = true;
    public int CacheHours { get; set; } = 24;
}

internal sealed class OracleScanCache
{
    public DateTime CreatedAt { get; set; }
    public List<OracleClientInfo> Clients { get; set; } = new();
}

internal sealed class EnvironmentBackup
{
    public DateTime CreatedAt { get; set; }
    public string Scope { get; set; } = "User";
    public string? PathValue { get; set; }
    public string? OracleHome { get; set; }
    public string? TnsAdmin { get; set; }
}

internal sealed class ApplyResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? BackupPath { get; init; }
    public bool RolledBack { get; init; }
}

internal sealed class EnvironmentChangeItem
{
    public string Item { get; init; } = string.Empty;
    public string CurrentValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
}

internal sealed class EnvironmentChangePreview
{
    public required OracleClientInfo Client { get; init; }
    public EnvironmentVariableTarget Target { get; init; }
    public IReadOnlyList<EnvironmentChangeItem> Changes { get; init; } = Array.Empty<EnvironmentChangeItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool HasChanges => Changes.Any(x => !string.Equals(x.CurrentValue, x.NewValue, StringComparison.Ordinal));
}

internal sealed class EnvironmentBackupRecord
{
    public required string FilePath { get; init; }
    public required EnvironmentBackup Backup { get; init; }
}

internal enum VerificationSeverity
{
    Success,
    Info,
    Warning,
    Error
}

internal sealed class ClientVerificationItem
{
    public string Category { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public VerificationSeverity Severity { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

internal sealed class OciProbeResult
{
    public bool Success { get; init; }
    public int ErrorCode { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed class ExternalProcessResult
{
    public bool Started { get; init; }
    public bool TimedOut { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

internal enum TnsIssueSeverity
{
    Info,
    Warning,
    Error
}

internal sealed class TnsValidationIssue
{
    public TnsIssueSeverity Severity { get; init; }
    public int Line { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed class TnsAliasEntry
{
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    public string Text { get; init; } = string.Empty;
    public int StartLine { get; init; }
}

internal sealed class TnsServiceDefinition
{
    public required TnsAliasEntry SourceEntry { get; init; }
    public string Alias { get; init; } = string.Empty;
    public string Protocol { get; init; } = "TCP";
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 1521;
    public string ConnectName { get; init; } = string.Empty;
    public bool UsesSid { get; init; }
    public bool IsSimpleEditable { get; init; }
    public string Limitation { get; init; } = string.Empty;
    public IReadOnlyList<TnsEndpoint> Endpoints { get; init; } = Array.Empty<TnsEndpoint>();
    public bool Failover { get; init; }
    public bool LoadBalance { get; init; }
    public int ConnectTimeoutSeconds { get; init; }
    public int RetryCount { get; init; }
    public string WalletDirectory { get; init; } = string.Empty;
}

internal sealed class TnsEndpoint
{
    public string Protocol { get; set; } = "TCP";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1521;
}

internal sealed class TnsAdvancedDefinition
{
    public string Alias { get; set; } = string.Empty;
    public List<TnsEndpoint> Endpoints { get; set; } = new();
    public string ConnectName { get; set; } = string.Empty;
    public bool UsesSid { get; set; }
    public bool Failover { get; set; }
    public bool LoadBalance { get; set; }
    public int ConnectTimeoutSeconds { get; set; }
    public int RetryCount { get; set; }
    public string WalletDirectory { get; set; } = string.Empty;
}

internal sealed class TnsOperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? BackupPath { get; init; }
}

internal sealed class OracleSwitcherSettings
{
    public bool UseSharedTnsDirectory { get; set; }
    public string? SharedTnsDirectory { get; set; }
}

internal sealed class OracleClientProfile
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Favorite { get; set; }
    public bool Hidden { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime? LastSelectedAt { get; set; }
}

internal enum ClientSortMode
{
    Favorite,
    Version,
    Name,
    RecentlySelected
}

internal sealed class ClientProfileSettings
{
    public List<OracleClientProfile> Profiles { get; set; } = new();
    public ClientSortMode SortMode { get; set; } = ClientSortMode.Favorite;
    public bool ShowHidden { get; set; }
    public string LastSelectedPath { get; set; } = string.Empty;
}

internal enum AppLogLevel
{
    Information,
    Warning,
    Error
}

internal sealed class AppLogEntry
{
    public DateTime Timestamp { get; set; }
    public AppLogLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

internal sealed class CompatibilityCheckResult
{
    public bool Supported { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

internal sealed class IsolatedLaunchProfile
{
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool SetOracleHome { get; set; }
    public DateTime LastUsedAt { get; set; }
}

internal sealed class IsolatedLaunchPlan
{
    public required OracleClientInfo Client { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string PathValue { get; init; } = string.Empty;
    public string TnsAdmin { get; init; } = string.Empty;
    public string? OracleHome { get; init; }
    public string TargetArchitecture { get; init; } = "未知";
    public bool ArchitectureCompatible { get; init; }
    public bool ArchitectureKnown { get; init; }
}

internal sealed class IsolatedLaunchResult
{
    public bool Success { get; init; }
    public int? ProcessId { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal enum OracleEnvironmentState
{
    Active,
    Invalid,
    NotConfigured
}

internal enum EnvironmentIssueSeverity
{
    Error,
    Warning,
    Info
}

internal sealed class EnvironmentIssue
{
    public EnvironmentIssueSeverity Severity { get; init; }
    public string Item { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string CurrentValue { get; init; } = string.Empty;
    public string Problem { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}

internal sealed class OracleEnvironmentStatus
{
    public OracleEnvironmentState State { get; init; }
    public OracleClientInfo? ActiveClient { get; init; }
    public OracleClientInfo? SuggestedClient { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string? ConfiguredPath { get; init; }
    public bool HasWarnings { get; init; }
    public IReadOnlyList<EnvironmentIssue> Issues { get; init; } = Array.Empty<EnvironmentIssue>();
}
