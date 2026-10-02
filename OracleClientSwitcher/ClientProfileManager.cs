using System.Text.Json;

namespace OracleClientSwitcher;

internal static class ClientProfileManager
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OracleClientSwitcher");
    private static readonly string SettingsFile = Path.Combine(DataDirectory, "client-profiles.json");
    private static readonly object SyncRoot = new();

    public static ClientProfileSettings Load()
    {
        lock (SyncRoot)
        {
            try
            {
                if (!File.Exists(SettingsFile)) return new ClientProfileSettings();
                ClientProfileSettings settings = JsonSerializer.Deserialize<ClientProfileSettings>(File.ReadAllText(SettingsFile)) ?? new();
                settings.Profiles ??= new();
                return settings;
            }
            catch (Exception ex)
            {
                AppLog.Warning("客户端", "读取收藏设置", ex.Message);
                return new ClientProfileSettings();
            }
        }
    }

    public static OracleClientProfile GetProfile(ClientProfileSettings settings, string path)
    {
        string normalized = OracleScanner.Normalize(path);
        OracleClientProfile? profile = settings.Profiles.FirstOrDefault(x =>
            string.Equals(OracleScanner.Normalize(x.Path), normalized, StringComparison.OrdinalIgnoreCase));
        if (profile is not null) return profile;
        profile = new OracleClientProfile { Path = normalized };
        settings.Profiles.Add(profile);
        return profile;
    }

    public static TnsOperationResult Save(ClientProfileSettings settings)
    {
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                settings.Profiles = settings.Profiles
                    .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                    .GroupBy(x => OracleScanner.Normalize(x.Path), StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Last())
                    .ToList();
                string temporary = SettingsFile + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, SettingsFile, true);
                return new TnsOperationResult { Success = true, Message = "客户端收藏和排序设置已保存。" };
            }
            catch (Exception ex)
            {
                AppLog.Error("客户端", "保存收藏设置", ex);
                return new TnsOperationResult { Success = false, Message = "保存客户端设置失败：" + ex.Message };
            }
        }
    }

    public static string GetDisplayName(ClientProfileSettings settings, OracleClientInfo client)
    {
        OracleClientProfile? profile = settings.Profiles.FirstOrDefault(x =>
            string.Equals(OracleScanner.Normalize(x.Path), OracleScanner.Normalize(client.PathDirectory), StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(profile?.DisplayName) ? $"Oracle {client.Version}" : profile.DisplayName.Trim();
    }

    public static IEnumerable<OracleClientInfo> SortAndFilter(IEnumerable<OracleClientInfo> clients, ClientProfileSettings settings)
    {
        IEnumerable<OracleClientInfo> visible = settings.ShowHidden
            ? clients
            : clients.Where(client => !GetProfile(settings, client.PathDirectory).Hidden);
        return settings.SortMode switch
        {
            ClientSortMode.Name => visible.OrderBy(x => GetDisplayName(settings, x), StringComparer.CurrentCultureIgnoreCase),
            ClientSortMode.Version => visible.OrderByDescending(x => ParseVersion(x.Version)).ThenBy(x => x.PathDirectory, StringComparer.OrdinalIgnoreCase),
            ClientSortMode.RecentlySelected => visible.OrderByDescending(x => GetProfile(settings, x.PathDirectory).LastSelectedAt ?? DateTime.MinValue),
            _ => visible.OrderByDescending(x => GetProfile(settings, x.PathDirectory).Favorite)
                .ThenByDescending(x => ParseVersion(x.Version))
                .ThenBy(x => x.PathDirectory, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static Version ParseVersion(string value)
    {
        string normalized = string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries).Take(4));
        return Version.TryParse(normalized, out Version? version) ? version : new Version();
    }
}
