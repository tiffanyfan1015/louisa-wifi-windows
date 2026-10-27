using LouisaWifi.Core;

namespace LouisaWifi.Windows;

internal static class WifiSecurityMapper
{
    // Native DOT11_AUTH_ALGORITHM: RSNA_PSK = 7, WPA3_SAE = 9.
    // DOT11_CIPHER_ALGO_CCMP = 4 (AES).
    internal static WifiSecurity FromNative(uint authentication, uint cipher, bool connectable) =>
        !connectable || cipher != 4 ? WifiSecurity.Unsupported : authentication switch
        {
            7 => WifiSecurity.Wpa2Personal,
            9 => WifiSecurity.Wpa3Personal,
            _ => WifiSecurity.Unsupported
        };
}
