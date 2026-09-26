using System.Buffers.Binary;

namespace DuckNote.Core.Crypto;

internal sealed class Blake2b
{
    private static readonly ulong[] Iv =
    [
        0x6A09E667F3BCC908UL, 0xBB67AE8584CAA73BUL, 0x3C6EF372FE94F82BUL, 0xA54FF53A5F1D36F1UL,
        0x510E527FADE682D1UL, 0x9B05688C2B3E6C1FUL, 0x1F83D9ABFB41BD6BUL, 0x5BE0CD19137E2179UL
    ];

    private static readonly byte[,] Sigma =
    {
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15},
        {14,10, 4, 8, 9,15,13, 6, 1,12, 0, 2,11, 7, 5, 3},
        {11, 8,12, 0, 5, 2,15,13,10,14, 3, 6, 7, 1, 9, 4},
        { 7, 9, 3, 1,13,12,11,14, 2, 6, 5,10, 4, 0,15, 8},
        { 9, 0, 5, 7, 2, 4,10,15,14, 1,11,12, 6, 8, 3,13},
        { 2,12, 6,10, 0,11, 8, 3, 4,13, 7, 5,15,14, 1, 9},
        {12, 5, 1,15,14,13, 4,10, 0, 7, 6, 3, 9, 2, 8,11},
        {13,11, 7,14,12, 1, 3, 9, 5, 0,15, 4, 8, 6, 2,10},
        { 6,15,14, 9,11, 3, 0, 8,12, 2,13, 7, 1, 4,10, 5},
        {10, 2, 8, 4, 7, 6, 1, 5,15,11, 9,14, 3,12,13, 0},
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15},
        {14,10, 4, 8, 9,15,13, 6, 1,12, 0, 2,11, 7, 5, 3}
    };

    private const int BlockSize = 128;

    private readonly ulong[] _h = new ulong[8];
    private readonly ulong[] _m = new ulong[16];
    private readonly ulong[] _v = new ulong[16];
    private readonly byte[] _buffer = new byte[BlockSize];
    private readonly int _outputLength;
    private int _buffered;
    private ulong _counter;

    internal Blake2b(int outputLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(outputLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(outputLength, 64);

        _outputLength = outputLength;
        Iv.CopyTo(_h, 0);
        _h[0] ^= 0x01010000UL ^ (ulong)outputLength;
    }

    internal void Update(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            if (_buffered == BlockSize)
            {
                _counter += BlockSize;
                Compress(last: false);
                _buffered = 0;
            }

            int take = Math.Min(BlockSize - _buffered, data.Length);
            data[..take].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += take;
            data = data[take..];
        }
    }

    internal byte[] Digest()
    {
        _counter += (ulong)_buffered;
        _buffer.AsSpan(_buffered).Clear();
        Compress(last: true);

        byte[] digest = new byte[_outputLength];
        for (int i = 0; i < _outputLength; i++)
        {
            digest[i] = (byte)(_h[i >> 3] >> (8 * (i & 7)));
        }
        return digest;
    }

    private void Compress(bool last)
    {
        for (int i = 0; i < 16; i++)
        {
            _m[i] = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.AsSpan(i * 8));
        }

        for (int i = 0; i < 8; i++)
        {
            _v[i] = _h[i];
            _v[8 + i] = Iv[i];
        }

        _v[12] ^= _counter;
        if (last)
        {
            _v[14] ^= ulong.MaxValue;
        }

        for (int round = 0; round < 12; round++)
        {
            Mix(_v, 0, 4,  8, 12, _m[Sigma[round,  0]], _m[Sigma[round,  1]]);
            Mix(_v, 1, 5,  9, 13, _m[Sigma[round,  2]], _m[Sigma[round,  3]]);
            Mix(_v, 2, 6, 10, 14, _m[Sigma[round,  4]], _m[Sigma[round,  5]]);
            Mix(_v, 3, 7, 11, 15, _m[Sigma[round,  6]], _m[Sigma[round,  7]]);
            Mix(_v, 0, 5, 10, 15, _m[Sigma[round,  8]], _m[Sigma[round,  9]]);
            Mix(_v, 1, 6, 11, 12, _m[Sigma[round, 10]], _m[Sigma[round, 11]]);
            Mix(_v, 2, 7,  8, 13, _m[Sigma[round, 12]], _m[Sigma[round, 13]]);
            Mix(_v, 3, 4,  9, 14, _m[Sigma[round, 14]], _m[Sigma[round, 15]]);
        }

        for (int i = 0; i < 8; i++)
        {
            _h[i] ^= _v[i] ^ _v[8 + i];
        }
    }

    private static void Mix(ulong[] v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] = v[a] + v[b] + x;
        v[d] = ulong.RotateRight(v[d] ^ v[a], 32);
        v[c] += v[d];
        v[b] = ulong.RotateRight(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + y;
        v[d] = ulong.RotateRight(v[d] ^ v[a], 16);
        v[c] += v[d];
        v[b] = ulong.RotateRight(v[b] ^ v[c], 63);
    }
}
