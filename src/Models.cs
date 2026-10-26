using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Text;
namespace AsterVPN;
public sealed class ServerProfile : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public event PropertyChangedEventHandler? PropertyChanged;
    private string latency = "—";
    [JsonIgnore] public string Latency { get => latency; set { latency = value; PropertyChanged?.Invoke(this, new(nameof(Latency))); } }
    public string Name { get; set; } = "Сервер";
    public JsonObject Outbound { get; set; } = new();
    public bool IsEndpoint { get; set; }
    public bool UsesXray { get; set; }
    public string Protocol => Outbound["type"]?.GetValue<string>() ?? "?";
    public string Host => Outbound["server"]?.GetValue<string>() ?? "";
    public int Port => Outbound["server_port"]?.GetValue<int>() ?? 0;
    public string Label => $"{Name}  ·  {Protocol.ToUpperInvariant()}{(UsesXray ? " / XHTTP" : "")}";
    [JsonIgnore] public string DisplayName => System.Text.RegularExpressions.Regex.Replace(Name, @"^[\s\p{So}\p{Cs}]+", "");
    [JsonIgnore] public string ProtocolDetail => Protocol == "amneziawg" ? "AMNEZIAWG  ·  ОБФУСКАЦИЯ" : UsesXray ? "VLESS  ·  XHTTP  ·  " + (Outbound["xray"]?["streamSettings"]?["security"]?.ToString().ToUpperInvariant() ?? "") : Protocol.ToUpperInvariant() + (Outbound["tls"]?["reality"] != null ? "  ·  REALITY" : Outbound["tls"] != null ? "  ·  TLS" : "");
    [JsonIgnore] public string ProtocolColor => Protocol == "amneziawg" ? "#51C5B6" : "#BDD658";
    [JsonIgnore] public string Country => FlagCountry() is { Length: > 0 } code ? code : Name.Contains("🇩🇪") || Name.Contains("Герман", StringComparison.OrdinalIgnoreCase) || Name.Contains("Germany", StringComparison.OrdinalIgnoreCase) ? "DE" : Name.Contains("🇳🇱") || Name.Contains("Нидерланд", StringComparison.OrdinalIgnoreCase) || Name.Contains("Netherlands", StringComparison.OrdinalIgnoreCase) ? "NL" : Name.Contains("🇪🇸") || Name.Contains("Испан", StringComparison.OrdinalIgnoreCase) || Name.Contains("Spain", StringComparison.OrdinalIgnoreCase) ? "ES" : "";
    private string FlagCountry()
    {
        var runes = Name.EnumerateRunes().ToArray();
        for (var i = 0; i + 1 < runes.Length; i++)
            if (runes[i].Value is >= 0x1F1E6 and <= 0x1F1FF && runes[i + 1].Value is >= 0x1F1E6 and <= 0x1F1FF)
                return new string([(char)('A' + runes[i].Value - 0x1F1E6), (char)('A' + runes[i + 1].Value - 0x1F1E6)]);
        return "";
    }
    [JsonIgnore] public string FlagTop => Country switch { "RU" => "#FFFFFF", "BG" => "#FFFFFF", "EE" => "#4891D9", "DE" => "#171717", "NL" => "#E64657", "ES" => "#CE243C", _ => "#515D43" };
    [JsonIgnore] public string FlagMiddle => Country switch { "RU" => "#0039A6", "BG" => "#00966E", "EE" => "#171717", "DE" => "#D92939", "NL" => "#F1F2EA", "ES" => "#F8D541", _ => "#788B5A" };
    [JsonIgnore] public string FlagBottom => Country switch { "RU" => "#D52B1E", "BG" => "#D62612", "EE" => "#FFFFFF", "DE" => "#F9CC35", "NL" => "#3C69B2", "ES" => "#CE243C", _ => "#515D43" };
}
public sealed class Settings
{
    public List<SubscriptionProfile> Subscriptions { get; set; } = [];
    public string? SelectedServerId { get; set; }
    [JsonIgnore] public List<ServerProfile> AllServers => Subscriptions.SelectMany(s => s.Servers).ToList();
    public void MigrateSubscriptions()
    {
        if (Subscriptions.Count == 0 && (Servers.Count > 0 || Subscription.Length > 0))
        {
            SelectedServerId = Servers.ElementAtOrDefault(Selected)?.Id;
            Subscriptions.Add(new() { Url = Subscription, Name = Subscription.Length > 0 ? "Моя подписка" : "Локальные профили", Servers = Servers, LastUpdated = LastUpdated });
        }
        Servers = []; Subscription = ""; LastUpdated = null;
        SelectedServerId ??= AllServers.FirstOrDefault()?.Id;
    }
    public string Subscription { get; set; } = "";
    public List<ServerProfile> Servers { get; set; } = [];
    public int Selected { get; set; }
    public int AppMode { get; set; }
    public List<string> Apps { get; set; } = [];
    public string DirectCountries { get; set; } = "";
    public string DirectSites { get; set; } = "";
    public string VpnSites { get; set; } = "";
    public int? SiteMode { get; set; }
    public int? GeoMode { get; set; }
    [JsonIgnore] public int EffectiveSiteMode => SiteMode ?? (VpnSites.Length > 0 ? 1 : DirectSites.Length > 0 ? 2 : 0);
    [JsonIgnore] public int EffectiveGeoMode => GeoMode ?? (DirectCountries.Length > 0 ? 2 : 0);
    public bool BypassLan { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public bool KillSwitch { get; set; }
    public bool StartWithWindows { get; set; }
    public bool AutoConnect { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public DateTimeOffset? LastUpdated { get; set; }
    public string ProbeUrl { get; set; } = "https://www.gstatic.com/generate_204";
}
public sealed class SubscriptionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Name { get; set; } = "Подписка";
    public List<ServerProfile> Servers { get; set; } = [];
    public bool IsExpanded { get; set; } = true;
    public DateTimeOffset? LastUpdated { get; set; }
    public int UpdateIntervalHours { get; set; } = 6;
    public long? Upload { get; set; }
    public long? Download { get; set; }
    public long? Total { get; set; }
    public long? Expire { get; set; }
    public string Announcement { get; set; } = "";
    [JsonIgnore] public string UpdateText => (LastUpdated is { } date ? date.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "Ещё не обновлена") + $" · {Servers.Count} серверов · {UpdateIntervalHours} ч";
    [JsonIgnore] public string TrafficText => Upload.HasValue && Download.HasValue
        ? $"{Bytes((decimal)Upload.Value + Download.Value)} / {(Total == 0 ? "∞" : Total.HasValue ? Bytes(Total.Value) : "—")}" : "Трафик не указан провайдером";
    [JsonIgnore] public double TrafficPercent => Total > 0 && Upload.HasValue && Download.HasValue ? (double)Math.Clamp(((decimal)Upload.Value + Download.Value) / Total.Value * 100, 0, 100) : 0;
    [JsonIgnore] public string ExpiryText => Expire switch { null => "Срок не указан", <= 0 => "Без срока", _ => "До " + DateTimeOffset.FromUnixTimeSeconds(Expire.Value).ToLocalTime().ToString("dd.MM.yyyy") };
    private static string Bytes(decimal value) => value >= 1073741824 ? $"{value / 1073741824:0.##} ГБ" : $"{value / 1048576:0.##} МБ";
}
