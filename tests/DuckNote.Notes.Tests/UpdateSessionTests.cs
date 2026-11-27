using System.Net;
using System.Security.Cryptography;
using System.Text;
using DuckNote.Notes.Updates;

namespace DuckNote.Notes.Tests;

public class UpdateSessionTests
{
    private static readonly byte[] Dmg = Encoding.ASCII.GetBytes("un'immagine disco di prova");

    private static string Release(string version) => $$"""
        {
          "tag_name": "v{{version}}",
          "html_url": "https://github.com/Zaick99/DuckNote/releases/tag/v{{version}}",
          "assets": [
            { "name": "DuckNote-{{version}}-macos-arm64.dmg", "size": 26, "browser_download_url": "https://github.com/Zaick99/DuckNote/releases/download/v{{version}}/DuckNote-{{version}}-macos-arm64.dmg" },
            { "name": "SHA256SUMS.txt", "size": 100, "browser_download_url": "https://github.com/Zaick99/DuckNote/releases/download/v{{version}}/SHA256SUMS.txt" }
          ]
        }
        """;

    [Fact]
    public async Task A_version_that_does_not_prepare_says_why_and_can_be_opened_by_hand()
    {
        GitHub github = new("9.9.9");
        Installer installer = new() { Prepared = false, Failure = "l'immagine disco non si monta (codice 1: attach failed)" };
        UpdateSession session = new(installer, new Version(0, 0, 18), _ => new HttpClient(github));

        await session.CheckAsync();

        Assert.False(session.IsReady);
        Assert.True(session.CanOpenDownloaded);
        Assert.Contains("(l'immagine disco non si monta (codice 1: attach failed))", session.Message);
        Assert.Contains("trascina DuckNote in Applicazioni", session.Message);
        Assert.Equal(1, github.Downloads);

        Assert.True(session.OpenDownloaded());
        Assert.Equal(Dmg, File.ReadAllBytes(installer.Opened!));
    }

    [Fact]
    public async Task The_same_failed_version_is_not_downloaded_again()
    {
        GitHub github = new("9.9.8");
        Installer installer = new() { Prepared = false, Failure = "boh" };
        UpdateSession session = new(installer, new Version(0, 0, 18), _ => new HttpClient(github));

        await session.CheckAsync();
        await session.CheckAsync();

        Assert.Equal(1, github.Downloads);
        Assert.True(session.CanOpenDownloaded);
    }

    [Fact]
    public async Task Without_a_manual_way_the_release_page_remains()
    {
        GitHub github = new("9.9.7");
        Installer installer = new() { Prepared = false, Hint = null };
        UpdateSession session = new(installer, new Version(0, 0, 18), _ => new HttpClient(github));

        await session.CheckAsync();

        Assert.False(session.CanOpenDownloaded);
        Assert.Contains("si scarica dalla pagina della release", session.Message);
    }

    [Fact]
    public async Task A_prepared_version_installs_at_exit()
    {
        GitHub github = new("9.9.6");
        Installer installer = new() { Prepared = true };
        UpdateSession session = new(installer, new Version(0, 0, 18), _ => new HttpClient(github));

        await session.CheckAsync();
        Assert.True(session.IsReady);
        Assert.False(session.CanOpenDownloaded);

        session.InstallOnExit();
        Assert.Equal(1, installer.Installs);
    }

    /// <summary>GitHub finto: l'ultima release, la sua lista di impronte e l'immagine disco.</summary>
    private sealed class GitHub(string version) : HttpMessageHandler
    {
        public int Downloads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            string name = $"DuckNote-{version}-macos-arm64.dmg";
            HttpContent content;

            if (path.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                content = new StringContent(Release(version));
            }
            else if (path.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal))
            {
                content = new StringContent($"{Convert.ToHexString(SHA256.HashData(Dmg)).ToLowerInvariant()}  {name}\n");
            }
            else
            {
                Downloads++;
                content = new ByteArrayContent(Dmg);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class Installer : IUpdateInstaller
    {
        public bool Prepared { get; init; }

        public string? Hint { get; init; } = "apri l'aggiornamento e trascina DuckNote in Applicazioni";

        public string? Opened { get; private set; }

        public int Installs { get; private set; }

        public UpdateTarget? Target => UpdateTarget.MacArm64;

        public string? Blocker => null;

        public string? Failure { get; init; }

        public string? OpenHint => Hint;

        public Task<bool> PrepareAsync(string downloaded, ReleaseInfo release, CancellationToken cancel) => Task.FromResult(Prepared);

        public bool Install(bool relaunch)
        {
            Installs++;
            return true;
        }

        public bool OpenDownloaded(string downloaded)
        {
            Opened = downloaded;
            return true;
        }
    }
}
