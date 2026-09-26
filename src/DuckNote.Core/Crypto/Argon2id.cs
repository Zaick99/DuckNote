using System.Buffers.Binary;

namespace DuckNote.Core.Crypto;

public static class Argon2id
{
    private const int Words = 128;
    private const int Version = 0x13;
    private const int TypeId = 2;
    private const int SyncPoints = 4;

    public static byte[] Hash(
        byte[]? password,
        byte[] salt,
        byte[]? secret,
        byte[]? associated,
        int memoryKib,
        int iterations,
        int lanes,
        int outputLength)
    {
        password ??= [];
        secret ??= [];
        associated ??= [];

        if (salt is null || salt.Length < 8)
        {
            throw new ArgumentException("Serve un salt di almeno 8 byte.", nameof(salt));
        }
        if (lanes is < 1 or > 0xFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(lanes), "Le lane vanno da 1 a 16777215.");
        }
        if (iterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Serve almeno una iterazione.");
        }
        if (outputLength < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(outputLength), "Il tag deve essere di almeno 4 byte.");
        }
        if (memoryKib < 8 * lanes)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryKib), "Servono almeno 8 blocchi per lane.");
        }

        int blocks = memoryKib / (SyncPoints * lanes) * SyncPoints * lanes;
        int laneLength = blocks / lanes;
        int segmentLength = laneLength / SyncPoints;

        byte[] h0 = InitialHash(password, salt, secret, associated, memoryKib, iterations, lanes, outputLength);

        ulong[] memory = new ulong[(long)blocks * Words];
        byte[] seed = new byte[h0.Length + 8];
        h0.CopyTo(seed, 0);

        for (int lane = 0; lane < lanes; lane++)
        {
            for (int column = 0; column < 2; column++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(seed.AsSpan(h0.Length), column);
                BinaryPrimitives.WriteInt32LittleEndian(seed.AsSpan(h0.Length + 4), lane);
                Absorb(LongHash(seed, 1024), memory, (long)(lane * laneLength + column) * Words);
            }
        }

        Scratch scratch = new();
        for (int pass = 0; pass < iterations; pass++)
        {
            for (int slice = 0; slice < SyncPoints; slice++)
            {
                for (int lane = 0; lane < lanes; lane++)
                {
                    FillSegment(memory, scratch, pass, slice, lane,
                                blocks, lanes, laneLength, segmentLength, iterations);
                }
            }
        }

        byte[] final = new byte[1024];
        for (int lane = 0; lane < lanes; lane++)
        {
            long offset = (long)(lane * laneLength + laneLength - 1) * Words;
            for (int i = 0; i < Words; i++)
            {
                ulong word = memory[offset + i] ^ BinaryPrimitives.ReadUInt64LittleEndian(final.AsSpan(i * 8));
                BinaryPrimitives.WriteUInt64LittleEndian(final.AsSpan(i * 8), word);
            }
        }

        Array.Clear(memory);
        Array.Clear(seed);
        Array.Clear(h0);

        try
        {
            return LongHash(final, outputLength);
        }
        finally
        {
            Array.Clear(final);
        }
    }

    private sealed class Scratch
    {
        internal readonly ulong[] R = new ulong[Words];
        internal readonly ulong[] Z = new ulong[Words];
        internal readonly ulong[] Zero = new ulong[Words];
        internal readonly ulong[] Input = new ulong[Words];
        internal readonly ulong[] Address = new ulong[Words];
        internal readonly ulong[] Tmp = new ulong[Words];
    }

    private static void FillSegment(
        ulong[] memory, Scratch scratch, int pass, int slice, int lane,
        int blocks, int lanes, int laneLength, int segmentLength, int iterations)
    {
        bool indexed = pass == 0 && slice < 2;
        if (indexed)
        {
            Array.Clear(scratch.Input);
            scratch.Input[0] = (ulong)pass;
            scratch.Input[1] = (ulong)lane;
            scratch.Input[2] = (ulong)slice;
            scratch.Input[3] = (ulong)blocks;
            scratch.Input[4] = (ulong)iterations;
            scratch.Input[5] = TypeId;
        }

        int start = pass == 0 && slice == 0 ? 2 : 0;
        int current = lane * laneLength + slice * segmentLength + start;

        for (int index = start; index < segmentLength; index++, current++)
        {
            int previous = current % laneLength == 0 ? current + laneLength - 1 : current - 1;

            ulong random;
            if (indexed)
            {
                if (index % Words == 0)
                {
                    scratch.Input[6]++;
                    FillBlock(scratch, scratch.Zero, 0, scratch.Input, 0, scratch.Tmp, 0, withXor: false);
                    FillBlock(scratch, scratch.Zero, 0, scratch.Tmp, 0, scratch.Address, 0, withXor: false);
                }
                random = scratch.Address[index % Words];
            }
            else
            {
                random = memory[(long)previous * Words];
            }

            int referenceLane = pass == 0 && slice == 0 ? lane : (int)((random >> 32) % (ulong)lanes);
            int referenceIndex = IndexAlpha(pass, slice, index, (uint)random,
                                            referenceLane == lane, segmentLength, laneLength);

            FillBlock(scratch,
                      memory, (long)previous * Words,
                      memory, (long)(referenceLane * laneLength + referenceIndex) * Words,
                      memory, (long)current * Words,
                      withXor: pass != 0);
        }
    }

    private static int IndexAlpha(
        int pass, int slice, int index, uint random, bool sameLane, int segmentLength, int laneLength)
    {
        ulong area;
        if (pass == 0)
        {
            if (slice == 0)
            {
                area = (ulong)(index - 1);
            }
            else if (sameLane)
            {
                area = (ulong)((slice * segmentLength) + index - 1);
            }
            else
            {
                area = (ulong)((slice * segmentLength) - (index == 0 ? 1 : 0));
            }
        }
        else if (sameLane)
        {
            area = (ulong)(laneLength - segmentLength + index - 1);
        }
        else
        {
            area = (ulong)(laneLength - segmentLength - (index == 0 ? 1 : 0));
        }

        ulong relative = (ulong)random * random >> 32;
        relative = area - 1 - (area * relative >> 32);
        ulong start = pass == 0 ? 0UL : (ulong)(slice == SyncPoints - 1 ? 0 : (slice + 1) * segmentLength);
        return (int)((start + relative) % (ulong)laneLength);
    }

    private static void FillBlock(
        Scratch scratch,
        ulong[] previousMemory, long previous,
        ulong[] referenceMemory, long reference,
        ulong[] outputMemory, long next,
        bool withXor)
    {
        for (int i = 0; i < Words; i++)
        {
            scratch.R[i] = previousMemory[previous + i] ^ referenceMemory[reference + i];
        }
        Array.Copy(scratch.R, scratch.Z, Words);

        for (int i = 0; i < 8; i++)
        {
            PermuteRow(scratch.Z, i * 16);
        }
        for (int i = 0; i < 8; i++)
        {
            PermuteColumn(scratch.Z, i * 2);
        }

        if (withXor)
        {
            for (int i = 0; i < Words; i++)
            {
                outputMemory[next + i] ^= scratch.Z[i] ^ scratch.R[i];
            }
        }
        else
        {
            for (int i = 0; i < Words; i++)
            {
                outputMemory[next + i] = scratch.Z[i] ^ scratch.R[i];
            }
        }
    }

    private static void PermuteRow(ulong[] block, int o) =>
        Round(block, o, o + 1, o + 2, o + 3, o + 4, o + 5, o + 6, o + 7,
                     o + 8, o + 9, o + 10, o + 11, o + 12, o + 13, o + 14, o + 15);

    private static void PermuteColumn(ulong[] block, int o) =>
        Round(block, o, o + 1, o + 16, o + 17, o + 32, o + 33, o + 48, o + 49,
                     o + 64, o + 65, o + 80, o + 81, o + 96, o + 97, o + 112, o + 113);

    private static void Round(
        ulong[] b, int i0, int i1, int i2, int i3, int i4, int i5, int i6, int i7,
                   int i8, int i9, int iA, int iB, int iC, int iD, int iE, int iF)
    {
        Mix(b, i0, i4, i8, iC);
        Mix(b, i1, i5, i9, iD);
        Mix(b, i2, i6, iA, iE);
        Mix(b, i3, i7, iB, iF);
        Mix(b, i0, i5, iA, iF);
        Mix(b, i1, i6, iB, iC);
        Mix(b, i2, i7, i8, iD);
        Mix(b, i3, i4, i9, iE);
    }

    private static void Mix(ulong[] v, int a, int b, int c, int d)
    {
        v[a] = v[a] + v[b] + (2UL * (uint)v[a] * (uint)v[b]);
        v[d] = ulong.RotateRight(v[d] ^ v[a], 32);
        v[c] = v[c] + v[d] + (2UL * (uint)v[c] * (uint)v[d]);
        v[b] = ulong.RotateRight(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + (2UL * (uint)v[a] * (uint)v[b]);
        v[d] = ulong.RotateRight(v[d] ^ v[a], 16);
        v[c] = v[c] + v[d] + (2UL * (uint)v[c] * (uint)v[d]);
        v[b] = ulong.RotateRight(v[b] ^ v[c], 63);
    }

    private static byte[] InitialHash(
        byte[] password, byte[] salt, byte[] secret, byte[] associated,
        int memoryKib, int iterations, int lanes, int outputLength)
    {
        Blake2b hash = new(64);
        hash.Update(Le32(lanes));
        hash.Update(Le32(outputLength));
        hash.Update(Le32(memoryKib));
        hash.Update(Le32(iterations));
        hash.Update(Le32(Version));
        hash.Update(Le32(TypeId));
        hash.Update(Le32(password.Length));   hash.Update(password);
        hash.Update(Le32(salt.Length));       hash.Update(salt);
        hash.Update(Le32(secret.Length));     hash.Update(secret);
        hash.Update(Le32(associated.Length)); hash.Update(associated);
        return hash.Digest();
    }

    private static byte[] LongHash(byte[] input, int outputLength)
    {
        if (outputLength <= 64)
        {
            Blake2b short_ = new(outputLength);
            short_.Update(Le32(outputLength));
            short_.Update(input);
            return short_.Digest();
        }

        int rounds = ((outputLength + 31) / 32) - 2;
        byte[] result = new byte[outputLength];

        Blake2b first = new(64);
        first.Update(Le32(outputLength));
        first.Update(input);
        byte[] previous = first.Digest();
        previous.AsSpan(0, 32).CopyTo(result);

        for (int i = 1; i < rounds; i++)
        {
            Blake2b step = new(64);
            step.Update(previous);
            previous = step.Digest();
            previous.AsSpan(0, 32).CopyTo(result.AsSpan(32 * i));
        }

        int tail = outputLength - (32 * rounds);
        Blake2b last = new(tail);
        last.Update(previous);
        last.Digest().CopyTo(result.AsSpan(32 * rounds));
        return result;
    }

    private static void Absorb(byte[] block, ulong[] memory, long offset)
    {
        for (int i = 0; i < Words; i++)
        {
            memory[offset + i] = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(i * 8));
        }
    }

    private static byte[] Le32(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
