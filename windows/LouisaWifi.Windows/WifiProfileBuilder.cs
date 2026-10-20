using System.Text;
using System.Xml.Linq;
using LouisaWifi.Core;

namespace LouisaWifi.Windows;

public static class WifiProfileBuilder
{
    public static string Build(string name, string ssid, string password)
    {
        if (!CandidateSelector.ValidPassword(password)) throw new ArgumentException("密碼需為 8–63 個 ASCII 字元");
        XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
        XElement E(string key, object value) => new(ns + key, value);
        return new XElement(ns + "WLANProfile",
            E("name", name), E("SSIDConfig", E("SSID", new object[] {
                E("hex", Convert.ToHexString(Encoding.UTF8.GetBytes(ssid))), E("name", ssid) })),
            E("connectionType", "ESS"), E("connectionMode", "manual"),
            E("MSM", E("security", new object[] {
                E("authEncryption", new object[] { E("authentication", "WPA2PSK"), E("encryption", "AES"), E("useOneX", "false") }),
                E("sharedKey", new object[] { E("keyType", "passPhrase"), E("protected", "false"), E("keyMaterial", password) })
            }))).ToString(SaveOptions.DisableFormatting);
    }
}
