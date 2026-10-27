using System.Text.Json;

namespace LouisaWifi.Core;

public sealed record Store(string Name, string Address, string Phone, double Lat, double Lng, string[] Passwords)
{
    public string Display => $"{Name} — {Address}";
}
public sealed record Position(double Latitude, double Longitude, double Accuracy, DateTimeOffset Timestamp);
public enum WifiSecurity { Unsupported, Wpa2Personal, Wpa3Personal }
public sealed record AccessPoint(Guid InterfaceId, string Ssid, string Bssid, int Signal, WifiSecurity Security)
{
    public bool Supported => Security is WifiSecurity.Wpa2Personal or WifiSecurity.Wpa3Personal;
}
public sealed record Connection(Guid InterfaceId, string Ssid, string Bssid, string Profile);
public sealed record WifiSnapshot(bool HasAdapter, bool Busy, IReadOnlyList<Connection> Connections);
public sealed record LearnedNetwork(string StoreName, string Password);
public sealed record Candidate(AccessPoint Target, string StoreName, string Password);
public sealed record JoinResult(Connection? Connection, string Error);

public interface IWifiClient
{
    event Action? Changed;
    WifiSnapshot Snapshot();
    Task<IReadOnlyList<AccessPoint>> ScanAsync(CancellationToken token);
    Task<JoinResult> JoinAsync(AccessPoint target, string password, CancellationToken token);
}
public interface ILearnedStore
{
    LearnedNetwork? Find(string bssid);
    void Remember(string bssid, LearnedNetwork network);
}
public interface ILocationService
{
    Task<Position?> LocateAsync(CancellationToken token);
}
public enum InternetStatus { Unconfirmed, Limited, Available }
public interface IConnectivityMonitor
{
    Task<InternetStatus> CheckAsync(Connection connection, CancellationToken token);
}

public sealed class StoreRepository
{
    public IReadOnlyList<Store> All { get; }
    public StoreRepository(Stream json)
    {
        All = JsonSerializer.Deserialize<Store[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("門市資料無法讀取");
    }
    public IEnumerable<Store> Search(string text) => All.Where(s =>
        (s.Name + s.Address).Replace('臺', '台').Contains(text.Trim().Replace('臺', '台'), StringComparison.OrdinalIgnoreCase));
    public IEnumerable<Store> Nearby(Position p) => All.Where(s => s.Lat != 0 && s.Lng != 0)
        .Select(s => (Store: s, Distance: Distance(p.Latitude, p.Longitude, s.Lat, s.Lng)))
        .Where(x => x.Distance < 5000).OrderBy(x => x.Distance).Take(3).Select(x => x.Store);
    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * rad / 2), 2)
            + Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin((lon2 - lon1) * rad / 2), 2);
        return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}

public static class CandidateSelector
{
    public const string Ssid = "LouisaCoffee";
    public static bool IsLouisaSsid(string ssid) =>
        ssid.Contains("louisa coffee", StringComparison.OrdinalIgnoreCase)
        || ssid.Contains(Ssid, StringComparison.OrdinalIgnoreCase);
    public static bool ValidPassword(string value) => value.Length is >= 8 and <= 63 && value.All(c => c is >= ' ' and <= '~');
    public static IReadOnlyList<Candidate> Select(IReadOnlyList<AccessPoint> networks, ILearnedStore learned,
        IEnumerable<Store> nearby, Store? selected = null, string? password = null)
    {
        var aps = networks.Where(a => IsLouisaSsid(a.Ssid) && a.Supported).OrderByDescending(a => a.Signal).ToArray();
        if (aps.Length == 0) return [];
        var candidates = new List<Candidate>();
        if (password is not null && selected is not null)
            return ValidPassword(password) ? [new(aps[0], selected.Name, password)] : [];
        foreach (var ap in aps)
            if (learned.Find(ap.Bssid) is { } known && (selected is null || known.StoreName == selected.Name))
                candidates.Add(new(ap, known.StoreName, known.Password));
        var stores = selected is null ? nearby.ToArray() : [selected];
        for (var index = 0; index < 3; index++)
            foreach (var store in stores)
                if (index < store.Passwords.Length)
                    candidates.Add(new(aps[0], store.Name, store.Passwords[index]));
        return candidates.Where(c => ValidPassword(c.Password))
            .DistinctBy(c => (c.Target.Bssid, c.Password)).Take(9).ToArray();
    }
}
