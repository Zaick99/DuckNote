using DuckNote.Scan.Probes;

namespace DuckNote.Scan.Tests;

/// <summary>Le uscite dei comandi di sistema su macOS e Linux, lette come quelle di Windows.</summary>
public class OtherSystemsTests
{
    [Fact]
    public void Arp_tables_of_every_system_give_the_same_entries()
    {
        const string windows = "Interfaccia: 192.168.1.20 --- 0x5\r\n" +
                               "  Indirizzo Internet    Indirizzo fisico      Tipo\r\n" +
                               "  192.168.1.1           00-11-22-aa-bb-cc     dinamico\r\n";
        const string mac = "? (192.168.1.1) at 0:11:22:aa:bb:cc on en0 ifscope [ethernet]\n" +
                           "? (192.168.1.7) at (incomplete) on en0 ifscope [ethernet]\n" +
                           "? (192.168.1.9) at a:b:c:d:e:f on en0 ifscope [ethernet]\n";
        const string linux = "? (192.168.1.1) at 00:11:22:aa:bb:cc [ether] on eth0\n" +
                             "? (192.168.1.5) at <incomplete> on eth0\n";

        Assert.Equal([("192.168.1.1", "00:11:22:AA:BB:CC")], NeighbourCache.ParseTable(windows));
        Assert.Equal([("192.168.1.1", "00:11:22:AA:BB:CC"), ("192.168.1.9", "0A:0B:0C:0D:0E:0F")], NeighbourCache.ParseTable(mac));
        Assert.Equal([("192.168.1.1", "00:11:22:AA:BB:CC")], NeighbourCache.ParseTable(linux));
    }

    [Fact]
    public void Shares_listed_by_smbutil_and_smbclient_are_read()
    {
        const string smbutil = "Share                                           Type    Comments\n" +
                               "-------------------------------\n" +
                               "IPC$                                            Pipe    IPC Service (nas)\n" +
                               "Media                                           Disk    \n" +
                               "Backup                                          Disk    copie\n\n" +
                               "3 shares listed\n";
        const string smbclient = "Disk|Media|\nIPC|IPC$|IPC Service (nas)\nPrinter|Ufficio|stampante\n";

        Assert.Equal(["Media", "Backup"], SharesProbe.ReadShares(smbutil));
        Assert.Equal(["Media", "Ufficio"], SharesProbe.ReadShares(smbclient));
    }
}
