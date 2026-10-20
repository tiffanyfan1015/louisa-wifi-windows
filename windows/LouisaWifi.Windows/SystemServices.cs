using Microsoft.Win32;
using Windows.Devices.Geolocation;
using Windows.Networking.Connectivity;
using LouisaWifi.Core;

namespace LouisaWifi.Windows;

public sealed class LocationService : ILocationService
{
    public async Task<bool> RequestAccessAsync() => await Geolocator.RequestAccessAsync() == GeolocationAccessStatus.Allowed;
    public async Task<Position?> LocateAsync(CancellationToken token)
    {
        try
        {
            var locator = new Geolocator { DesiredAccuracyInMeters = 100 };
            var p = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8)).AsTask(token);
            return new(p.Coordinate.Point.Position.Latitude, p.Coordinate.Point.Position.Longitude, p.Coordinate.Accuracy, p.Coordinate.Timestamp);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or TimeoutException)
        { return null; }
    }
}

public sealed class ConnectivityMonitor : IConnectivityMonitor
{
    public Task<string> CheckAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // OS-wide assessment can include Ethernet or VPN; do not claim this proves Wi-Fi internet access.
        var level = NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel();
        return Task.FromResult(level switch {
            NetworkConnectivityLevel.InternetAccess => "系統顯示網際網路可用",
            NetworkConnectivityLevel.ConstrainedInternetAccess => "網路受限，可能需要網頁登入",
            _ => "系統尚未確認網際網路可用"
        });
    }
}

public static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "LouisaWifi";
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(Name) is string; }
    }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, true);
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException();
            key.SetValue(Name, $"\"{path}\" --background");
        }
        else key.DeleteValue(Name, false);
    }
}
