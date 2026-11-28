using DuckNote.Notes.Settings;

namespace DuckNote.Notes.Tests;

public class OfflinePortsSettingTests
{
    [Fact]
    public void The_offline_ports_setting_is_on_by_default_and_reaches_the_scan()
    {
        AppSettings settings = new();

        Assert.True(settings.CheckPortsWhenOffline);
        Assert.True(settings.ToScanOptions().ProbePortsWhenSilent);

        settings.CheckPortsWhenOffline = false;
        Assert.False(settings.ToScanOptions().ProbePortsWhenSilent);
    }

    [Fact]
    public void An_old_settings_file_without_the_setting_keeps_it_on()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ducknote-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "ScanDeadHosts": false }""");
            AppSettings loaded = AppSettings.Load(path);

            Assert.False(loaded.ScanDeadHosts);
            Assert.True(loaded.CheckPortsWhenOffline);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
