using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace DuckNote.Core.Crypto;

public sealed class SecretKey : IDisposable
{
    private const uint SameProcess = 0x00;
    private const int BlockSize = 16;

    private readonly int _length;
    private readonly int _capacity;
    private readonly bool _pinned;
    private IntPtr _page;
    private bool _veiled;
    private bool _disposed;

    public int Length => _length;

    public bool Paged => !_pinned;

    public bool Veiled => _veiled;

    public static SecretKey Adopt(byte[] material)
    {
        if (material is null || material.Length == 0)
        {
            throw new ArgumentException("La chiave non puo' essere vuota.", nameof(material));
        }

        SecretKey key = new(material.Length);
        try
        {
            Marshal.Copy(material, 0, key._page, material.Length);
            key.Veil();
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private SecretKey(int length)
    {
        _length = length;
        _capacity = (length + BlockSize - 1) / BlockSize * BlockSize;
        _page = Marshal.AllocHGlobal(_capacity);

        for (int i = 0; i < _capacity; i++)
        {
            Marshal.WriteByte(_page, i, 0);
        }
        _pinned = VirtualLock(_page, (UIntPtr)(ulong)_capacity);
    }

    public byte[] Reveal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Unveil();
        try
        {
            byte[] clear = new byte[_length];
            Marshal.Copy(_page, clear, 0, _length);
            return clear;
        }
        finally
        {
            Veil();
        }
    }

    public byte[] Derive(string label)
    {
        byte[] parent = Reveal();
        try
        {
            return HMACSHA256.HashData(parent, System.Text.Encoding.ASCII.GetBytes(label));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(parent);
        }
    }

    private void Veil()
    {
        if (_veiled)
        {
            return;
        }
        if (CryptProtectMemory(_page, (uint)_capacity, SameProcess))
        {
            _veiled = true;
        }
    }

    private void Unveil()
    {
        if (!_veiled)
        {
            return;
        }
        if (CryptUnprotectMemory(_page, (uint)_capacity, SameProcess))
        {
            _veiled = false;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        _ = disposing;
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_page == IntPtr.Zero)
        {
            return;
        }

        for (int i = 0; i < _capacity; i++)
        {
            Marshal.WriteByte(_page, i, 0);
        }
        if (_pinned)
        {
            VirtualUnlock(_page, (UIntPtr)(ulong)_capacity);
        }
        Marshal.FreeHGlobal(_page);
        _page = IntPtr.Zero;
    }

    ~SecretKey() => Dispose(disposing: false);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualLock(IntPtr address, UIntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualUnlock(IntPtr address, UIntPtr size);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectMemory(IntPtr data, uint size, uint flags);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectMemory(IntPtr data, uint size, uint flags);
}
