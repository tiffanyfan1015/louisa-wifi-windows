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
    public async Task<InternetStatus> CheckAsync(Connection connection, CancellationToken token)
    {
        // Wait up to 30 seconds for this Wi-Fi's IP/connectivity state, not Ethernet or VPN.
        for (var attempt = 0; attempt <= 15; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var profile = NetworkInformation.GetConnectionProfiles().FirstOrDefault(p =>
                p.IsWlanConnectionProfile && p.NetworkAdapter?.NetworkAdapterId == connection.InterfaceId
                && p.WlanConnectionProfileDetails.GetConnectedSsid() == connection.Ssid);
            var level = profile?.GetNetworkConnectivityLevel();
            if (level == NetworkConnectivityLevel.InternetAccess) return InternetStatus.Available;
            if (level == NetworkConnectivityLevel.ConstrainedInternetAccess) return InternetStatus.Limited;
            if (attempt < 15) await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        return InternetStatus.Unconfirmed;
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
