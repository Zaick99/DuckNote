using DuckNote.Notes.Settings;

namespace DuckNote.Notes.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _appData = Path.Combine(Path.GetTempPath(), "ducknote-paths-" + Guid.NewGuid().ToString("N"));

    public AppPathsTests() => Directory.CreateDirectory(_appData);

    public void Dispose() => Directory.Delete(_appData, recursive: true);

    [Fact]
    public void Data_lives_under_duck_in_appdata()
    {
        Assert.Equal(Path.Combine(_appData, "duck", "DuckNote"), AppPaths.Resolve(_appData, null));
    }

    [Fact]
    public void Ducknote_home_wins()
    {
        Assert.Equal("/somewhere/else", AppPaths.Resolve(_appData, "/somewhere/else"));
    }

    [Fact]
    public void The_old_folder_is_moved_with_its_files()
    {
        string old = Path.Combine(_appData, "DuckNote");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "settings.json"), "{}");

        string folder = AppPaths.Resolve(_appData, null);

        Assert.Equal(Path.Combine(_appData, "duck", "DuckNote"), folder);
        Assert.True(File.Exists(Path.Combine(folder, "settings.json")));
        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void An_existing_new_folder_is_never_overwritten_by_the_old_one()
    {
        string old = Path.Combine(_appData, "DuckNote");
        string wanted = Path.Combine(_appData, "duck", "DuckNote");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(wanted);
        File.WriteAllText(Path.Combine(wanted, "settings.json"), "new");
        File.WriteAllText(Path.Combine(old, "settings.json"), "old");

        Assert.Equal(wanted, AppPaths.Resolve(_appData, null));
        Assert.Equal("new", File.ReadAllText(Path.Combine(wanted, "settings.json")));
        Assert.True(Directory.Exists(old));
    }
}
