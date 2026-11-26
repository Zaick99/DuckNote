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

    /// <summary>
    /// Su macOS e Linux non c'e' CryptProtectMemory: la chiave in memoria e' velata con uno XOR
    /// contro una maschera casuale tenuta altrove, cosi' i suoi byte non stanno mai in chiaro
    /// in un'immagine della memoria se non nel momento in cui servono.
    /// </summary>
    private IntPtr _mask;
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
        if (OperatingSystem.IsWindows())
        {
            _pinned = VirtualLock(_page, (UIntPtr)(ulong)_capacity);
            return;
        }

        // altrove: mlock la tiene fuori dallo swap (se il sistema lo consente) e una maschera
        // casuale la vela
        _pinned = Lock(_page, _capacity);
        _mask = Marshal.AllocHGlobal(_capacity);
        byte[] noise = RandomNumberGenerator.GetBytes(_capacity);
        Marshal.Copy(noise, 0, _mask, _capacity);
        CryptographicOperations.ZeroMemory(noise);
        Lock(_mask, _capacity);
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

        if (!OperatingSystem.IsWindows())
        {
            Flip();
            _veiled = true;
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

        if (!OperatingSystem.IsWindows())
        {
            Flip();
            _veiled = false;
            return;
        }

        if (CryptUnprotectMemory(_page, (uint)_capacity, SameProcess))
        {
            _veiled = false;
        }
    }

    /// <summary>XOR con la maschera: la stessa operazione vela e svela.</summary>
    private void Flip()
    {
        for (int i = 0; i < _capacity; i++)
        {
            Marshal.WriteByte(_page, i, (byte)(Marshal.ReadByte(_page, i) ^ Marshal.ReadByte(_mask, i)));
        }
    }

    private static bool Lock(IntPtr address, int length)
    {
        try
        {
            return MLock(address, (UIntPtr)(ulong)length) == 0;
        }
        catch (Exception problem) when (problem is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void Unlock(IntPtr address, int length)
    {
        try
        {
            _ = MUnlock(address, (UIntPtr)(ulong)length);
        }
        catch (Exception problem) when (problem is DllNotFoundException or EntryPointNotFoundException)
        {
            // niente mlock: niente da sbloccare
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
        if (_pinned && OperatingSystem.IsWindows())
        {
            VirtualUnlock(_page, (UIntPtr)(ulong)_capacity);
        }
        else if (_pinned)
        {
            Unlock(_page, _capacity);
        }
        Marshal.FreeHGlobal(_page);
        _page = IntPtr.Zero;

        if (_mask != IntPtr.Zero)
        {
            for (int i = 0; i < _capacity; i++)
            {
                Marshal.WriteByte(_mask, i, 0);
            }

            Unlock(_mask, _capacity);
            Marshal.FreeHGlobal(_mask);
            _mask = IntPtr.Zero;
        }
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

    [DllImport("libc", EntryPoint = "mlock", SetLastError = true)]
    private static extern int MLock(IntPtr address, UIntPtr length);

    [DllImport("libc", EntryPoint = "munlock", SetLastError = true)]
    private static extern int MUnlock(IntPtr address, UIntPtr length);
}
