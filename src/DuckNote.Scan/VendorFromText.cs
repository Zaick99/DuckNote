using System.Text.RegularExpressions;

namespace DuckNote.Scan;

public static class VendorFromText
{
    private static readonly (Regex Pattern, string Vendor)[] Catalogue =
    [
        (Make("mikrotik|routeros"),               "MikroTik"),
        (Make("ubiquiti|unifi|edgeos|airos"),     "Ubiquiti"),
        (Make("fritz!?box|avm"),                  "AVM"),
        (Make("synology|diskstation"),            "Synology"),
        (Make("qnap|turbonas"),                   "QNAP"),
        (Make("openwrt|lede"),                    "OpenWrt"),
        (Make("pfsense|opnsense|netgate"),        "Netgate"),
        (Make("cisco|ios-xe|nx-os"),              "Cisco"),
        (Make("juniper|junos"),                   "Juniper"),
        (Make("fortigate|fortios|fortinet"),      "Fortinet"),
        (Make("aruba|arubaos"),                   "Aruba"),
        (Make("zyxel"),                           "Zyxel"),
        (Make("tp-link|tplink|archer"),           "TP-Link"),
        (Make("netgear|readynas"),                "Netgear"),
        (Make("d-link|dlink"),                    "D-Link"),
        (Make("asuswrt|asus"),                    "ASUS"),
        (Make("hikvision|hikconnect"),            "Hikvision"),
        (Make("dahua"),                           "Dahua"),
        (Make("axis ?communication|axis camera"), "Axis"),
        (Make("hp ?(laserjet|officejet|ethernet)|hewlett"), "HP"),
        (Make("brother"),                         "Brother"),
        (Make("epson"),                           "Epson"),
        (Make("canon"),                           "Canon"),
        (Make("kyocera"),                         "Kyocera"),
        (Make("lexmark"),                         "Lexmark"),
        (Make("ricoh|aficio"),                    "Ricoh"),
        (Make("xerox|phaser"),                    "Xerox"),
        (Make("idrac|poweredge|dell"),            "Dell"),
        (Make("ilo |integrated lights-out"),      "HPE"),
        (Make("supermicro|megarac"),              "Supermicro"),
        (Make("vmware|esxi"),                     "VMware"),
        (Make("proxmox"),                         "Proxmox"),
        (Make("synapse|sonos"),                   "Sonos"),
        (Make("roku"),                            "Roku"),
        (Make("philips ?hue|hue bridge"),         "Philips"),
        (Make("shelly"),                          "Shelly"),
        (Make("tasmota|espressif|esp8266|esp32"), "Espressif"),
        (Make("raspbian|raspberry"),              "Raspberry Pi"),
        (Make("apple|airport|airplay"),           "Apple"),
        (Make("samsung"),                         "Samsung"),
        (Make("lg electronics|webos"),            "LG"),
        (Make("sony|bravia"),                     "Sony"),
        (Make("technicolor"),                     "Technicolor"),
        (Make("sagemcom"),                        "Sagemcom"),
        (Make("huawei|hicloud"),                  "Huawei"),
        (Make("zte "),                            "ZTE"),
        (Make("grandstream"),                     "Grandstream"),
        (Make("yealink"),                         "Yealink"),
        (Make("polycom|poly "),                   "Polycom")
    ];

    public static string Identify(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        foreach ((Regex pattern, string vendor) in Catalogue)
        {
            if (pattern.IsMatch(text))
            {
                return vendor;
            }
        }
        return string.Empty;
    }

    private static Regex Make(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
