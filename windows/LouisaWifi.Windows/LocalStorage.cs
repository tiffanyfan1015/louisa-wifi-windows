using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using LouisaWifi.Core;

namespace LouisaWifi.Windows;

public static class LocalFiles
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LouisaWifi");
    public static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, true);
    }
    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, "app.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class AppSettings
{
    public bool AutoConnect { get; set; }
    public bool UseLocation { get; set; }
    public static AppSettings Load()
    {
        var path = Path.Combine(LocalFiles.Root, "settings.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(path)) ?? new();
    }
    public void Save() => LocalFiles.AtomicWrite(Path.Combine(LocalFiles.Root, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(this));
}

public sealed class LearnedNetworkStore : ILearnedStore
{
    private readonly string path;
    private readonly Dictionary<string, LearnedNetwork> records;
    public LearnedNetworkStore(string? directory = null)
    {
        path = Path.Combine(directory ?? LocalFiles.Root, "learned.dpapi");
        if (!File.Exists(path)) { records = new(StringComparer.OrdinalIgnoreCase); return; }
        var bytes = Dpapi.Transform(File.ReadAllBytes(path), false);
        try
        {
            records = new(JsonSerializer.Deserialize<Dictionary<string, LearnedNetwork>>(bytes)
                ?? throw new InvalidDataException(), StringComparer.OrdinalIgnoreCase);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public LearnedNetwork? Find(string bssid) => records.GetValueOrDefault(bssid);
    public void Remember(string bssid, LearnedNetwork network)
    {
        var next = new Dictionary<string, LearnedNetwork>(records, StringComparer.OrdinalIgnoreCase) { [bssid] = network };
        var plain = JsonSerializer.SerializeToUtf8Bytes(next);
        try { LocalFiles.AtomicWrite(path, Dpapi.Transform(plain, true)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        records[bssid] = network;
    }
}

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static byte[] Transform(byte[] input, bool protect)
    {
        var blob = new Blob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(input, 0, blob.Data, input.Length);
            var success = protect ? CryptProtectData(ref blob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException("無法使用目前 Windows 使用者解密或加密記錄");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[input.Length], 0, blob.Data, input.Length);
            Marshal.FreeHGlobal(blob.Data);
            if (output.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size);
                LocalFree(output.Data);
            }
        }
    }
}
