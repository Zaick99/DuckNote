namespace DuckNote.Scan;

public interface IVendorLookup
{
    string Describe(string mac);

    static IVendorLookup None { get; } = new NoVendorLookup();

    private sealed class NoVendorLookup : IVendorLookup
    {
        public string Describe(string mac) => string.Empty;
    }
}
