using System.Security.Cryptography;
using System.Text.Json;

namespace DuckNote.Notes.Updates;

/// <summary>Un file di una release: nome, indirizzo di download, misura.</summary>
public sealed record ReleaseAsset(string Name, Uri Url, long Size);

/// <summary>L'ultima release pubblicata su GitHub, quanto basta per aggiornarsi.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, Uri Page, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Che cosa sta girando, e quindi quale file della release lo sostituisce.</summary>
public enum UpdateTarget
{
    WindowsPortable,
    WindowsInstaller,
    MacArm64,
    MacX64
}

/// <summary>
/// Le release di DuckNote su GitHub, lette senza credenziali: l'ultima (api.github.com), il file
/// giusto per chi la chiede, e la sua impronta in SHA256SUMS.txt, che il workflow di release
/// pubblica con ogni versione. Un file che non corrisponde all'impronta non si usa.
/// </summary>
public static class UpdateFeed
{
    public const string Repository = "Zaick99/DuckNote";
    public const string Sums = "SHA256SUMS.txt";

    public static Uri LatestApi { get; } = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public static Uri ReleasesPage { get; } = new($"https://github.com/{Repository}/releases/latest");

    /// <summary>La release dal JSON di api.github.com; null se non si legge o non ha una versione.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || (root.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
                || (root.TryGetProperty("prerelease", out JsonElement pre) && pre.ValueKind == JsonValueKind.True)
                || !root.TryGetProperty("tag_name", out JsonElement tagElement)
                || tagElement.GetString() is not { Length: > 0 } tag
                || VersionOf(tag) is not { } version)
            {
                return null;
            }

            List<ReleaseAsset> assets = [];

            if (root.TryGetProperty("assets", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in list.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out JsonElement name) && name.GetString() is { Length: > 0 } fileName
                        && asset.TryGetProperty("browser_download_url", out JsonElement url)
                        && Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? address)
                        && IsGitHub(address))
                    {
                        long size = asset.TryGetProperty("size", out JsonElement bytes) && bytes.TryGetInt64(out long value) ? value : 0;
                        assets.Add(new ReleaseAsset(fileName, address, size));
                    }
                }
            }

            Uri page = root.TryGetProperty("html_url", out JsonElement html) && Uri.TryCreate(html.GetString(), UriKind.Absolute, out Uri? link) && IsGitHub(link)
                ? link
                : ReleasesPage;

            return new ReleaseInfo(version, tag, page, assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"v0.0.17" o "0.0.17" come versione a tre numeri; null se non lo e'.</summary>
    public static Version? VersionOf(string tag)
    {
        string text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out Version? version) && version.Build >= 0 ? Three(version) : null;
    }

    /// <summary>La release e' piu' nuova di quella in uso (si confrontano tre numeri: la quarta cifra non conta).</summary>
    public static bool IsNewer(ReleaseInfo release, Version current) => Three(release.Version) > Three(current);

    /// <summary>Il nome del file che aggiorna quella installazione, come lo pubblica il workflow.</summary>
    public static string FileName(UpdateTarget target, Version version)
    {
        string v = Three(version).ToString(3);

        return target switch
        {
            UpdateTarget.WindowsPortable => "DuckNote.exe",
            UpdateTarget.WindowsInstaller => $"DuckNote-{v}-setup.exe",
            UpdateTarget.MacArm64 => $"DuckNote-{v}-macos-arm64.dmg",
            _ => $"DuckNote-{v}-macos-x64.dmg"
        };
    }

    public static ReleaseAsset? AssetFor(ReleaseInfo release, UpdateTarget target) =>
        release.Assets.FirstOrDefault(asset => asset.Name.Equals(FileName(target, release.Version), StringComparison.Ordinal));

    /// <summary>L'impronta del file in SHA256SUMS.txt ("hash  nome" per riga); null se non c'e'.</summary>
    public static string? ExpectedHash(string sums, string fileName)
    {
        foreach (string line in sums.Split('\n'))
        {
            string[] parts = line.Trim().Split((char[])[' ', '\t', '*'], 2, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 2 && parts[1].Trim().Equals(fileName, StringComparison.Ordinal)
                && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit))
            {
                return parts[0].ToLowerInvariant();
            }
        }

        return null;
    }

    public static string Hash(Stream content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>Si scarica solo da GitHub, e solo in https.</summary>
    public static bool IsGitHub(Uri address) =>
        address.Scheme == Uri.UriSchemeHttps
        && (address.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || address.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            || address.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static Version Three(Version version) =>
        new(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));

    public static string Describe(Version version) => Three(version).ToString(3);
}

/// <summary>
/// Controlla l'ultima release e ne scarica il file, verificato. Una richiesta anonima a
/// api.github.com all'avvio; il download (decine di MB) solo se c'e' davvero qualcosa di nuovo.
/// </summary>
public sealed class Updater(HttpClient http)
{
    public static HttpClient Client(Version current)
    {
        HttpClient client = new() { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub vuole un User-Agent: il nome del programma e la sua versione, nient'altro
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DuckNote/{UpdateFeed.Describe(current)}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public async Task<ReleaseInfo?> LatestAsync(CancellationToken cancel = default)
    {
        using HttpResponseMessage response = await http.GetAsync(UpdateFeed.LatestApi, cancel).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return UpdateFeed.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
    }

    /// <summary>
    /// Scarica il file in <paramref name="folder"/> e lo tiene solo se la sua impronta e' quella
    /// pubblicata in SHA256SUMS.txt della stessa release. Torna il percorso del file verificato.
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, string folder, CancellationToken cancel = default)
    {
        ReleaseAsset sums = release.Assets.FirstOrDefault(a => a.Name == UpdateFeed.Sums)
            ?? throw new InvalidDataException($"la release {release.Tag} non ha {UpdateFeed.Sums}");

        string list = await http.GetStringAsync(sums.Url, cancel).ConfigureAwait(false);
        string expected = UpdateFeed.ExpectedHash(list, asset.Name)
            ?? throw new InvalidDataException($"{asset.Name} non e' in {UpdateFeed.Sums}");

        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, asset.Name);
        string partial = target + ".part";

        await using (Stream body = await http.GetStreamAsync(asset.Url, cancel).ConfigureAwait(false))
        await using (FileStream file = new(partial, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await body.CopyToAsync(file, cancel).ConfigureAwait(false);
        }

        string actual;

        await using (FileStream check = File.OpenRead(partial))
        {
            actual = UpdateFeed.Hash(check);
        }

        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            File.Delete(partial);
            throw new InvalidDataException($"{asset.Name}: l'impronta non corrisponde a quella pubblicata");
        }

        File.Move(partial, target, overwrite: true);
        return target;
    }
}
