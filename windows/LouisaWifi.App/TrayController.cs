using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LouisaWifi.Core;
using LouisaWifi.Windows;
using Forms = System.Windows.Forms;

namespace LouisaWifi.App;

public sealed class TrayController : IDisposable
{
    private readonly StoreRepository stores;
    private readonly AppSettings settings;
    private readonly LocationService location;
    private readonly ConnectionCoordinator coordinator;
    private readonly IWifiClient wifi;
    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon trayIcon;
    private readonly Forms.ToolStripMenuItem statusItem, autoItem, locationItem, startupItem;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private StorePickerWindow? window;
    private bool exiting;
    private DateTimeOffset lastEvent = DateTimeOffset.MinValue;
    public TrayController(StoreRepository stores, AppSettings settings, LocationService location, ConnectionCoordinator coordinator, IWifiClient wifi)
    {
        this.stores = stores; this.settings = settings; this.location = location; this.coordinator = coordinator; this.wifi = wifi;
        var menu = new Forms.ContextMenuStrip();
        statusItem = new Forms.ToolStripMenuItem("準備就緒") { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add("選擇門市／輸入密碼…", null, (_, _) => ShowStores());
        menu.Items.Add("立即檢查", null, async (_, _) => await Guard(async () => { if (Authorize()) await coordinator.CheckAsync(true); }));
        autoItem = new Forms.ToolStripMenuItem("啟用自動連線") { Checked = settings.AutoConnect && settings.Authorized };
        autoItem.Click += async (_, _) => await Guard(async () => {
            var enabled = !autoItem.Checked;
            if (enabled && !Authorize()) return;
            settings.AutoConnect = enabled; settings.Save(); autoItem.Checked = enabled;
            coordinator.SetEnabled(enabled);
            if (enabled) await coordinator.CheckAsync();
        });
        menu.Items.Add(autoItem);
        locationItem = new Forms.ToolStripMenuItem("使用定位尋找附近門市") { Checked = settings.UseLocation };
        locationItem.Click += async (_, _) => await Guard(async () => {
            var enabled = !locationItem.Checked;
            if (enabled && !await location.RequestAccessAsync()) { Open("ms-settings:privacy-location"); return; }
            settings.UseLocation = enabled; settings.Save(); locationItem.Checked = enabled; coordinator.UseLocation = enabled;
        });
        menu.Items.Add(locationItem);
        menu.Items.Add("Windows 定位權限設定…", null, (_, _) => Open("ms-settings:privacy-location"));
        startupItem = new Forms.ToolStripMenuItem("隨登入啟動") { Checked = StartupRegistration.Enabled };
        startupItem.Click += async (_, _) => await Guard(() => {
            StartupRegistration.Set(!startupItem.Checked); startupItem.Checked = StartupRegistration.Enabled; return Task.CompletedTask;
        });
        menu.Items.Add(startupItem);
        menu.Items.Add("取消本次嘗試", null, (_, _) => coordinator.Cancel());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("結束", null, async (_, _) => {
            exiting = true; timer.Stop(); coordinator.SetEnabled(false);
            await coordinator.WaitForIdleAsync();
            Application.Current.Shutdown();
        });
        using (var stream = typeof(TrayController).Assembly.GetManifestResourceStream("LouisaWifi.Louisa.ico")
            ?? throw new InvalidOperationException("Missing application icon."))
        using (var resourceIcon = new System.Drawing.Icon(stream))
            trayIcon = (System.Drawing.Icon)resourceIcon.Clone();
        icon = new Forms.NotifyIcon { Icon = trayIcon, Text = "Louisa Wi-Fi", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => ShowStores();
        coordinator.StatusChanged += OnStatus;
        coordinator.UseLocation = settings.UseLocation;
        coordinator.SetEnabled(autoItem.Checked);
        wifi.Changed += WifiChanged;
        timer.Tick += async (_, _) => await coordinator.CheckAsync();
        timer.Start();
        if (autoItem.Checked) _ = coordinator.CheckAsync();
    }
    private async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { OnStatus($"設定操作失敗（{ex.GetType().Name}）"); }
    }
    private bool Authorize()
    {
        if (settings.Authorized) return true;
        if (MessageBox.Show("只會嘗試 LouisaCoffee。請確認你已獲准使用門市 Wi-Fi。\nWindows 可能要求定位權限以掃描 Wi-Fi。", "啟用 Louisa Wi-Fi", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return false;
        settings.Authorized = true; settings.Save(); return true;
    }
    private void WifiChanged() => Application.Current.Dispatcher.BeginInvoke(async () => {
        if (exiting || DateTimeOffset.UtcNow - lastEvent < TimeSpan.FromSeconds(15)) return;
        lastEvent = DateTimeOffset.UtcNow;
        await coordinator.CheckAsync();
    });
    private void OnStatus(string status)
    {
        if (!Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.BeginInvoke(() => OnStatus(status)); return; }
        if (statusItem.Text != status)
        {
            LocalFiles.Log(status);
            if (status.StartsWith("Wi-Fi 已連線") || status.Contains("權限") || status.Contains("停止嘗試"))
                icon.ShowBalloonTip(5000, "Louisa Wi-Fi", status, Forms.ToolTipIcon.Info);
        }
        statusItem.Text = status;
        icon.Text = status.Length > 60 ? status[..60] : status;
        window?.SetStatus(status);
    }
    public void ShowStores()
    {
        if (window is null)
        {
            window = new StorePickerWindow(stores, settings.Authorized, async (store, password) => {
                settings.Authorized = true; settings.Save();
                await coordinator.CheckAsync(true, store, password);
            }, coordinator.Cancel);
            window.Closed += (_, _) => window = null;
        }
        window.SetStatus(coordinator.Status);
        window.Show(); window.Activate();
    }
    private static void Open(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { LocalFiles.Log($"開啟設定失敗：{ex.GetType().Name}"); }
    }
    public void Dispose()
    {
        exiting = true; timer.Stop(); wifi.Changed -= WifiChanged;
        coordinator.StatusChanged -= OnStatus; coordinator.Cancel();
        icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Dispose();
        trayIcon.Dispose();
    }
}
