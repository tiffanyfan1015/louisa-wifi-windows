using System.Runtime.InteropServices;
using LouisaWifi.Core;
using static LouisaWifi.Windows.WlanNative;

namespace LouisaWifi.Windows;

public sealed class WlanClient : IWifiClient, IDisposable
{
    private IntPtr handle;
    private readonly Callback callback;
    private event Action<Guid, uint, string, uint>? NotificationReceived;
    public event Action? Changed;
    public WlanClient()
    {
        Check(WlanOpenHandle(2, IntPtr.Zero, out _, out handle));
        callback = OnNotification;
        try { Check(WlanRegisterNotification(handle, 8, false, callback, IntPtr.Zero, IntPtr.Zero, out _)); }
        catch { WlanCloseHandle(handle, IntPtr.Zero); handle = IntPtr.Zero; throw; }
    }
    private void OnNotification(ref Notification n, IntPtr context)
    {
        // Never let managed exceptions escape a native callback; copy only non-secret fields.
        try
        {
            if (n.Source != 8) return;
            string profile = "";
            uint reason = 0;
            if (n.Code is 9 or 10 or 11 or 20 or 21 && n.Data != IntPtr.Zero && n.Size >= 564)
            {
                profile = Marshal.PtrToStringUni(n.Data + 4, 256)?.TrimEnd('\0') ?? "";
                reason = unchecked((uint)Marshal.ReadInt32(n.Data, 560));
            }
            NotificationReceived?.Invoke(n.InterfaceId, n.Code, profile, reason);
            if (n.Code is 10 or 13 or 14 or 21)
                ThreadPool.QueueUserWorkItem(_ => { try { Changed?.Invoke(); } catch { /* Observer must not terminate process. */ } });
        }
        catch { /* Native callbacks must not throw. */ }
    }
    private List<InterfaceInfo> Interfaces()
    {
        Check(WlanEnumInterfaces(handle, IntPtr.Zero, out var ptr));
        try { return ReadList<InterfaceInfo>(ptr, 0, 8); }
        finally { WlanFreeMemory(ptr); }
    }
    private static List<T> ReadList<T>(IntPtr ptr, int countOffset, int start) where T : struct
    {
        var count = Marshal.ReadInt32(ptr, countOffset);
        if (count is < 0 or > 10000) throw new InvalidDataException();
        var result = new List<T>(count);
        var size = Marshal.SizeOf<T>();
        for (var i = 0; i < count; i++) result.Add(Marshal.PtrToStructure<T>(ptr + start + i * size));
        return result;
    }
    private Connection? Current(Guid id)
    {
        var code = WlanQueryInterface(handle, ref id, 7, IntPtr.Zero, out var size, out var ptr, out _);
        if (code == 5023) return null; // ERROR_INVALID_STATE: not associated.
        Check(code);
        try
        {
            if (size < Marshal.SizeOf<ConnectionAttributes>()) throw new InvalidDataException();
            var c = Marshal.PtrToStructure<ConnectionAttributes>(ptr);
            return c.State == 1 ? new(id, c.Association.Ssid.Text, Mac(c.Association.Bssid), c.Profile) : null;
        }
        finally { WlanFreeMemory(ptr); }
    }
    private static string Mac(byte[] bytes) => string.Join(":", bytes.Select(b => b.ToString("x2")));
    public WifiSnapshot Snapshot()
    {
        var interfaces = Interfaces();
        var connections = new List<Connection>();
        foreach (var i in interfaces.Where(i => i.State == 1))
            if (Current(i.Id) is { } c) connections.Add(c);
        // Connected interface with an unreadable/racing state is also protected.
        return new(interfaces.Count > 0, interfaces.Any(i => i.State is 1 or 2 or 5 or 6 or 7) && connections.Count == 0, connections);
    }
    public async Task<IReadOnlyList<AccessPoint>> ScanAsync(CancellationToken token)
    {
        var result = new List<AccessPoint>();
        foreach (var info in Interfaces().Where(i => i.State != 0))
        {
            token.ThrowIfCancellationRequested();
            var id = info.Id;
            var done = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Scanned(Guid adapter, uint code, string profile, uint reason)
            { if (adapter == id && code is 7 or 8) done.TrySetResult(code); }
            NotificationReceived += Scanned;
            try
            {
                Check(WlanScan(handle, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
                var status = await done.Task.WaitAsync(TimeSpan.FromSeconds(12), token);
                if (status == 8) continue;
                Check(WlanGetAvailableNetworkList(handle, ref id, 0, IntPtr.Zero, out var availablePtr));
                HashSet<string> supported;
                try { supported = ReadList<AvailableNetwork>(availablePtr, 0, 8)
                    .Where(n => n.Auth == 7 && n.Cipher == 4 && n.Connectable != 0).Select(n => n.Ssid.Text).ToHashSet(); }
                finally { WlanFreeMemory(availablePtr); }
                Check(WlanGetNetworkBssList(handle, ref id, IntPtr.Zero, 3, false, IntPtr.Zero, out var bssPtr));
                try
                {
                    var total = Marshal.ReadInt32(bssPtr);
                    var count = Marshal.ReadInt32(bssPtr, 4);
                    if (count < 0 || 8L + (long)count * Marshal.SizeOf<BssEntry>() > total) throw new InvalidDataException();
                    foreach (var b in ReadList<BssEntry>(bssPtr, 4, 8))
                        if (b.Ssid.Text == CandidateSelector.Ssid)
                            result.Add(new(id, b.Ssid.Text, Mac(b.Bssid), b.Rssi,
                                b.BssType == 1 && (b.Capability & 16) != 0 && supported.Contains(b.Ssid.Text)));
                }
                finally { WlanFreeMemory(bssPtr); }
            }
            finally { NotificationReceived -= Scanned; }
        }
        return result;
    }
    public async Task<JoinResult> JoinAsync(AccessPoint target, string password, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Snapshot() is { } snapshot && (snapshot.Busy || snapshot.Connections.Count != 0))
            return new(null, "已有其他連線活動");
        var id = target.InterfaceId;
        var profile = "LouisaWifi-" + Guid.NewGuid().ToString("N");
        var pending = new PendingConnection(id, profile);
        void Completed(Guid adapter, uint code, string name, uint reason)
        {
            pending.Accept(adapter, code, name, reason);
        }
        // DOT11_BSSID_LIST: NDIS_OBJECT_HEADER + two ULONGs + six-byte MAC (padded to 20).
        var bssids = Marshal.AllocHGlobal(20);
        Marshal.Copy(new byte[20], 0, bssids, 20);
        Marshal.WriteByte(bssids, 0, 0x80);
        Marshal.WriteByte(bssids, 1, 1);
        Marshal.WriteInt16(bssids, 2, 20);
        Marshal.WriteInt32(bssids, 4, 1);
        Marshal.WriteInt32(bssids, 8, 1);
        Marshal.Copy(Convert.FromHexString(target.Bssid.Replace(":", "")), 0, bssids + 12, 6);
        NotificationReceived += Completed;
        var requested = false;
        var completed = false;
        try
        {
            var parameters = new ConnectionParameters { Mode = 1, Profile = WifiProfileBuilder.Build(profile, target.Ssid, password), BssidList = bssids, BssType = 1 };
            Check(WlanConnect(handle, ref id, ref parameters, IntPtr.Zero));
            requested = true;
            var reason = await pending.Completion.WaitAsync(TimeSpan.FromSeconds(25), token);
            completed = true;
            if (pending.External) return new(null, "使用者或 Windows 已切換連線");
            if (reason != 0) return new(null, $"Wi-Fi 驗證失敗（代碼 {reason}）");
            var actual = Current(id);
            return actual is not null && actual.Profile == profile && actual.Ssid == target.Ssid && actual.Bssid == target.Bssid
                ? new(actual, "") : new(null, "連線結果與目標基地台不符");
        }
        catch (TimeoutException) { return new(null, "Wi-Fi 連線逾時"); }
        finally
        {
            // Cancel only our own request; never disconnect a different completed profile.
            if (requested && !completed && !pending.External)
            {
                try
                {
                    var current = Current(id);
                    if (current is null || current.Profile == profile) WlanDisconnect(handle, ref id, IntPtr.Zero);
                }
                catch { /* Preserve an unknown connection if ownership cannot be established. */ }
            }
            NotificationReceived -= Completed;
            Marshal.FreeHGlobal(bssids);
        }
    }
    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        WlanRegisterNotification(handle, 0, true, null, IntPtr.Zero, IntPtr.Zero, out _);
        WlanCloseHandle(handle, IntPtr.Zero);
        handle = IntPtr.Zero;
        GC.KeepAlive(callback);
    }
}
