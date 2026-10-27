namespace LouisaWifi.Core;

public sealed class ConnectionCoordinator(IWifiClient wifi, ILearnedStore learned, StoreRepository stores,
    ILocationService location, IConnectivityMonitor connectivity, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, DateTimeOffset> cooldown = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private CancellationTokenSource? active;
    private int requestVersion;
    public bool Enabled { get; private set; }
    public bool UseLocation { get; set; }
    public string Status { get; private set; } = "自動連線已暫停";
    public event Action<string>? StatusChanged;
    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        Cancel();
        Report(enabled ? "自動連線已啟用" : "自動連線已暫停");
    }
    public void Cancel() { requestVersion++; active?.Cancel(); }
    public async Task WaitForIdleAsync() { await gate.WaitAsync(); gate.Release(); }
    private void Report(string text) { Status = text; StatusChanged?.Invoke(text); }

    public async Task CheckAsync(bool manual = false, Store? selected = null, string? password = null)
    {
        if (!manual && !Enabled) return;
        if (manual) Cancel();
        var version = requestVersion;
        // Coalesce timer/native event bursts instead of queueing future attempts.
        if (manual) await gate.WaitAsync();
        else if (!await gate.WaitAsync(0)) return;
        if (version != requestVersion) { gate.Release(); return; }
        using var cts = new CancellationTokenSource();
        active = cts;
        try
        {
            var token = cts.Token;
            var state = wifi.Snapshot();
            if (!state.HasAdapter) { Report("找不到無線網卡，請確認 Wi-Fi 與 WLAN AutoConfig 服務"); return; }
            if (state.Connections.Count > 0)
            {
                if (state.Connections.FirstOrDefault(c => CandidateSelector.IsLouisaSsid(c.Ssid)) is { } current)
                    await ConfirmInternetAsync(current, learned.Find(current.Bssid)?.StoreName ?? current.Ssid, token);
                else Report("保留現有 Wi-Fi 連線；如需切換請先自行中斷");
                return;
            }
            if (state.Busy) { Report("Windows 正在處理 Wi-Fi 連線，稍後再檢查"); return; }
            Report("正在掃描 LouisaCoffee…");
            var aps = await wifi.ScanAsync(token);
            var targets = aps.Where(a => CandidateSelector.IsLouisaSsid(a.Ssid)).ToArray();
            if (targets.Length == 0) { Report("附近沒有 LouisaCoffee，或 Wi-Fi 尚未開啟"); return; }
            if (!targets.Any(a => a.Supported)) { Report("此網路安全性不支援或 Windows 回報無法連線；支援 WPA2/WPA3-Personal（AES）"); return; }
            if (!manual && targets.All(a => cooldown.TryGetValue(a.Bssid, out var until) && until > time.GetUtcNow()))
            { Report("連線嘗試已冷卻，稍後再檢查；也可手動選店重試"); return; }
            var eligible = manual ? targets : targets.Where(a => !cooldown.TryGetValue(a.Bssid, out var until) || until <= time.GetUtcNow()).ToArray();
            Position? here = null;
            if (selected is null && UseLocation && !eligible.Any(a => learned.Find(a.Bssid) is not null))
                here = await location.LocateAsync(token);
            var nearby = here is { Accuracy: <= 1000 } && time.GetUtcNow() - here.Timestamp <= TimeSpan.FromMinutes(10)
                ? stores.Nearby(here) : [];
            var candidates = CandidateSelector.Select(eligible, learned, nearby, selected, password);
            if (candidates.Count == 0)
            {
                Cool(targets);
                Report("無法辨識門市，請手動選店或輸入店內密碼（定位可能不可用）");
                return;
            }
            var lastError = "連線失敗";
            for (var i = 0; i < candidates.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                // Recheck before every attempt: the user or Windows may have connected meanwhile.
                state = wifi.Snapshot();
                if (state.Connections.Count > 0 || state.Busy) { Report("偵測到其他連線活動，停止嘗試"); return; }
                var attempt = candidates[i];
                Report($"正在連線：{attempt.StoreName}（{i + 1}/{candidates.Count}）");
                var result = await wifi.JoinAsync(attempt.Target, attempt.Password, token);
                token.ThrowIfCancellationRequested();
                if (result.Connection is { } connected)
                {
                    if (connected.Ssid != attempt.Target.Ssid || !connected.Bssid.Equals(attempt.Target.Bssid, StringComparison.OrdinalIgnoreCase))
                    { Report("基地台已變更，未儲存門市記錄"); return; }
                    learned.Remember(connected.Bssid, new(attempt.StoreName, attempt.Password));
                    cooldown.Remove(connected.Bssid);
                    await ConfirmInternetAsync(connected, attempt.StoreName, token);
                    return;
                }
                lastError = result.Error;
            }
            Cool(targets);
            Report($"{lastError}；停止嘗試 10 分鐘，可手動選店重試");
        }
        catch (OperationCanceledException) { Report(Enabled ? "已取消本次連線" : "自動連線已暫停"); }
        catch (UnauthorizedAccessException) { Report("缺少 Wi-Fi／定位權限，請開啟 Windows 定位設定"); }
        catch (TimeoutException) { Report("Wi-Fi 掃描逾時，請確認網卡狀態後手動重試"); }
        catch (System.ComponentModel.Win32Exception ex) { Report($"Windows Wi-Fi API 失敗（代碼 {ex.NativeErrorCode}），請確認無線網卡與 WLAN AutoConfig 服務"); }
        catch (Exception ex) { Report($"操作失敗（{ex.GetType().Name}），請確認網卡、權限與資料檔案"); }
        finally { active = null; gate.Release(); }
    }
    private async Task ConfirmInternetAsync(Connection connection, string name, CancellationToken token)
    {
        Report($"正在連線：{name}（確認網際網路中…）");
        var status = await connectivity.CheckAsync(connection, token);
        token.ThrowIfCancellationRequested();
        if (!wifi.Snapshot().Connections.Any(c => c.InterfaceId == connection.InterfaceId
            && c.Ssid == connection.Ssid && c.Bssid.Equals(connection.Bssid, StringComparison.OrdinalIgnoreCase)))
        { Report("連線已中斷或已切換，未確認上網狀態"); return; }
        Report(status switch
        {
            InternetStatus.Available => $"Wi-Fi 已連線：{name}；可上網",
            InternetStatus.Limited => $"網路受限：{name}；可能需要開啟瀏覽器登入",
            _ => $"尚未確認可上網：{name}；請稍後重試或檢查門市網路"
        });
    }
    private void Cool(IEnumerable<AccessPoint> aps)
    {
        foreach (var ap in aps) cooldown[ap.Bssid] = time.GetUtcNow().AddMinutes(10);
    }
}
