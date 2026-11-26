using System.Net;
using System.Text;
using DuckNote.Notes.Updates;
using Xunit;

namespace DuckNote.Notes.Tests;

public class UpdateFeedTests
{
    private const string Release = """
        {
          "tag_name": "v0.0.18",
          "name": "DuckNote 0.0.18",
          "draft": false,
          "prerelease": false,
          "html_url": "https://github.com/Zaick99/DuckNote/releases/tag/v0.0.18",
          "assets": [
            { "name": "DuckNote.exe", "size": 66742514, "browser_download_url": "https://github.com/Zaick99/DuckNote/releases/download/v0.0.18/DuckNote.exe" },
            { "name": "DuckNote-0.0.18-setup.exe", "size": 62173070, "browser_download_url": "https://github.com/Zaick99/DuckNote/releases/download/v0.0.18/DuckNote-0.0.18-setup.exe" },
            { "name": "DuckNote-0.0.18-macos-arm64.dmg", "size": 48503262, "browser_download_url": "https://github.com/Zaick99/DuckNote/releases/download/v0.0.18/DuckNote-0.0.18-macos-arm64.dmg" },
            { "name": "DuckNote-0.0.18-macos-x64.dmg", "size": 50576094, "browser_download_url": "https://github.com/Zaick99/DuckNote/releases/download/v0.0.18/DuckNote-0.0.18-macos-x64.dmg" },
            { "name": "SHA256SUMS.txt", "size": 369, "browser_download_url": "https://github.com/Zaick99/DuckNote/releases/download/v0.0.18/SHA256SUMS.txt" },
            { "name": "altro.exe", "size": 1, "browser_download_url": "http://example.com/altro.exe" }
          ]
        }
        """;

    [Fact]
    public void The_latest_release_is_read_with_its_files()
    {
        ReleaseInfo release = Assert.IsType<ReleaseInfo>(UpdateFeed.Parse(Release));

        Assert.Equal(new Version(0, 0, 18), release.Version);
        Assert.Equal("v0.0.18", release.Tag);
        Assert.Equal("https://github.com/Zaick99/DuckNote/releases/tag/v0.0.18", release.Page.ToString());
        // un file che non viene da GitHub in https non si considera nemmeno
        Assert.Equal(5, release.Assets.Count);
        Assert.DoesNotContain(release.Assets, asset => asset.Name == "altro.exe");
    }

    [Theory]
    [InlineData("""{ "tag_name": "v0.0.18", "draft": true }""")]
    [InlineData("""{ "tag_name": "v0.0.18", "prerelease": true }""")]
    [InlineData("""{ "tag_name": "nightly" }""")]
    [InlineData("""{ "name": "senza tag" }""")]
    [InlineData("non e' json")]
    public void What_is_not_a_published_version_is_ignored(string json) => Assert.Null(UpdateFeed.Parse(json));

    [Theory]
    [InlineData("0.0.17.0", true)]
    [InlineData("0.0.18.0", false)]
    [InlineData("0.0.19.0", false)]
    [InlineData("0.1.0.0", false)]
    public void Only_a_newer_version_is_an_update(string running, bool newer)
    {
        ReleaseInfo release = UpdateFeed.Parse(Release)!;
        Assert.Equal(newer, UpdateFeed.IsNewer(release, Version.Parse(running)));
    }

    [Theory]
    [InlineData(UpdateTarget.WindowsPortable, "DuckNote.exe")]
    [InlineData(UpdateTarget.WindowsInstaller, "DuckNote-0.0.18-setup.exe")]
    [InlineData(UpdateTarget.MacArm64, "DuckNote-0.0.18-macos-arm64.dmg")]
    [InlineData(UpdateTarget.MacX64, "DuckNote-0.0.18-macos-x64.dmg")]
    public void Each_installation_takes_its_own_file(UpdateTarget target, string file)
    {
        ReleaseInfo release = UpdateFeed.Parse(Release)!;
        Assert.Equal(file, UpdateFeed.AssetFor(release, target)?.Name);
    }

    [Fact]
    public void The_hash_list_is_read_line_by_line()
    {
        string sums = "63c84d66126abb1f81c1765a7951b6013df426d64ac26a6a67d015e3c3cf0431  DuckNote.exe\r\n"
                      + "A9A8A38D748D2500D266F20ABDE1FEBC6FBB2179CAA05972A8842CCD333F162F  DuckNote-0.0.18-macos-arm64.dmg\r\n"
                      + "non-un-hash  DuckNote-0.0.18-setup.exe\r\n";

        Assert.Equal("63c84d66126abb1f81c1765a7951b6013df426d64ac26a6a67d015e3c3cf0431", UpdateFeed.ExpectedHash(sums, "DuckNote.exe"));
        Assert.Equal("a9a8a38d748d2500d266f20abde1febc6fbb2179caa05972a8842ccd333f162f", UpdateFeed.ExpectedHash(sums, "DuckNote-0.0.18-macos-arm64.dmg"));
        Assert.Null(UpdateFeed.ExpectedHash(sums, "DuckNote-0.0.18-setup.exe"));
        Assert.Null(UpdateFeed.ExpectedHash(sums, "DuckNote-0.0.18-macos-x64.dmg"));
    }

    [Fact]
    public async Task A_file_is_kept_only_if_it_matches_the_published_hash()
    {
        byte[] exe = Encoding.ASCII.GetBytes("abc");
        string good = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";  // SHA-256 di "abc"
        string folder = Path.Combine(Path.GetTempPath(), "ducknote-update-test-" + Guid.NewGuid().ToString("N"));
        ReleaseInfo release = UpdateFeed.Parse(Release)!;
        ReleaseAsset asset = UpdateFeed.AssetFor(release, UpdateTarget.WindowsPortable)!;

        try
        {
            Updater right = new(new HttpClient(new Fake(good + "  DuckNote.exe\n", exe)));
            string path = await right.DownloadAsync(release, asset, folder);
            Assert.Equal(exe, await File.ReadAllBytesAsync(path));

            Directory.Delete(folder, recursive: true);
            Updater wrong = new(new HttpClient(new Fake(new string('0', 64) + "  DuckNote.exe\n", exe)));
            await Assert.ThrowsAsync<InvalidDataException>(() => wrong.DownloadAsync(release, asset, folder));
            // niente resta a meta': ne' il file, ne' il parziale
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>GitHub finto: SHA256SUMS.txt e il file chiesto, nient'altro.</summary>
    private sealed class Fake(string sums, byte[] file) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            HttpContent content = path.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal)
                ? new StringContent(sums)
                : new ByteArrayContent(file);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
