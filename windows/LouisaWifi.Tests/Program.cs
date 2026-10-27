using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using LouisaWifi.Core;
using LouisaWifi.Windows;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> run) => tests.Add((name, run));
void Expect(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
Store Store(string name, params string[] passwords) => new(name, "台北市", "", 25, 121, passwords);
var storeA = Store("A", "11111111", "22222222", "33333333");
var storeB = Store("B", "44444444", "55555555");
var ap = new AccessPoint(Guid.NewGuid(), CandidateSelector.Ssid, "aa:bb:cc:dd:ee:01", -40, WifiSecurity.Wpa2Personal);
StoreRepository Repository(params Store[] values) => new(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(values)));
(ConnectionCoordinator C, FakeWifi W, MemoryLearned L) Setup()
{
    var wifi = new FakeWifi(ap);
    var learned = new MemoryLearned();
    var c = new ConnectionCoordinator(wifi, learned, Repository(storeA, storeB), new FakeLocation(), new FakeConnectivity());
    c.SetEnabled(true);
    return (c, wifi, learned);
}

Test("原始門市資料可以載入，候選與座標有效", () => {
    using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "stores.json"));
    var repo = new StoreRepository(file);
    Expect(repo.All.Count > 400);
    Expect(repo.All.All(s => s.Passwords.Length is >= 1 and <= 3 && s.Passwords.All(CandidateSelector.ValidPassword)));
    Expect(repo.All.All(s => (s.Lat == 0 && s.Lng == 0) || (s.Lat is > 21 and < 26.5 && s.Lng is > 118 and < 122.5)));
});
Test("已知 BSSID 優先，附近門市按候選輪次排列", () => {
    var learned = new MemoryLearned(); learned.Remember(ap.Bssid, new("A", "88888888"));
    var candidates = CandidateSelector.Select([ap], learned, [storeA, storeB]);
    Expect(candidates.Select(c => c.Password).SequenceEqual(new[] { "88888888", "11111111", "44444444", "22222222", "55555555", "33333333" }));
});
Test("明確密碼不混入其他候選", () => {
    var candidates = CandidateSelector.Select([ap], new MemoryLearned(), [storeB], storeA, "12345678");
    Expect(candidates.Count == 1 && candidates[0].Password == "12345678");
});
Test("候選去重、限制次數且不處理其他 SSID", () => {
    Expect(CandidateSelector.Select([ap with { Ssid = "Other" }], new MemoryLearned(), [storeA]).Count == 0);
    var repeat = Store("X", "12345678", "12345678", "12345678");
    Expect(CandidateSelector.Select([ap], new MemoryLearned(), [repeat]).Count == 1);
    var stores = Enumerable.Range(0, 20).Select(i => Store(i.ToString(), i.ToString("D8"))).ToArray();
    Expect(CandidateSelector.Select([ap], new MemoryLearned(), stores).Count == 9);
});
Test("路易莎名稱支援包含比對、大小寫與原始名稱，保留完整 SSID", () => {
    foreach (var ssid in new[] { "LOUISA COFFEE2", "louisa coffee", "Store Louisa Coffee 5G", "LouisaCoffee", "louisacoffee2" })
    {
        var candidates = CandidateSelector.Select([ap with { Ssid = ssid }], new MemoryLearned(), [storeA]);
        Expect(candidates.Count > 0 && candidates.All(c => c.Target.Ssid == ssid));
    }
    foreach (var ssid in new[] { "", "Other", "Louisa", "Coffee", "Louisa Tea" })
        Expect(!CandidateSelector.IsLouisaSsid(ssid));
    Expect(CandidateSelector.Select([ap with { Ssid = "LOUISA COFFEE2", Security = WifiSecurity.Unsupported }], new MemoryLearned(), [storeA]).Count == 0);
});
Test("連線 Profile 保留完整的路易莎變體名稱", () => {
    const string ssid = "LOUISA COFFEE2";
    var xml = XDocument.Parse(WifiProfileBuilder.Build("test", ssid, "12345678", WifiSecurity.Wpa2Personal));
    XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
    Expect(xml.Descendants(ns + "SSID").Single().Element(ns + "name")!.Value == ssid);
    Expect(xml.Descendants(ns + "hex").Single().Value == Convert.ToHexString(Encoding.UTF8.GetBytes(ssid)));
});
AsyncTest("變體 SSID 可以連線、記錄並在下次自動連線使用", async () => {
    var (c, w, l) = Setup(); w.Aps = [ap with { Ssid = "LOUISA COFFEE2" }];
    w.OnJoin = (target, _, _) => Task.FromResult(new JoinResult(new(target.InterfaceId, target.Ssid, target.Bssid, "test"), ""));
    await c.CheckAsync(true, storeA);
    Expect(w.Joins == 1 && l.Find(ap.Bssid)?.StoreName == storeA.Name && c.Status.Contains("Wi-Fi 已連線"));
    w.Connections = [];
    await c.CheckAsync();
    Expect(w.Joins == 2 && c.Status.Contains("Wi-Fi 已連線"));
});
AsyncTest("已連線的變體 SSID 顯示狀態且不重新掃描", async () => {
    var (c, w, _) = Setup(); w.Connections = [new(ap.InterfaceId, "LOUISA COFFEE2", ap.Bssid, "test")];
    await c.CheckAsync();
    Expect(w.Scans == 0 && w.Joins == 0 && c.Status.Contains("LOUISA COFFEE2") && c.Status.Contains("可上網"));
});
AsyncTest("連上另一個符合名稱的 SSID 仍不記錄錯誤關聯", async () => {
    var (c, w, l) = Setup(); w.Aps = [ap with { Ssid = "LOUISA COFFEE2" }];
    w.OnJoin = (target, _, _) => Task.FromResult(new JoinResult(new(target.InterfaceId, "LOUISA COFFEE3", target.Bssid, "test"), ""));
    await c.CheckAsync(true, storeA);
    Expect(l.Records.Count == 0 && c.Status.Contains("未儲存"));
});
Test("距離排序排除未知與遠方座標", () => {
    var repo = Repository(storeA, storeB with { Lat = 0, Lng = 0 }, storeA with { Name = "遠方", Lat = 24 });
    Expect(repo.Nearby(new(25, 121, 50, DateTimeOffset.UtcNow)).Single().Name == "A");
    Expect(StoreRepository.Distance(25, 121, 25, 121) == 0);
});
Test("Profile XML 安全逸出密碼且為手動模式", () => {
    const string password = "a<&\"12345";
    var xml = XDocument.Parse(WifiProfileBuilder.Build("test", CandidateSelector.Ssid, password, WifiSecurity.Wpa2Personal));
    XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
    Expect(xml.Descendants(ns + "keyMaterial").Single().Value == password);
    Expect(xml.Descendants(ns + "connectionMode").Single().Value == "manual");
});
Test("Windows 驗證與加密代碼只接受可連線的 WPA2/WPA3 個人版 AES", () => {
    Expect(WifiSecurityMapper.FromNative(7, 4, true) == WifiSecurity.Wpa2Personal);
    Expect(WifiSecurityMapper.FromNative(9, 4, true) == WifiSecurity.Wpa3Personal);
    foreach (var auth in new uint[] { 1, 3, 4, 6, 8, 10, 11, 99 })
        Expect(WifiSecurityMapper.FromNative(auth, 4, true) == WifiSecurity.Unsupported);
    foreach (var auth in new uint[] { 7, 9 })
    {
        Expect(WifiSecurityMapper.FromNative(auth, 2, true) == WifiSecurity.Unsupported);
        Expect(WifiSecurityMapper.FromNative(auth, 4, false) == WifiSecurity.Unsupported);
    }
});
Test("WPA2/WPA3 Profile 使用對應驗證、AES 並安全逸出密碼", () => {
    XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
    foreach (var (security, authentication) in new[] { (WifiSecurity.Wpa2Personal, "WPA2PSK"), (WifiSecurity.Wpa3Personal, "WPA3SAE") })
    {
        const string password = "test<&12345";
        var xml = XDocument.Parse(WifiProfileBuilder.Build("test", "LOUISA COFFEE2", password, security));
        Expect(xml.Descendants(ns + "authentication").Single().Value == authentication);
        Expect(xml.Descendants(ns + "encryption").Single().Value == "AES");
        Expect(xml.Descendants(ns + "useOneX").Single().Value == "false");
        Expect(xml.Descendants(ns + "keyMaterial").Single().Value == password);
        Expect(xml.Descendants(ns + "connectionMode").Single().Value == "manual");
    }
    var rejected = false;
    try { WifiProfileBuilder.Build("test", "LOUISA COFFEE2", "12345678", WifiSecurity.Unsupported); }
    catch (ArgumentException) { rejected = true; }
    Expect(rejected);
});
AsyncTest("WPA3 安全性傳到連線目標且成功記錄可供自動連線", async () => {
    var (c, w, l) = Setup(); w.Aps = [ap with { Ssid = "LOUISA COFFEE2", Security = WifiSecurity.Wpa3Personal }];
    var securities = new List<WifiSecurity>();
    w.OnJoin = (target, _, _) => {
        securities.Add(target.Security);
        return Task.FromResult(new JoinResult(new(target.InterfaceId, target.Ssid, target.Bssid, "test"), ""));
    };
    await c.CheckAsync(true, storeA);
    Expect(l.Find(ap.Bssid)?.StoreName == storeA.Name && c.Status.Contains("Wi-Fi 已連線"));
    w.Connections = [];
    await c.CheckAsync();
    Expect(securities.SequenceEqual(new[] { WifiSecurity.Wpa3Personal, WifiSecurity.Wpa3Personal }));
});
Test("Native 結構大小與欄位偏移符合 Windows ABI", () => {
    Expect(Marshal.SizeOf<WlanNative.Ssid>() == 36);
    Expect(Marshal.SizeOf<WlanNative.InterfaceInfo>() == 532);
    Expect(Marshal.SizeOf<WlanNative.BssEntry>() == 360);
    Expect(Marshal.OffsetOf<WlanNative.BssEntry>(nameof(WlanNative.BssEntry.Timestamp)).ToInt32() == 72);
    Expect(Marshal.SizeOf<WlanNative.ConnectionAttributes>() == 604);
    Expect(Marshal.SizeOf<WlanNative.AvailableNetwork>() == 628);
});
AsyncTest("Native 中途失敗與過期事件不能提前結束本次連線", async () => {
    var pending = new PendingConnection(ap.InterfaceId, "unique-profile");
    pending.Accept(ap.InterfaceId, 10, "unique-profile", 0);
    Expect(!pending.Completion.IsCompleted);
    pending.Accept(ap.InterfaceId, 9, "unique-profile", 0);
    pending.Accept(ap.InterfaceId, 11, "unique-profile", 123);
    pending.Accept(ap.InterfaceId, 10, "old-profile", 0);
    pending.Accept(Guid.NewGuid(), 10, "unique-profile", 0);
    Expect(!pending.Completion.IsCompleted);
    pending.Accept(ap.InterfaceId, 10, "unique-profile", 0);
    Expect(await pending.Completion == 0 && !pending.External);
});
AsyncTest("Native 外部連線開始時停止本次操作", async () => {
    var pending = new PendingConnection(ap.InterfaceId, "unique-profile");
    pending.Accept(ap.InterfaceId, 9, "user-profile", 0);
    Expect(await pending.Completion == uint.MaxValue && pending.External);
});
AsyncTest("相同名稱的已儲存設定由使用者連線時仍停止本次操作", async () => {
    var pending = new PendingConnection(ap.InterfaceId, "LOUISA COFFEE2");
    pending.Accept(ap.InterfaceId, 9, "LOUISA COFFEE2", 0, 1);
    pending.Accept(ap.InterfaceId, 9, "LOUISA COFFEE2", 0, 0);
    pending.Accept(ap.InterfaceId, 10, "LOUISA COFFEE2", 0, 1);
    Expect(await pending.Completion == uint.MaxValue && pending.External);
});
AsyncTest("相同暫時設定的第二次連線開始不視為原本請求", async () => {
    var pending = new PendingConnection(ap.InterfaceId, "LOUISA COFFEE2");
    pending.Accept(ap.InterfaceId, 9, "LOUISA COFFEE2", 0);
    pending.Accept(ap.InterfaceId, 9, "LOUISA COFFEE2", 0);
    Expect(await pending.Completion == uint.MaxValue && pending.External);
});
AsyncTest("已有 Wi-Fi 時，自動和手動皆不掃描或連線", async () => {
    var (c, w, _) = Setup(); w.Connections = [new(ap.InterfaceId, "MyHotspot", ap.Bssid, "mine")];
    await c.CheckAsync(); await c.CheckAsync(true, storeA);
    Expect(w.Scans == 0 && w.Joins == 0);
});
AsyncTest("Windows 正在連線時不介入", async () => {
    var (c, w, _) = Setup(); w.Busy = true;
    await c.CheckAsync(true, storeA); Expect(w.Scans == 0 && w.Joins == 0);
});
AsyncTest("關閉自動連線後，計時器不進行操作", async () => {
    var (c, w, _) = Setup(); c.SetEnabled(false); await c.CheckAsync(); Expect(w.Scans == 0);
});
AsyncTest("只記錄成功 BSSID，不記附近其他基地台", async () => {
    var (c, w, l) = Setup(); w.Aps = [ap, ap with { Bssid = "aa:bb:cc:dd:ee:02", Signal = -45 }];
    w.OnJoin = (target, _, _) => Task.FromResult(new JoinResult(new(target.InterfaceId, target.Ssid, target.Bssid, "test"), ""));
    await c.CheckAsync(true, storeA);
    Expect(l.Records.Count == 1 && l.Find(ap.Bssid) is not null);
    Expect(c.Status.Contains("Wi-Fi 已連線") && c.Status.Contains("可上網"));
});
AsyncTest("實際基地台不同時，不記住錯誤關聯", async () => {
    var (c, w, l) = Setup();
    w.OnJoin = (target, _, _) => Task.FromResult(new JoinResult(new(target.InterfaceId, target.Ssid, "different", "test"), ""));
    await c.CheckAsync(true, storeA); Expect(l.Records.Count == 0);
});
AsyncTest("失敗後冷卻，自動檢查不再嘗試；手動可重試", async () => {
    var (c, w, l) = Setup(); l.Remember(ap.Bssid, new("A", "11111111"));
    await c.CheckAsync(); Expect(w.Joins == 1);
    await c.CheckAsync(); Expect(w.Joins == 1);
    await c.CheckAsync(true, storeA); Expect(w.Joins == 4);
});
AsyncTest("每次嘗試前重新保護使用者的新連線", async () => {
    var (c, w, _) = Setup();
    w.OnJoin = (_, _, _) => { w.Connections = [new(ap.InterfaceId, "Other", ap.Bssid, "other")]; return Task.FromResult(new JoinResult(null, "失敗")); };
    await c.CheckAsync(true, storeA); Expect(w.Joins == 1);
});
AsyncTest("拒絕存取明確提示權限，不當成沒有門市", async () => {
    var (c, w, _) = Setup(); w.ScanError = new UnauthorizedAccessException();
    await c.CheckAsync(true, storeA); Expect(c.Status.Contains("權限") && w.Joins == 0);
});
AsyncTest("重複事件不排隊；暫停取消正在等待的連線", async () => {
    var (c, w, l) = Setup(); l.Remember(ap.Bssid, new("A", "11111111"));
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    w.OnJoin = async (_, _, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return new(null, ""); };
    var first = c.CheckAsync(); await entered.Task;
    await c.CheckAsync(); c.SetEnabled(false); await first;
    Expect(w.Joins == 1 && c.Status.Contains("暫停"));
});
AsyncTest("新的手動選店會取代執行中的嘗試", async () => {
    var (c, w, _) = Setup();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    w.OnJoin = async (_, password, token) => {
        if (password == "11111111") { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
        return new(null, "失敗");
    };
    var first = c.CheckAsync(true, storeA); await entered.Task;
    await c.CheckAsync(true, storeB, "44444444"); await first;
    Expect(w.Joins == 2);
});
AsyncTest("狀態與錯誤不輸出手動密碼", async () => {
    var (c, _, _) = Setup(); var messages = new List<string>(); c.StatusChanged += messages.Add;
    await c.CheckAsync(true, storeA, "secret88"); Expect(messages.All(m => !m.Contains("secret88")));
});
AsyncTest("已知 BSSID 不等待定位", async () => {
    var wifi = new FakeWifi(ap); var learned = new MemoryLearned();
    learned.Remember(ap.Bssid, new("A", "11111111"));
    var location = new FakeLocation();
    var c = new ConnectionCoordinator(wifi, learned, Repository(storeA), location, new FakeConnectivity()) { UseLocation = true };
    c.SetEnabled(true); await c.CheckAsync();
    Expect(location.Calls == 0 && wifi.Joins == 1);
});
AsyncTest("未知 BSSID 不採用過期或精度不足的位置", async () => {
    foreach (var position in new[] { new Position(25, 121, 2000, DateTimeOffset.UtcNow), new Position(25, 121, 100, DateTimeOffset.UtcNow.AddHours(-1)) })
    {
        var wifi = new FakeWifi(ap);
        var c = new ConnectionCoordinator(wifi, new MemoryLearned(), Repository(storeA), new FakeLocation { Position = position }, new FakeConnectivity()) { UseLocation = true };
        c.SetEnabled(true); await c.CheckAsync();
        Expect(wifi.Joins == 0 && c.Status.Contains("手動選店"));
    }
});
AsyncTest("首次及既有連線都等上網確認完成才顯示已連線", async () => {
    foreach (var existing in new[] { false, true })
    {
        var wifi = new FakeWifi(ap);
        var connection = new Connection(ap.InterfaceId, ap.Ssid, ap.Bssid, "test");
        if (existing) wifi.Connections = [connection];
        wifi.OnJoin = (_, _, _) => Task.FromResult(new JoinResult(connection, ""));
        var ready = new TaskCompletionSource<InternetStatus>();
        var monitor = new FakeConnectivity { OnCheck = (target, token) => {
            Expect(target == connection);
            return ready.Task.WaitAsync(token);
        } };
        var c = new ConnectionCoordinator(wifi, new MemoryLearned(), Repository(storeA), new FakeLocation(), monitor);
        var messages = new List<string>(); c.StatusChanged += messages.Add;
        var run = c.CheckAsync(true, storeA);
        Expect(!run.IsCompleted && c.Status.StartsWith("正在連線") && messages.All(m => !m.Contains("已連線")));
        ready.SetResult(InternetStatus.Available);
        await run;
        Expect(c.Status.Contains("已連線") && c.Status.Contains("可上網"));
    }
});
AsyncTest("受限或未確認可上網不顯示連線成功", async () => {
    foreach (var result in new[] { InternetStatus.Unconfirmed, InternetStatus.Limited })
    {
        var wifi = new FakeWifi(ap) { Connections = [new(ap.InterfaceId, ap.Ssid, ap.Bssid, "test")] };
        var monitor = new FakeConnectivity { OnCheck = (_, _) => Task.FromResult(result) };
        var c = new ConnectionCoordinator(wifi, new MemoryLearned(), Repository(storeA), new FakeLocation(), monitor);
        await c.CheckAsync(true);
        Expect(!c.Status.Contains("已連線"));
        Expect(c.Status.Contains(result == InternetStatus.Limited ? "網路受限" : "尚未確認"));
        Expect(wifi.Joins == 0);
    }
});
AsyncTest("確認上網時切換網路不沿用成功結果", async () => {
    var wifi = new FakeWifi(ap) { Connections = [new(ap.InterfaceId, ap.Ssid, ap.Bssid, "test")] };
    var monitor = new FakeConnectivity { OnCheck = (_, _) => {
        wifi.Connections = [new(ap.InterfaceId, "Other", "other", "other")];
        return Task.FromResult(InternetStatus.Available);
    } };
    var c = new ConnectionCoordinator(wifi, new MemoryLearned(), Repository(storeA), new FakeLocation(), monitor);
    await c.CheckAsync(true);
    Expect(c.Status.Contains("已切換") && !c.Status.Contains("已連線"));
});
AsyncTest("等待上網確認可以取消且不顯示成功", async () => {
    var wifi = new FakeWifi(ap) { Connections = [new(ap.InterfaceId, ap.Ssid, ap.Bssid, "test")] };
    var monitor = new FakeConnectivity { OnCheck = async (_, token) => {
        await Task.Delay(Timeout.Infinite, token);
        return InternetStatus.Available;
    } };
    var c = new ConnectionCoordinator(wifi, new MemoryLearned(), Repository(storeA), new FakeLocation(), monitor);
    c.SetEnabled(true);
    var run = c.CheckAsync(true);
    c.Cancel(); await run;
    Expect(c.Status.Contains("已取消") && wifi.Connections.Count == 1);
});
Test("DPAPI 記錄重載成功、磁碟不含明文、損壞資料不覆寫", () => {
    var dir = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    var data = new LearnedNetworkStore(dir); data.Remember(ap.Bssid, new("A", "secret88"));
    Expect(new LearnedNetworkStore(dir).Find(ap.Bssid)?.Password == "secret88");
    var path = Path.Combine(dir, "learned.dpapi");
    Expect(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("secret88"));
    File.WriteAllBytes(path, [1, 2, 3]);
    var failed = false;
    try { _ = new LearnedNetworkStore(dir); } catch (System.Security.Cryptography.CryptographicException) { failed = true; }
    Expect(failed && File.ReadAllBytes(path).SequenceEqual(new byte[] { 1, 2, 3 }));
});

if (args.Contains("--native-smoke"))
    Test("Native WLAN handle 與網卡列舉（不掃描、不連線）", () => {
        WlanNative.Check(WlanNative.WlanOpenHandle(2, IntPtr.Zero, out _, out var handle));
        try
        {
            WlanNative.Check(WlanNative.WlanEnumInterfaces(handle, IntPtr.Zero, out var list));
            try { Console.WriteLine($"Native WLAN interfaces: {Marshal.ReadInt32(list)}"); }
            finally { WlanNative.WlanFreeMemory(list); }
        }
        finally { WlanNative.WlanCloseHandle(handle, IntPtr.Zero); }
    });
var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run().WaitAsync(TimeSpan.FromSeconds(15)); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed");
return failures == 0 ? 0 : 1;

sealed class MemoryLearned : ILearnedStore
{
    public Dictionary<string, LearnedNetwork> Records { get; } = new();
    public LearnedNetwork? Find(string bssid) => Records.GetValueOrDefault(bssid);
    public void Remember(string bssid, LearnedNetwork network) => Records[bssid] = network;
}
sealed class FakeLocation : ILocationService
{
    public int Calls;
    public Position? Position = new(25, 121, 100, DateTimeOffset.UtcNow);
    public Task<Position?> LocateAsync(CancellationToken token) { Calls++; return Task.FromResult(Position); }
}
sealed class FakeConnectivity : IConnectivityMonitor
{
    public Func<Connection, CancellationToken, Task<InternetStatus>>? OnCheck;
    public Task<InternetStatus> CheckAsync(Connection connection, CancellationToken token) =>
        OnCheck?.Invoke(connection, token) ?? Task.FromResult(InternetStatus.Available);
}
sealed class FakeWifi(AccessPoint ap) : IWifiClient
{
    public event Action? Changed { add { } remove { } }
    public IReadOnlyList<AccessPoint> Aps = [ap];
    public IReadOnlyList<Connection> Connections = [];
    public bool Busy;
    public int Scans, Joins;
    public Exception? ScanError;
    public Func<AccessPoint, string, CancellationToken, Task<JoinResult>>? OnJoin;
    public WifiSnapshot Snapshot() => new(true, Busy, Connections);
    public Task<IReadOnlyList<AccessPoint>> ScanAsync(CancellationToken token)
    { Scans++; if (ScanError is not null) throw ScanError; return Task.FromResult(Aps); }
    public async Task<JoinResult> JoinAsync(AccessPoint target, string password, CancellationToken token)
    {
        Joins++;
        var result = OnJoin is null ? new JoinResult(null, "驗證失敗") : await OnJoin(target, password, token);
        if (result.Connection is { } connection) Connections = [connection];
        return result;
    }
}
