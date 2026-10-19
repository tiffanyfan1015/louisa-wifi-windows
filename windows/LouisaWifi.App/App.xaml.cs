using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LouisaWifi.Core;
using LouisaWifi.Windows;

namespace LouisaWifi.App;

public partial class App : Application
{
    private Mutex? mutex;
    private WlanClient? wifi;
    private TrayController? tray;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
        {
            SmokeTest(e.Args[1]);
            return;
        }
        mutex = new Mutex(true, @"Local\LouisaWifi.Windows", out var created);
        if (!created) { MessageBox.Show("Louisa Wi-Fi 已在執行，請查看工作列右下角的系統匣。", "Louisa Wi-Fi"); Shutdown(); return; }
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("stores.json")))!;
            var stores = new StoreRepository(stream);
            var settings = AppSettings.Load();
            var learned = new LearnedNetworkStore();
            var location = new LocationService();
            wifi = new WlanClient();
            var coordinator = new ConnectionCoordinator(wifi, learned, stores, location, new ConnectivityMonitor());
            tray = new TrayController(stores, settings, location, coordinator, wifi);
            if (!settings.Authorized && !e.Args.Contains("--background")) tray.ShowStores();
        }
        catch (Exception ex)
        {
            LocalFiles.Log($"啟動失敗：{ex.GetType().Name}");
            MessageBox.Show($"無法啟動（{ex.GetType().Name}）。請確認 WLAN AutoConfig 服務已啟動。\n若設定或記錄損壞，請先備份 {LocalFiles.Root} 再移開損壞檔案。", "Louisa Wi-Fi");
            Shutdown(1);
        }
    }
    private void SmokeTest(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("stores.json")))!;
            var stores = new StoreRepository(stream);
            var view = new StorePickerWindow(stores, false, (_, _) => System.Threading.Tasks.Task.CompletedTask, () => { });
            view.PreparePreview();
            var content = (FrameworkElement)view.Content;
            content.Measure(new Size(680, 540));
            content.Arrange(new Rect(0, 0, 680, 540));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(680, 540, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen()) dc.DrawRectangle(view.Background, null, new Rect(0, 0, 680, 540));
            bitmap.Render(background);
            bitmap.Render(content);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(outputDirectory, "window.png"))) png.Save(output);
            File.WriteAllText(Path.Combine(outputDirectory, "smoke.txt"), $"PASS: WPF window initialized and rendered; {stores.All.Count} embedded stores loaded. No WLAN calls or user settings changed.");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputDirectory, "smoke.txt"), $"FAIL: {ex}");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        wifi?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
    }
}
