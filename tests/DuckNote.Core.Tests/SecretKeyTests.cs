using DuckNote.Core.Crypto;

namespace DuckNote.Core.Tests;

public class SecretKeyTests
{
    [Fact]
    public void The_key_comes_back_intact_and_is_never_kept_in_clear()
    {
        byte[] material = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];
        byte[] expected = [.. material];

        using SecretKey key = SecretKey.Adopt(material);

        // chi la consegna non la tiene: la copia passata viene azzerata
        Assert.All(material, b => Assert.Equal(0, b));
        Assert.True(key.Veiled);
        Assert.Equal(expected, key.Reveal());
        Assert.Equal(expected, key.Reveal());
        Assert.True(key.Veiled);
    }
}
