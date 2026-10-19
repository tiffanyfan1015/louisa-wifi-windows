using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LouisaWifi.Windows;

internal static class WlanNative
{
    internal static void Check(uint code)
    {
        if (code == 5) throw new UnauthorizedAccessException();
        if (code != 0) throw new Win32Exception((int)code);
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;
        public readonly string Text => Encoding.UTF8.GetString(Bytes, 0, (int)Math.Min(Length, 32));
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct InterfaceInfo
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public uint State;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Association
    {
        public Ssid Ssid;
        public uint BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public uint PhyType, PhyIndex, Quality, RxRate, TxRate;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ConnectionAttributes
    {
        public uint State, Mode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
        public Association Association;
        public uint Security, OneX, Auth, Cipher;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct BssEntry
    {
        public Ssid Ssid;
        public uint PhyId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public uint BssType, PhyType;
        public int Rssi;
        public uint Quality;
        public byte InRegDomain;
        public ushort BeaconPeriod;
        public ulong Timestamp, HostTimestamp;
        public ushort Capability;
        public uint Frequency, RateLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)] public ushort[] Rates;
        public uint IeOffset, IeSize;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct AvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
        public Ssid Ssid;
        public uint BssType, BssidCount, Connectable, Reason, PhyCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
        public uint MorePhyTypes, Quality, Security, Auth, Cipher, Flags, Reserved;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ConnectionParameters
    {
        public uint Mode;
        [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
        public IntPtr Ssid, BssidList;
        public uint BssType, Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Notification
    {
        public uint Source, Code;
        public Guid InterfaceId;
        public uint Size;
        public IntPtr Data;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void Callback(ref Notification notification, IntPtr context);

    [DllImport("wlanapi.dll")] internal static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] internal static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] internal static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("wlanapi.dll")] internal static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] internal static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, uint opcode, IntPtr reserved, out uint size, out IntPtr data, out uint type);
    [DllImport("wlanapi.dll")] internal static extern uint WlanScan(IntPtr handle, ref Guid id, IntPtr ssid, IntPtr ie, IntPtr reserved);
    [DllImport("wlanapi.dll")] internal static extern uint WlanGetNetworkBssList(IntPtr handle, ref Guid id, IntPtr ssid, uint bssType, [MarshalAs(UnmanagedType.Bool)] bool secure, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] internal static extern uint WlanGetAvailableNetworkList(IntPtr handle, ref Guid id, uint flags, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] internal static extern uint WlanConnect(IntPtr handle, ref Guid id, ref ConnectionParameters parameters, IntPtr reserved);
    [DllImport("wlanapi.dll")] internal static extern uint WlanDisconnect(IntPtr handle, ref Guid id, IntPtr reserved);
    [DllImport("wlanapi.dll")] internal static extern uint WlanRegisterNotification(IntPtr handle, uint source, [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicates, Callback? callback, IntPtr context, IntPtr reserved, out uint previous);
}
