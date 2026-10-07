[CmdletBinding()]
param([switch]$Reset)

$ErrorActionPreference = 'Continue'
$ProgressPreference     = 'SilentlyContinue'

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    $exe  = (Get-Process -Id $PID).Path
    $argv = @('-NoProfile','-ExecutionPolicy','Bypass')
    if ($PSVersionTable.PSVersion.Major -lt 6) { $argv += '-Sta' }
    $argv += @('-File',"`"$PSCommandPath`"")
    if ($Reset) { $argv += '-Reset' }
    Start-Process -FilePath $exe -ArgumentList $argv | Out-Null
    return
}

try {
    Add-Type -AssemblyName PresentationFramework
    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase
    Add-Type -AssemblyName System.Xaml
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
} catch {
    Write-Error "WPF non disponibile: $_"
    return
}

if (-not ('DuckNative.Win32' -as [type])) {
Add-Type -Namespace DuckNative -Name Win32 -MemberDefinition @'
    [DllImport("iphlpapi.dll", ExactSpelling=true)]
    public static extern int SendARP(uint DestIP, uint SrcIP, byte[] pMacAddr, ref uint PhyAddrLen);

    [DllImport("dwmapi.dll", PreserveSig=true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
'@
}

if (-not ('DuckNative.GridLengthAnimation' -as [type])) {
Add-Type -ReferencedAssemblies @(
    [System.Windows.Window].Assembly.Location
    [System.Windows.Media.Animation.AnimationTimeline].Assembly.Location
    [System.Windows.DependencyObject].Assembly.Location
) -TypeDefinition @'
using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace DuckNative {
    public class GridLengthAnimation : AnimationTimeline {
        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register("From", typeof(double), typeof(GridLengthAnimation));
        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register("To", typeof(double), typeof(GridLengthAnimation));
        public static readonly DependencyProperty EasingFunctionProperty =
            DependencyProperty.Register("EasingFunction", typeof(IEasingFunction), typeof(GridLengthAnimation));

        public double From {
            get { return (double)GetValue(FromProperty); }
            set { SetValue(FromProperty, value); }
        }
        public double To {
            get { return (double)GetValue(ToProperty); }
            set { SetValue(ToProperty, value); }
        }
        public IEasingFunction EasingFunction {
            get { return (IEasingFunction)GetValue(EasingFunctionProperty); }
            set { SetValue(EasingFunctionProperty, value); }
        }

        public override Type TargetPropertyType { get { return typeof(GridLength); } }
        protected override Freezable CreateInstanceCore() { return new GridLengthAnimation(); }

        public override object GetCurrentValue(object origin, object destination, AnimationClock clock) {
            double p = clock.CurrentProgress ?? 0.0;
            IEasingFunction ease = EasingFunction;
            if (ease != null) { p = ease.Ease(p); }
            return new GridLength(From + (To - From) * p, GridUnitType.Pixel);
        }
    }
}
'@
}

if (-not ('DuckNative.Argon2id' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Threading;

namespace DuckNative {

    internal sealed class Blake2b {
        static readonly ulong[] Iv = {
            0x6A09E667F3BCC908UL, 0xBB67AE8584CAA73BUL, 0x3C6EF372FE94F82BUL, 0xA54FF53A5F1D36F1UL,
            0x510E527FADE682D1UL, 0x9B05688C2B3E6C1FUL, 0x1F83D9ABFB41BD6BUL, 0x5BE0CD19137E2179UL
        };

        static readonly byte[,] Sigma = {
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

        readonly ulong[] h = new ulong[8];
        readonly ulong[] m = new ulong[16];
        readonly ulong[] v = new ulong[16];
        readonly byte[] buf = new byte[128];
        int bufLen;
        ulong counter;
        readonly int outLen;

        internal Blake2b(int outLen) {
            if (outLen < 1 || outLen > 64) throw new ArgumentOutOfRangeException("outLen");
            this.outLen = outLen;
            Array.Copy(Iv, h, 8);
            h[0] ^= 0x01010000UL ^ (ulong)outLen;
        }

        internal void Update(byte[] data) { Update(data, 0, data.Length); }

        internal void Update(byte[] data, int off, int len) {
            while (len > 0) {
                if (bufLen == 128) {
                    counter += 128;
                    Compress(false);
                    bufLen = 0;
                }
                int n = Math.Min(128 - bufLen, len);
                Buffer.BlockCopy(data, off, buf, bufLen, n);
                bufLen += n; off += n; len -= n;
            }
        }

        internal byte[] Digest() {
            counter += (ulong)bufLen;
            for (int i = bufLen; i < 128; i++) buf[i] = 0;
            Compress(true);
            byte[] outb = new byte[outLen];
            for (int i = 0; i < outLen; i++) outb[i] = (byte)(h[i >> 3] >> (8 * (i & 7)));
            return outb;
        }

        static ulong Rotr(ulong x, int n) { return (x >> n) | (x << (64 - n)); }

        static void G(ulong[] v, int a, int b, int c, int d, ulong x, ulong y) {
            v[a] = v[a] + v[b] + x;
            v[d] = Rotr(v[d] ^ v[a], 32);
            v[c] = v[c] + v[d];
            v[b] = Rotr(v[b] ^ v[c], 24);
            v[a] = v[a] + v[b] + y;
            v[d] = Rotr(v[d] ^ v[a], 16);
            v[c] = v[c] + v[d];
            v[b] = Rotr(v[b] ^ v[c], 63);
        }

        void Compress(bool last) {
            for (int i = 0; i < 16; i++) m[i] = BitConverter.ToUInt64(buf, i * 8);
            for (int i = 0; i < 8; i++) { v[i] = h[i]; v[8 + i] = Iv[i]; }
            v[12] ^= counter;
            if (last) v[14] ^= ulong.MaxValue;

            for (int r = 0; r < 12; r++) {
                G(v, 0, 4,  8, 12, m[Sigma[r,  0]], m[Sigma[r,  1]]);
                G(v, 1, 5,  9, 13, m[Sigma[r,  2]], m[Sigma[r,  3]]);
                G(v, 2, 6, 10, 14, m[Sigma[r,  4]], m[Sigma[r,  5]]);
                G(v, 3, 7, 11, 15, m[Sigma[r,  6]], m[Sigma[r,  7]]);
                G(v, 0, 5, 10, 15, m[Sigma[r,  8]], m[Sigma[r,  9]]);
                G(v, 1, 6, 11, 12, m[Sigma[r, 10]], m[Sigma[r, 11]]);
                G(v, 2, 7,  8, 13, m[Sigma[r, 12]], m[Sigma[r, 13]]);
                G(v, 3, 4,  9, 14, m[Sigma[r, 14]], m[Sigma[r, 15]]);
            }
            for (int i = 0; i < 8; i++) h[i] ^= v[i] ^ v[8 + i];
        }
    }

    public static class Argon2id {
        const int Words = 128;          // 1024-byte block as 64-bit words
        const int Version = 0x13;
        const int TypeId = 2;           // Argon2id

        public static byte[] Hash(byte[] password, byte[] salt, byte[] secret, byte[] associated,
                                  int memoryKib, int iterations, int lanes, int outLen) {
            if (password == null) password = new byte[0];
            if (secret == null) secret = new byte[0];
            if (associated == null) associated = new byte[0];
            if (salt == null || salt.Length < 8) throw new ArgumentException("salt di almeno 8 byte");
            if (lanes < 1 || lanes > 0xFFFFFF) throw new ArgumentException("lanes tra 1 e 16777215");
            if (iterations < 1) throw new ArgumentException("almeno una iterazione");
            if (outLen < 4) throw new ArgumentException("tag di almeno 4 byte");
            if (memoryKib < 8 * lanes) throw new ArgumentException("memoria di almeno 8 blocchi per lane");

            int blocks = (memoryKib / (4 * lanes)) * 4 * lanes;
            int laneLen = blocks / lanes;
            int segLen  = laneLen / 4;

            byte[] h0 = InitialHash(password, salt, secret, associated,
                                    memoryKib, iterations, lanes, outLen);

            ulong[] mem = new ulong[(long)blocks * Words];
            byte[] seed = new byte[h0.Length + 8];
            Buffer.BlockCopy(h0, 0, seed, 0, h0.Length);
            for (int lane = 0; lane < lanes; lane++) {
                for (int col = 0; col < 2; col++) {
                    WriteLe32(seed, h0.Length,     col);
                    WriteLe32(seed, h0.Length + 4, lane);
                    Absorb(Hprime(seed, 1024), mem, (long)(lane * laneLen + col) * Words);
                }
            }

            Scratch s = new Scratch();
            for (int pass = 0; pass < iterations; pass++)
                for (int slice = 0; slice < 4; slice++)
                    for (int lane = 0; lane < lanes; lane++)
                        FillSegment(mem, s, pass, slice, lane, blocks, lanes, laneLen, segLen, iterations);

            byte[] final = new byte[1024];
            for (int lane = 0; lane < lanes; lane++) {
                long off = (long)(lane * laneLen + laneLen - 1) * Words;
                for (int i = 0; i < Words; i++) {
                    ulong w = mem[off + i] ^ BitConverter.ToUInt64(final, i * 8);
                    WriteLe64(final, i * 8, w);
                }
            }
            Array.Clear(mem, 0, mem.Length);
            return Hprime(final, outLen);
        }

        sealed class Scratch {
            internal readonly ulong[] R = new ulong[Words];
            internal readonly ulong[] Z = new ulong[Words];
            internal readonly ulong[] Zero = new ulong[Words];
            internal readonly ulong[] Input = new ulong[Words];
            internal readonly ulong[] Address = new ulong[Words];
            internal readonly ulong[] Tmp = new ulong[Words];
        }

        static void FillSegment(ulong[] mem, Scratch s, int pass, int slice, int lane,
                                int blocks, int lanes, int laneLen, int segLen, int iterations) {
            bool indexed = (pass == 0 && slice < 2);   // Argon2id: prima meta' della prima passata
            if (indexed) {
                Array.Clear(s.Input, 0, Words);
                s.Input[0] = (ulong)pass;
                s.Input[1] = (ulong)lane;
                s.Input[2] = (ulong)slice;
                s.Input[3] = (ulong)blocks;
                s.Input[4] = (ulong)iterations;
                s.Input[5] = TypeId;
            }

            int start = (pass == 0 && slice == 0) ? 2 : 0;
            int curr  = lane * laneLen + slice * segLen + start;

            for (int index = start; index < segLen; index++, curr++) {
                int prev = (curr % laneLen == 0) ? curr + laneLen - 1 : curr - 1;

                ulong rand;
                if (indexed) {
                    if (index % Words == 0) {
                        s.Input[6]++;
                        FillBlock(s, s.Zero, 0, s.Input, 0, s.Tmp, 0, false);
                        FillBlock(s, s.Zero, 0, s.Tmp, 0, s.Address, 0, false);
                    }
                    rand = s.Address[index % Words];
                } else {
                    rand = mem[(long)prev * Words];
                }

                int refLane = (pass == 0 && slice == 0) ? lane : (int)((rand >> 32) % (ulong)lanes);
                int refIndex = IndexAlpha(pass, slice, index, (uint)rand,
                                          refLane == lane, segLen, laneLen);

                FillBlock(s, mem, (long)prev * Words,
                             mem, (long)(refLane * laneLen + refIndex) * Words,
                             mem, (long)curr * Words, pass != 0);
            }
        }

        static int IndexAlpha(int pass, int slice, int index, uint rand,
                              bool sameLane, int segLen, int laneLen) {
            ulong area;
            if (pass == 0) {
                if (slice == 0)      area = (ulong)(index - 1);
                else if (sameLane)   area = (ulong)(slice * segLen + index - 1);
                else                 area = (ulong)(slice * segLen - (index == 0 ? 1 : 0));
            } else {
                if (sameLane)        area = (ulong)(laneLen - segLen + index - 1);
                else                 area = (ulong)(laneLen - segLen - (index == 0 ? 1 : 0));
            }

            ulong rel = ((ulong)rand * (ulong)rand) >> 32;
            rel = area - 1 - ((area * rel) >> 32);
            ulong start = (pass == 0) ? 0UL : (ulong)((slice == 3) ? 0 : (slice + 1) * segLen);
            return (int)((start + rel) % (ulong)laneLen);
        }

        static void FillBlock(Scratch s, ulong[] prevMem, long prev, ulong[] refMem, long refb,
                              ulong[] outMem, long next, bool withXor) {
            for (int i = 0; i < Words; i++) s.R[i] = prevMem[prev + i] ^ refMem[refb + i];
            Array.Copy(s.R, s.Z, Words);

            for (int i = 0; i < 8; i++) PermuteRow(s.Z, i * 16);
            for (int i = 0; i < 8; i++) PermuteCol(s.Z, i * 2);

            if (withXor) for (int i = 0; i < Words; i++) outMem[next + i] ^= s.Z[i] ^ s.R[i];
            else         for (int i = 0; i < Words; i++) outMem[next + i]  = s.Z[i] ^ s.R[i];
        }

        static void PermuteRow(ulong[] b, int o) {
            Round(b, o, o + 1, o + 2, o + 3, o + 4, o + 5, o + 6, o + 7,
                     o + 8, o + 9, o + 10, o + 11, o + 12, o + 13, o + 14, o + 15);
        }

        static void PermuteCol(ulong[] b, int o) {
            Round(b, o, o + 1, o + 16, o + 17, o + 32, o + 33, o + 48, o + 49,
                     o + 64, o + 65, o + 80, o + 81, o + 96, o + 97, o + 112, o + 113);
        }

        static void Round(ulong[] b, int i0, int i1, int i2, int i3, int i4, int i5, int i6, int i7,
                                     int i8, int i9, int iA, int iB, int iC, int iD, int iE, int iF) {
            GB(b, i0, i4, i8, iC);
            GB(b, i1, i5, i9, iD);
            GB(b, i2, i6, iA, iE);
            GB(b, i3, i7, iB, iF);
            GB(b, i0, i5, iA, iF);
            GB(b, i1, i6, iB, iC);
            GB(b, i2, i7, i8, iD);
            GB(b, i3, i4, i9, iE);
        }

        static void GB(ulong[] v, int a, int b, int c, int d) {
            v[a] = v[a] + v[b] + 2UL * (ulong)(uint)v[a] * (ulong)(uint)v[b];
            v[d] = Rotr(v[d] ^ v[a], 32);
            v[c] = v[c] + v[d] + 2UL * (ulong)(uint)v[c] * (ulong)(uint)v[d];
            v[b] = Rotr(v[b] ^ v[c], 24);
            v[a] = v[a] + v[b] + 2UL * (ulong)(uint)v[a] * (ulong)(uint)v[b];
            v[d] = Rotr(v[d] ^ v[a], 16);
            v[c] = v[c] + v[d] + 2UL * (ulong)(uint)v[c] * (ulong)(uint)v[d];
            v[b] = Rotr(v[b] ^ v[c], 63);
        }

        static ulong Rotr(ulong x, int n) { return (x >> n) | (x << (64 - n)); }

        static byte[] InitialHash(byte[] pwd, byte[] salt, byte[] secret, byte[] ad,
                                  int memoryKib, int iterations, int lanes, int outLen) {
            Blake2b b = new Blake2b(64);
            b.Update(Le32(lanes));
            b.Update(Le32(outLen));
            b.Update(Le32(memoryKib));
            b.Update(Le32(iterations));
            b.Update(Le32(Version));
            b.Update(Le32(TypeId));
            b.Update(Le32(pwd.Length));    b.Update(pwd);
            b.Update(Le32(salt.Length));   b.Update(salt);
            b.Update(Le32(secret.Length)); b.Update(secret);
            b.Update(Le32(ad.Length));     b.Update(ad);
            return b.Digest();
        }

        static byte[] Hprime(byte[] input, int outLen) {
            if (outLen <= 64) {
                Blake2b b = new Blake2b(outLen);
                b.Update(Le32(outLen));
                b.Update(input);
                return b.Digest();
            }

            int rounds = (outLen + 31) / 32 - 2;
            byte[] result = new byte[outLen];

            Blake2b first = new Blake2b(64);
            first.Update(Le32(outLen));
            first.Update(input);
            byte[] v = first.Digest();
            Buffer.BlockCopy(v, 0, result, 0, 32);

            for (int i = 1; i < rounds; i++) {
                Blake2b h = new Blake2b(64);
                h.Update(v);
                v = h.Digest();
                Buffer.BlockCopy(v, 0, result, 32 * i, 32);
            }

            int tail = outLen - 32 * rounds;
            Blake2b lastH = new Blake2b(tail);
            lastH.Update(v);
            Buffer.BlockCopy(lastH.Digest(), 0, result, 32 * rounds, tail);
            return result;
        }

        static void Absorb(byte[] block, ulong[] mem, long off) {
            for (int i = 0; i < Words; i++) mem[off + i] = BitConverter.ToUInt64(block, i * 8);
        }

        static byte[] Le32(int v) {
            return new byte[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) };
        }

        static void WriteLe32(byte[] dst, int off, int v) {
            dst[off] = (byte)v; dst[off+1] = (byte)(v >> 8);
            dst[off+2] = (byte)(v >> 16); dst[off+3] = (byte)(v >> 24);
        }

        static void WriteLe64(byte[] dst, int off, ulong v) {
            for (int i = 0; i < 8; i++) dst[off + i] = (byte)(v >> (8 * i));
        }
    }

    // Deriva la chiave su un thread proprio: il thread della finestra resta
    // libero di animare mentre Argon2id macina 256 MiB.
    public sealed class VaultKey {
        volatile bool done;
        byte[] key;
        string error;

        public bool Done    { get { return done; } }
        public byte[] Key   { get { return key; } }
        public string Error { get { return error; } }

        public static VaultKey Derive(byte[] password, byte[] salt,
                                      int memoryKib, int passes, int lanes, int iterations) {
            VaultKey w = new VaultKey();
            Thread th = new Thread(delegate() {
                w.Run(password, salt, memoryKib, passes, lanes, iterations);
            });
            th.IsBackground = true;
            th.Priority = ThreadPriority.BelowNormal;
            th.Start();
            return w;
        }

        void Run(byte[] password, byte[] salt, int memoryKib, int passes, int lanes, int iterations) {
            byte[] pre = null;
            try {
                pre = Argon2id.Hash(password, salt, null, null, memoryKib, passes, lanes, 64);
                key = Pbkdf2Sha512(pre, salt, iterations, 64);
            } catch (Exception ex) {
                error = ex.Message;
            } finally {
                if (pre != null) Array.Clear(pre, 0, pre.Length);
                Array.Clear(password, 0, password.Length);
                done = true;
            }
        }

        // PBKDF2 scritto qui perche' l'unico costruttore Rfc2898DeriveBytes comune a
        // .NET Framework 4.8 e a .NET recenti e' deprecato, e Add-Type di PowerShell 5.1
        // rifiuta il pragma che ne zittirebbe l'avviso. La costruzione e' quella di
        // RFC 8018 su HMAC-SHA512 e i test la confrontano con l'implementazione Microsoft.
        public static byte[] Pbkdf2Sha512(byte[] password, byte[] salt, int iterations, int outLen) {
            const int HashLen = 64;
            int blocks = (outLen + HashLen - 1) / HashLen;
            byte[] result = new byte[outLen];
            byte[] input = new byte[salt.Length + 4];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);

            using (HMACSHA512 hmac = new HMACSHA512(password)) {
                for (int b = 1; b <= blocks; b++) {
                    input[salt.Length]     = (byte)(b >> 24);
                    input[salt.Length + 1] = (byte)(b >> 16);
                    input[salt.Length + 2] = (byte)(b >> 8);
                    input[salt.Length + 3] = (byte)b;

                    byte[] u = hmac.ComputeHash(input);
                    byte[] acc = (byte[])u.Clone();
                    for (int i = 1; i < iterations; i++) {
                        u = hmac.ComputeHash(u);
                        for (int j = 0; j < HashLen; j++) acc[j] ^= u[j];
                    }
                    int take = Math.Min(HashLen, outLen - (b - 1) * HashLen);
                    Buffer.BlockCopy(acc, 0, result, (b - 1) * HashLen, take);
                }
            }
            return result;
        }
    }

    public static class Shell {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string id);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr icon);

        const int WM_SETICON  = 0x0080;
        const int ICON_SMALL  = 0;
        const int ICON_BIG    = 1;

        // Senza un'identita' propria la barra delle applicazioni raggruppa la
        // finestra sotto PowerShell, e con il gruppo ne prende anche l'icona.
        public static bool SetAppId(string id) {
            try { return SetCurrentProcessExplicitAppUserModelID(id) == 0; }
            catch { return false; }
        }

        public static void SetWindowIcon(IntPtr window, IntPtr small, IntPtr big) {
            if (window == IntPtr.Zero) return;
            if (small != IntPtr.Zero) SendMessage(window, WM_SETICON, (IntPtr)ICON_SMALL, small);
            if (big   != IntPtr.Zero) SendMessage(window, WM_SETICON, (IntPtr)ICON_BIG,   big);
        }

        public static void ReleaseIcon(IntPtr icon) {
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        }
    }

    public static class Idle {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        // Millisecondi da quando qualcuno ha toccato tastiera o mouse. L'aritmetica
        // senza segno a 32 bit regge il giro di boa di Environment.TickCount.
        public static uint Milliseconds() {
            LASTINPUTINFO info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref info)) return 0;
            return (uint)Environment.TickCount - info.dwTime;
        }
    }

    public static class Secret {
        // La password non diventa mai una String gestita: dal PasswordBox
        // arriva come SecureString e ne esce un byte[] azzerabile.
        public static byte[] FromSecureString(SecureString s) {
            if (s == null || s.Length == 0) return new byte[0];
            IntPtr p = IntPtr.Zero;
            char[] chars = new char[s.Length];
            try {
                p = Marshal.SecureStringToGlobalAllocUnicode(s);
                for (int i = 0; i < chars.Length; i++) chars[i] = (char)Marshal.ReadInt16(p, i * 2);
                return Encoding.UTF8.GetBytes(chars);
            } finally {
                Array.Clear(chars, 0, chars.Length);
                if (p != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(p);
            }
        }

        public static bool Equal(byte[] a, byte[] b) {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    // La chiave dei dati vive qui: fuori dallo heap gestito, in pagine che il
    // sistema non puo' scrivere su disco, e cifrata con una chiave di sessione
    // per tutto il tempo in cui non serve. Resta in chiaro solo dentro Reveal,
    // e il chiamante ha il dovere di azzerare la copia che riceve.
    public sealed class SecretKey : IDisposable {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualLock(IntPtr addr, UIntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualUnlock(IntPtr addr, UIntPtr size);
        [DllImport("crypt32.dll", SetLastError = true)]
        static extern bool CryptProtectMemory(IntPtr data, uint size, uint flags);
        [DllImport("crypt32.dll", SetLastError = true)]
        static extern bool CryptUnprotectMemory(IntPtr data, uint size, uint flags);

        const uint SameProcess = 0x00;   // CRYPTPROTECTMEMORY_SAME_PROCESS
        const int  Granularity = 16;     // CRYPTPROTECTMEMORY_BLOCK_SIZE

        IntPtr pagina;
        readonly int lunghezza;          // byte utili
        readonly int capienza;           // arrotondata a 16, quanto cifra CryptProtectMemory
        bool cifrata;
        bool bloccata;
        bool dismessa;

        public int Length { get { return lunghezza; } }
        public bool Paged { get { return !bloccata; } }   // false = mai su disco
        public bool Veiled { get { return cifrata; } }

        // Prende possesso del materiale: lo copia dentro e azzera l'originale.
        public static SecretKey Adopt(byte[] material) {
            if (material == null || material.Length == 0) throw new ArgumentException("chiave vuota");
            SecretKey k = new SecretKey(material.Length);
            try {
                Marshal.Copy(material, 0, k.pagina, material.Length);
                k.Veil();
                return k;
            } catch {
                k.Dispose();
                throw;
            } finally {
                Array.Clear(material, 0, material.Length);
            }
        }

        SecretKey(int length) {
            lunghezza = length;
            capienza  = ((length + Granularity - 1) / Granularity) * Granularity;
            pagina    = Marshal.AllocHGlobal(capienza);
            for (int i = 0; i < capienza; i++) Marshal.WriteByte(pagina, i, 0);
            bloccata  = VirtualLock(pagina, (UIntPtr)(ulong)capienza);
        }

        void Veil() {
            if (cifrata) return;
            if (CryptProtectMemory(pagina, (uint)capienza, SameProcess)) cifrata = true;
        }

        void Unveil() {
            if (!cifrata) return;
            if (CryptUnprotectMemory(pagina, (uint)capienza, SameProcess)) cifrata = false;
        }

        // Copia in chiaro per il tempo di una operazione: da azzerare subito dopo.
        public byte[] Reveal() {
            if (dismessa) throw new ObjectDisposedException("SecretKey");
            Unveil();
            try {
                byte[] chiaro = new byte[lunghezza];
                Marshal.Copy(pagina, chiaro, 0, lunghezza);
                return chiaro;
            } finally {
                Veil();
            }
        }

        // Sottochiave derivata senza che la chiave madre esca da qui.
        public byte[] Derive(string label) {
            byte[] madre = Reveal();
            try {
                using (HMACSHA256 h = new HMACSHA256(madre)) {
                    return h.ComputeHash(System.Text.Encoding.ASCII.GetBytes(label));
                }
            } finally {
                Array.Clear(madre, 0, madre.Length);
            }
        }

        public void Dispose() {
            if (dismessa) return;
            dismessa = true;
            if (pagina != IntPtr.Zero) {
                for (int i = 0; i < capienza; i++) Marshal.WriteByte(pagina, i, 0);
                if (bloccata) VirtualUnlock(pagina, (UIntPtr)(ulong)capienza);
                Marshal.FreeHGlobal(pagina);
                pagina = IntPtr.Zero;
            }
        }

        ~SecretKey() { Dispose(); }
    }
}
'@
}

$script:AppDir       = Join-Path $env:APPDATA 'DuckNote'
if (-not (Test-Path $script:AppDir)) { New-Item -ItemType Directory -Path $script:AppDir -Force | Out-Null }
$script:NoteFile     = Join-Path $script:AppDir 'note.xaml'
$script:LegacyFile   = Join-Path $env:USERPROFILE 'DuckNote.txt'
$script:SettingsFile = Join-Path $script:AppDir 'settings.json'
$script:ScanFile     = Join-Path $script:AppDir 'lastscan.json'
$script:OuiFile      = Join-Path $script:AppDir 'oui.txt'

$script:StoreFile    = Join-Path $script:AppDir 'store.bin'
$script:OldKeyFile   = Join-Path $script:AppDir 'vault.key'
$script:OldNoteFile  = Join-Path $script:AppDir 'note.dat'
$script:OldScanFile  = Join-Path $script:AppDir 'lastscan.dat'
$script:OldPrivFile  = Join-Path $script:AppDir 'private.dat'

$script:ScriptDir = if ($PSScriptRoot) { $PSScriptRoot }
                    elseif ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path }
                    else { (Get-Location).Path }
$script:DuckPngCandidates = @(
    (Join-Path $script:ScriptDir 'duck.png')
    (Join-Path $script:ScriptDir 'img\duck.png')
    (Join-Path $script:AppDir    'duck.png')
    (Join-Path $script:AppDir    'img\duck.png')
)
$script:DuckPngFile = $null
$script:DuckBitmap  = $null

if ($Reset) {
    foreach ($f in @($script:NoteFile,"$($script:NoteFile).bak",$script:SettingsFile,$script:ScanFile,
                     $script:StoreFile,"$($script:StoreFile).bak",$script:OldKeyFile,$script:OldNoteFile,
                     "$($script:OldNoteFile).bak",$script:OldScanFile,$script:OldPrivFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}

$script:Tokens = @{
    light = @{
        BgWindow        = '#8CFFFFFF'
        BgSidebar       = '#F4F4F5'
        BgToolbar       = '#FFFFFF'
        BgToolbarGlass  = '#99FFFFFF'
        BgSidebarGlass  = '#8CF4F4F5'
        BgContentGlass  = '#59EAEAEC'
        BgContent       = '#FFFFFF'
        BgGrouped       = '#F4F4F5'
        BgElevated      = '#FFFFFF'
        BgField         = '#FFFFFF'
        BgFieldAlt      = '#EFEFF1'
        BgRowAlt        = '#FAFAFB'
        BgHover         = '#ECECEE'
        BgPressed       = '#E0E0E3'
        Panel           = '#E6FFFFFF'
        PanelSoft       = '#DEF4F4F5'
        EdgeHighlight   = '#B3FFFFFF'
        Separator       = '#E2E2E4'
        SeparatorSoft   = '#EFEFF1'
        BorderControl   = '#D5D5D8'
        Label           = '#1F2328'
        LabelSecondary  = '#6A737D'
        LabelTertiary   = '#8D949C'
        LabelQuaternary = '#B6BCC2'
        LabelOnAccent   = '#FFFFFF'
        Blue            = '#2E6BE6'
        BlueDeep        = '#1D52C0'
        Accent          = '#FF8C00'
        AccentDark      = '#E07B00'
        AccentSoft      = '#1FFF8C00'
        AccentBorder    = '#59FF8C00'
        SelectionRowBg  = '#E4E4E8'
        SheetScrim      = '#D6FFFFFF'
        GlassEdge       = '#59FFFFFF'
        SelectionRowFg  = '#141619'
        AccentText      = '#1A1A1A'
        Green           = '#1F9D45'
        Red             = '#C0392B'
        Orange          = '#FF8C00'
        Yellow          = '#E0A100'
        Purple          = '#6D5FD6'
        Indigo          = '#4C46B8'
        Teal            = '#0E8C8C'
        Pink            = '#D6337A'
        Gray            = '#8D949C'
        DuckOrange      = '#FF8C00'
        GridMinor       = '#14000000'
        GridMajor       = '#33FF8C00'
        TLClose         = '#C0392B'
        TLMin           = '#FF8C00'
        TLZoom          = '#1F9D45'
        TLIdle          = '#CFCFD3'
        CodeBg          = '#F1F1F3'
        CodeFg          = '#B4009E'
        Highlight       = '#FFEBC2'
        HighlightFg     = '#1F2328'
        SelectionBg     = '#FFD9A6'
        TableHeaderBg   = '#F4F4F5'
        TableBorder     = '#E2E2E4'
        Shadow          = '#26000000'
    }
    dark = @{
        BgWindow        = '#8C121214'
        BgSidebar       = '#17171A'
        BgToolbar       = '#121214'
        BgToolbarGlass  = '#99121214'
        BgSidebarGlass  = '#8C17171A'
        BgContentGlass  = '#590E0E10'
        BgContent       = '#1C1C1F'
        BgGrouped       = '#17171A'
        BgElevated      = '#1C1C1F'
        BgField         = '#232327'
        BgFieldAlt      = '#17171A'
        BgRowAlt        = '#1F1F23'
        BgHover         = '#2A2A2F'
        BgPressed       = '#35353B'
        Panel           = '#E61C1C1F'
        PanelSoft       = '#DE17171A'
        EdgeHighlight   = '#26FFFFFF'
        Separator       = '#2C2C31'
        SeparatorSoft   = '#232327'
        BorderControl   = '#3A3A40'
        Label           = '#E6E7EA'
        LabelSecondary  = '#9AA0A6'
        LabelTertiary   = '#7B8188'
        LabelQuaternary = '#585D63'
        LabelOnAccent   = '#FFFFFF'
        Blue            = '#5B8DEF'
        BlueDeep        = '#7AA6F5'
        Accent          = '#FF9F2E'
        AccentDark      = '#E08A20'
        AccentSoft      = '#2BFF9F2E'
        AccentBorder    = '#59FF9F2E'
        SelectionRowBg  = '#33333A'
        SheetScrim      = '#CC17171B'
        GlassEdge       = '#33FFFFFF'
        SelectionRowFg  = '#FFFFFF'
        AccentText      = '#1A1A1A'
        Green           = '#5FCB7A'
        Red             = '#F1707B'
        Orange          = '#FF9F2E'
        Yellow          = '#FFC94D'
        Purple          = '#9385F0'
        Indigo          = '#A79BFF'
        Teal            = '#4FC3C7'
        Pink            = '#FF74B1'
        Gray            = '#9AA0A6'
        DuckOrange      = '#FF9F2E'
        GridMinor       = '#12FFFFFF'
        GridMajor       = '#38FF9F2E'
        TLClose         = '#F1707B'
        TLMin           = '#FF9F2E'
        TLZoom          = '#5FCB7A'
        TLIdle          = '#45454B'
        CodeBg          = '#232327'
        CodeFg          = '#FF9BEA'
        Highlight       = '#5A3A00'
        HighlightFg     = '#FFE9C7'
        SelectionBg     = '#8A4B00'
        TableHeaderBg   = '#232327'
        TableBorder     = '#2C2C31'
        Shadow          = '#66000000'
    }
}

$script:DuckGeometry = 'F1 M 86,30 A 18,18 0 1 1 50,30 A 18,18 0 1 1 86,30 Z ' +
                       'M 84,26.5 L 99,31.5 L 84,37 Z ' +
                       'M 56,38 L 80,38 L 76,68 L 54,68 Z ' +
                       'M 8,66 A 36,22 0 1 1 80,66 A 36,22 0 1 1 8,66 Z ' +
                       'M 14,52 L 2,38 L 26,47 Z'
$script:DuckEye     = 'M 74,29 A 3.4,3.4 0 1 1 67.2,29 A 3.4,3.4 0 1 1 74,29 Z'
$script:DuckW = 99.0
$script:DuckH = 100.0

$script:FontUI   = 'SF Pro Text, SF Pro Display, Segoe UI Variable Text, Segoe UI, Helvetica Neue, Arial'
$script:FontMono = 'SF Mono, Cascadia Mono, Consolas, Menlo, Courier New'

$script:Type = @{
    LargeTitle = 24; Title1 = 20; Title2 = 16; Title3 = 14
    Headline   = 13; Body   = 13; Callout = 12; Subheadline = 12
    Footnote   = 11; Caption = 10; Caption2 = 9
}

$script:Settings = [ordered]@{
    Theme                = 'light'
    FollowSystemTheme    = $true
    AutosaveEnabled      = $true
    AutosaveDebounceMs   = 1200
    LiveFormatting       = $true
    FormatDebounceMs     = 350
    HostDebounceMs       = 5000
    MonitorEnabled       = $true
    MonitorIntervalSec   = 60
    MaxThreads           = 64
    UseParallel          = $true
    PingCount            = 2
    PingTimeoutMs        = 800
    PortTimeoutMs        = 500
    ScanDeadHosts        = $true
    ResolveDns           = $true
    DnsServer            = ''
    ProbeNetBios         = $true
    ProbeMdns            = $true
    ProbeSsdp            = $true
    ProbeSnmp            = $true
    SnmpCommunity        = 'public'
    ProbeBanners         = $true
    ProbeShares          = $false
    ProbeWmi             = $false
    ProbeTraceHops       = $false
    Ports                = '21,22,23,25,53,80,110,135,139,143,443,445,465,554,587,631,993,995,1433,1723,3000,3306,3389,5000,5060,5432,5900,5985,5986,6379,8000,8006,8080,8443,8888,9100,32400'
    LastRange            = ''
    IgnoredHosts         = ''
    SidebarWidth         = 232
    InspectorWidth       = 330
    WindowWidth          = 1180
    WindowHeight         = 740
    DuckBackground       = $true
    DuckCount            = 22
    DuckOpacity          = 4
    EditorZoom           = 100
    SidebarMode          = 'host'
    ShowLineHighlight    = $true
    SecurityPrompted     = $false
    AutoLockMinutes      = 30
}

function Load-Settings {
    if (-not (Test-Path $script:SettingsFile)) { return }
    try {
        $j = Get-Content $script:SettingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($k in @($script:Settings.Keys)) {
            $v = $j.PSObject.Properties[$k]
            if ($null -eq $v -or $null -eq $v.Value) { continue }
            $cur = $script:Settings[$k]
            try {
                if     ($cur -is [bool])   { $script:Settings[$k] = [bool]$v.Value }
                elseif ($cur -is [int])    { $script:Settings[$k] = [int]$v.Value }
                elseif ($cur -is [double]) { $script:Settings[$k] = [double]$v.Value }
                else                       { $script:Settings[$k] = [string]$v.Value }
            } catch {}
        }
    } catch {}
    if ($script:Settings.MaxThreads -lt 1)        { $script:Settings.MaxThreads = 1 }
    if ($script:Settings.MaxThreads -gt 512)      { $script:Settings.MaxThreads = 512 }
    if ($script:Settings.PingTimeoutMs -lt 100)   { $script:Settings.PingTimeoutMs = 100 }
    if ($script:Settings.PortTimeoutMs -lt 80)    { $script:Settings.PortTimeoutMs = 80 }
    if ($script:Settings.MonitorIntervalSec -lt 5){ $script:Settings.MonitorIntervalSec = 5 }
    if ($script:Settings.Theme -notin @('light','dark')) { $script:Settings.Theme = 'light' }
    $dns = $null
    if ($script:Settings.DnsServer -and -not [Net.IPAddress]::TryParse($script:Settings.DnsServer, [ref]$dns)) {
        $script:Settings.DnsServer = ''
    }
}

$script:PrivateSettings = @('IgnoredHosts', 'LastRange')

$script:Dismesso = $false

function Save-Settings {
    if ($script:Dismesso) { return }
    try {
        $dump = [ordered]@{}
        $riservato = Test-VaultLocked
        foreach ($k in $script:Settings.Keys) {
            $dump[$k] = if ($riservato -and $script:PrivateSettings -contains $k) { '' }
                        else { $script:Settings[$k] }
        }
        [pscustomobject]$dump | ConvertTo-Json -Depth 4 |
            Out-File $script:SettingsFile -Encoding UTF8 -Force
    } catch {}
}

$script:VaultKdf = [ordered]@{ Memory = 262144; Passes = 3; Lanes = 4; Iterations = 600000 }
$script:VaultKey = $null
$script:Vault    = $null
$script:VaultSalt    = $null
$script:VaultWrapped = $null
$script:VaultParams  = $null
$script:UltimoBackup = $null
$script:UltimaRotazione = $null
$script:VaultBroken = $false

$script:SezioneNota      = 1
$script:SezioneNotaPrec  = 2
$script:SezioneScansione = 3
$script:SezionePrivati   = 4

function New-Entropy {
    param([int]$Count)
    $b = [byte[]]::new($Count)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($b) } finally { $rng.Dispose() }
    return ,$b
}

function Get-SubKey {
    param([byte[]]$Key, [string]$Label)
    $h = New-Object System.Security.Cryptography.HMACSHA256 (,$Key)
    try { return ,$h.ComputeHash([Text.Encoding]::ASCII.GetBytes($Label)) } finally { $h.Dispose() }
}

function Get-PaddedSize {
    param([int]$Utili)
    $scaglione = if ($Utili -lt 1MB) { 4096 } else { 65536 }
    return [int]([Math]::Ceiling(($Utili + 4) / $scaglione) * $scaglione)
}

function Add-Padding {
    param([byte[]]$Plain)
    if ($null -eq $Plain) { $Plain = [byte[]]::new(0) }
    $totale = Get-PaddedSize $Plain.Length
    $out = [byte[]]::new($totale)
    [Array]::Copy([BitConverter]::GetBytes([int]$Plain.Length), 0, $out, 0, 4)
    [Array]::Copy($Plain, 0, $out, 4, $Plain.Length)
    return ,$out
}

function Remove-Padding {
    param([byte[]]$Padded)
    if ($null -eq $Padded -or $Padded.Length -lt 4) { return $null }
    $utili = [BitConverter]::ToInt32($Padded, 0)
    if ($utili -lt 0 -or $utili -gt ($Padded.Length - 4)) { return $null }
    $out = [byte[]]::new($utili)
    [Array]::Copy($Padded, 4, $out, 0, $utili)
    return ,$out
}

function Protect-Bytes {
    param([byte[]]$Plain, [byte[]]$Enc, [byte[]]$Mac, [switch]$Padded)
    $iv  = New-Entropy 16
    [byte[]]$imbottito = if ($Padded) { Add-Padding $Plain } else { $Plain }
    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256; $aes.Key = $Enc; $aes.IV = $iv
        $aes.Mode = 'CBC'; $aes.Padding = 'PKCS7'
        $tr = $aes.CreateEncryptor()
        try { $ct = $tr.TransformFinalBlock($imbottito, 0, $imbottito.Length) } finally { $tr.Dispose() }
    } finally {
        $aes.Dispose()
        if ($Padded) { [Array]::Clear($imbottito, 0, $imbottito.Length) }
    }

    $head = [Text.Encoding]::ASCII.GetBytes($(if ($Padded) { 'DND2' } else { 'DNW2' }))
    $body = [byte[]]::new($head.Length + 16 + $ct.Length)
    [Array]::Copy($head, 0, $body, 0, $head.Length)
    [Array]::Copy($iv,   0, $body, $head.Length, 16)
    [Array]::Copy($ct,   0, $body, $head.Length + 16, $ct.Length)

    $h = New-Object System.Security.Cryptography.HMACSHA256 (,$Mac)
    try { $tag = $h.ComputeHash($body) } finally { $h.Dispose() }

    $out = [byte[]]::new($body.Length + 32)
    [Array]::Copy($body, 0, $out, 0, $body.Length)
    [Array]::Copy($tag,  0, $out, $body.Length, 32)
    return ,$out
}

function Unprotect-Bytes {
    param([byte[]]$Blob, [byte[]]$Enc, [byte[]]$Mac)
    if ($null -eq $Blob -or $Blob.Length -lt 68) { return $null }
    $marchio = [Text.Encoding]::ASCII.GetString($Blob, 0, 4)
    if ($marchio -notin @('DND2', 'DNW2', 'DND1')) { return $null }

    $bodyLen = $Blob.Length - 32
    $h = New-Object System.Security.Cryptography.HMACSHA256 (,$Mac)
    try { $tag = $h.ComputeHash($Blob, 0, $bodyLen) } finally { $h.Dispose() }
    $given = [byte[]]::new(32)
    [Array]::Copy($Blob, $bodyLen, $given, 0, 32)
    if (-not [DuckNative.Secret]::Equal($tag, $given)) { return $null }

    $iv = [byte[]]::new(16)
    [Array]::Copy($Blob, 4, $iv, 0, 16)
    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256; $aes.Key = $Enc; $aes.IV = $iv
        $aes.Mode = 'CBC'; $aes.Padding = 'PKCS7'
        $tr = $aes.CreateDecryptor()
        try { $chiaro = $tr.TransformFinalBlock($Blob, 20, $bodyLen - 20) } finally { $tr.Dispose() }
    } catch { return $null } finally { $aes.Dispose() }

    if ($marchio -ne 'DND2') { return ,$chiaro }
    return ,(Remove-Padding $chiaro)
}

function Protect-WithVaultKey {
    param([byte[]]$Plain)
    $dek = $script:VaultKey.Reveal()
    try {
        $enc = Get-SubKey $dek 'DuckNote/dati/cifratura/1'
        $mac = Get-SubKey $dek 'DuckNote/dati/integrita/1'
        try { return ,(Protect-Bytes -Plain $Plain -Enc $enc -Mac $mac -Padded) }
        finally { [Array]::Clear($enc, 0, 32); [Array]::Clear($mac, 0, 32) }
    } finally { [Array]::Clear($dek, 0, $dek.Length) }
}

function Unprotect-WithVaultKey {
    param([byte[]]$Blob)
    $dek = $script:VaultKey.Reveal()
    try {
        $enc = Get-SubKey $dek 'DuckNote/dati/cifratura/1'
        $mac = Get-SubKey $dek 'DuckNote/dati/integrita/1'
        try { return ,(Unprotect-Bytes -Blob $Blob -Enc $enc -Mac $mac) }
        finally { [Array]::Clear($enc, 0, 32); [Array]::Clear($mac, 0, 32) }
    } finally { [Array]::Clear($dek, 0, $dek.Length) }
}

function Split-Kek {
    param([byte[]]$Kek, [int]$Offset)
    $half = [byte[]]::new(32)
    [Array]::Copy($Kek, $Offset, $half, 0, 32)
    return ,$half
}

function Start-KeyDerivation {
    param([Security.SecureString]$Password, [byte[]]$Salt, $Kdf = $script:VaultKdf)
    $bytes = [DuckNative.Secret]::FromSecureString($Password)
    return [DuckNative.VaultKey]::Derive($bytes, $Salt, [int]$Kdf.Memory,
        [int]$Kdf.Passes, [int]$Kdf.Lanes, [int]$Kdf.Iterations)
}

function Set-VaultKey {
    param([byte[]]$Dek)
    Clear-VaultKey
    $script:VaultKey = [DuckNative.SecretKey]::Adopt($Dek)
    if ($null -eq $script:Vault) { $script:Vault = @{} }
}

function Clear-VaultKey {
    if ($script:VaultKey) { $script:VaultKey.Dispose() }
    $script:VaultKey = $null
    if ($script:Vault) {
        foreach ($k in @($script:Vault.Keys)) {
            $v = $script:Vault[$k]
            if ($v -is [byte[]]) { [Array]::Clear($v, 0, $v.Length) }
        }
        $script:Vault.Clear()
    }
    $script:Vault = $null
    $script:VaultBroken = $false
}

function Test-VaultLocked {
    return ((Test-Path $script:StoreFile) -or (Test-Path $script:OldKeyFile))
}
function Test-VaultOpen   { return ($null -ne $script:VaultKey) }

function Hide-FileTime {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        $fissa = [DateTime]::new(2020, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
        $f = Get-Item -LiteralPath $Path -Force
        $f.CreationTimeUtc   = $fissa
        $f.LastWriteTimeUtc  = $fissa
        $f.LastAccessTimeUtc = $fissa
    } catch {}
}

function Get-VaultSection {
    param([int]$Id)
    if ($null -eq $script:Vault -or -not $script:Vault.ContainsKey($Id)) { return $null }
    return ,$script:Vault[$Id]
}

function Set-VaultSection {
    param([int]$Id, [byte[]]$Bytes)
    if ($null -eq $script:Vault) { return }
    $vecchio = $script:Vault[$Id]
    if ($vecchio -is [byte[]]) { [Array]::Clear($vecchio, 0, $vecchio.Length) }
    if ($null -eq $Bytes -or $Bytes.Length -eq 0) { [void]$script:Vault.Remove($Id) }
    else { $script:Vault[$Id] = $Bytes }
}

function Write-VaultSections {
    $ms = New-Object System.IO.MemoryStream
    $w  = New-Object System.IO.BinaryWriter $ms
    try {
        $ordinate = @($script:Vault.Keys | Sort-Object)
        $w.Write([int]$ordinate.Count)
        foreach ($id in $ordinate) {
            [byte[]]$dati = $script:Vault[$id]
            $w.Write([byte]$id)
            $w.Write([int]$dati.Length)
            $w.Write($dati)
        }
        $w.Flush()
        return ,$ms.ToArray()
    } finally { $w.Dispose(); $ms.Dispose() }
}

function Read-VaultSections {
    param([byte[]]$Plain)
    $sezioni = @{}
    $ms = New-Object System.IO.MemoryStream (,$Plain)
    $r  = New-Object System.IO.BinaryReader $ms
    try {
        $quante = $r.ReadInt32()
        if ($quante -lt 0 -or $quante -gt 32) { return $null }
        for ($i = 0; $i -lt $quante; $i++) {
            $id  = [int]$r.ReadByte()
            $len = $r.ReadInt32()
            if ($len -lt 0 -or $len -gt ($ms.Length - $ms.Position)) { return $null }
            $sezioni[$id] = $r.ReadBytes($len)
        }
        return $sezioni
    } catch { return $null } finally { $r.Dispose(); $ms.Dispose() }
}

function Rotate-NoteSection {
    $scaduta = ($null -eq $script:UltimaRotazione) -or
               (((Get-Date) - $script:UltimaRotazione) -ge [TimeSpan]::FromMinutes(15))
    if (-not $scaduta) { return }
    [byte[]]$attuale = Get-VaultSection $script:SezioneNota
    if ($null -eq $attuale) { return }
    $copia = [byte[]]::new($attuale.Length)
    [Array]::Copy($attuale, $copia, $attuale.Length)
    Set-VaultSection $script:SezioneNotaPrec $copia
    $script:UltimaRotazione = Get-Date
}

function Save-VaultStore {
    if ($script:Dismesso -or -not (Test-VaultOpen)) { return }
    [byte[]]$sezioni = Write-VaultSections
    try {
        [byte[]]$payload = Protect-WithVaultKey $sezioni
    } finally { [Array]::Clear($sezioni, 0, $sezioni.Length) }

    $ms = New-Object System.IO.MemoryStream
    $w  = New-Object System.IO.BinaryWriter $ms
    try {
        $w.Write([Text.Encoding]::ASCII.GetBytes('DNVAULT2'))
        $w.Write([byte]2)
        $w.Write([int]$script:VaultParams.Memory)
        $w.Write([int]$script:VaultParams.Passes)
        $w.Write([byte]$script:VaultParams.Lanes)
        $w.Write([int]$script:VaultParams.Iterations)
        $w.Write([byte]$script:VaultSalt.Length)
        $w.Write($script:VaultSalt)
        $w.Write([int]$script:VaultWrapped.Length)
        $w.Write($script:VaultWrapped)
        $w.Write([int]$payload.Length)
        $w.Write($payload)
        $w.Flush()

        Backup-VaultStore
        $tmp = "$($script:StoreFile).tmp"
        [IO.File]::WriteAllBytes($tmp, $ms.ToArray())
        Move-Item -Path $tmp -Destination $script:StoreFile -Force
        Hide-FileTime $script:StoreFile
    } finally { $w.Dispose(); $ms.Dispose() }
}

function Backup-VaultStore {
    $scaduto = ($null -eq $script:UltimoBackup) -or
               (((Get-Date) - $script:UltimoBackup) -ge [TimeSpan]::FromMinutes(15))
    if (-not $scaduto -or -not (Test-Path $script:StoreFile)) { return }
    try {
        Copy-Item -Path $script:StoreFile -Destination "$($script:StoreFile).bak" -Force
        Hide-FileTime "$($script:StoreFile).bak"
        $script:UltimoBackup = Get-Date
    } catch {}
}

function Read-VaultHeader {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $null }
    try {
        $raw = [IO.File]::ReadAllBytes($Path)
        if ($raw.Length -lt 80 -or [Text.Encoding]::ASCII.GetString($raw, 0, 8) -ne 'DNVAULT2') { return $null }
        $ms = New-Object System.IO.MemoryStream (,$raw)
        $r  = New-Object System.IO.BinaryReader $ms
        try {
            [void]$r.ReadBytes(8)
            if ($r.ReadByte() -ne 2) { return $null }
            $kdf = [ordered]@{
                Memory     = $r.ReadInt32()
                Passes     = $r.ReadInt32()
                Lanes      = [int]$r.ReadByte()
                Iterations = $r.ReadInt32()
            }
            $saltLen = $r.ReadByte()
            if ($saltLen -lt 8 -or $saltLen -gt 64) { return $null }
            $salt = $r.ReadBytes($saltLen)
            $wrappedLen = $r.ReadInt32()
            if ($wrappedLen -lt 68 -or $wrappedLen -gt 4096) { return $null }
            $wrapped = $r.ReadBytes($wrappedLen)
            $payloadLen = $r.ReadInt32()
            if ($payloadLen -lt 0 -or $payloadLen -gt ($ms.Length - $ms.Position)) { return $null }
            return @{ Kdf = $kdf; Salt = $salt; Wrapped = $wrapped; Payload = $r.ReadBytes($payloadLen) }
        } finally { $r.Dispose(); $ms.Dispose() }
    } catch { return $null }
}

function Open-VaultWithKek {
    param([hashtable]$Header, [byte[]]$Kek)
    $dek = Unprotect-Bytes -Blob $Header.Wrapped -Enc (Split-Kek $Kek 0) -Mac (Split-Kek $Kek 32)
    if ($null -eq $dek -or $dek.Length -ne 32) { return $false }

    $script:VaultSalt    = $Header.Salt
    $script:VaultWrapped = $Header.Wrapped
    $script:VaultParams  = $Header.Kdf
    Set-VaultKey $dek

    if ($Header.Payload -and $Header.Payload.Length -gt 0) {
        [byte[]]$chiaro = Unprotect-WithVaultKey $Header.Payload
        if ($null -eq $chiaro) {
            $script:VaultBroken = $true
            $script:Vault = @{}
            return $true
        }
        try {
            $sezioni = Read-VaultSections $chiaro
            if ($null -eq $sezioni) { $script:VaultBroken = $true; $script:Vault = @{} }
            else { $script:Vault = $sezioni }
        } finally { [Array]::Clear($chiaro, 0, $chiaro.Length) }
    }
    return $true
}

function New-VaultStore {
    param([byte[]]$Kek, $Kdf = $script:VaultKdf, [byte[]]$Salt)
    $dek = New-Entropy 32
    $script:VaultSalt    = $Salt
    $script:VaultParams  = $Kdf
    $script:VaultWrapped = Protect-Bytes -Plain $dek -Enc (Split-Kek $Kek 0) -Mac (Split-Kek $Kek 32)
    Set-VaultKey $dek
    $script:Vault = @{}
    Save-VaultStore
}

function Update-VaultWrapper {
    param([byte[]]$Kek, $Kdf, [byte[]]$Salt)
    $dek = $script:VaultKey.Reveal()
    try {
        $script:VaultWrapped = Protect-Bytes -Plain $dek -Enc (Split-Kek $Kek 0) -Mac (Split-Kek $Kek 32)
    } finally { [Array]::Clear($dek, 0, $dek.Length) }
    $script:VaultSalt   = $Salt
    $script:VaultParams = $Kdf
    Save-VaultStore
}

function Read-LegacyKeystore {
    if (-not (Test-Path $script:OldKeyFile)) { return $null }
    try {
        $raw = [IO.File]::ReadAllBytes($script:OldKeyFile)
        if ($raw.Length -lt 60 -or [Text.Encoding]::ASCII.GetString($raw, 0, 6) -ne 'DNKEY1') { return $null }
        $ms = New-Object System.IO.MemoryStream (,$raw)
        $r  = New-Object System.IO.BinaryReader $ms
        try {
            [void]$r.ReadBytes(6)
            if ($r.ReadByte() -ne 1) { return $null }
            $kdf = [ordered]@{
                Memory     = $r.ReadInt32()
                Passes     = $r.ReadInt32()
                Lanes      = [int]$r.ReadByte()
                Iterations = $r.ReadInt32()
            }
            $saltLen = $r.ReadByte()
            if ($saltLen -lt 8 -or $saltLen -gt 64) { return $null }
            $salt = $r.ReadBytes($saltLen)
            return @{ Kdf = $kdf; Salt = $salt; Wrapped = $r.ReadBytes([int]($ms.Length - $ms.Position)) }
        } finally { $r.Dispose(); $ms.Dispose() }
    } catch { return $null }
}

function Import-LegacyVault {
    param([hashtable]$Header, [byte[]]$Kek)
    $dek = Unprotect-Bytes -Blob $Header.Wrapped -Enc (Split-Kek $Kek 0) -Mac (Split-Kek $Kek 32)
    if ($null -eq $dek -or $dek.Length -ne 32) { return $false }

    $script:VaultSalt    = $Header.Salt
    $script:VaultParams  = $Header.Kdf
    $script:VaultWrapped = $Header.Wrapped
    Set-VaultKey $dek

    foreach ($voce in @(@($script:OldNoteFile,             $script:SezioneNota),
                        @("$($script:OldNoteFile).bak",    $script:SezioneNotaPrec),
                        @($script:OldScanFile,             $script:SezioneScansione),
                        @($script:OldPrivFile,             $script:SezionePrivati))) {
        if (-not (Test-Path $voce[0])) { continue }
        [byte[]]$chiaro = Unprotect-WithVaultKey ([IO.File]::ReadAllBytes($voce[0]))
        if ($chiaro) { Set-VaultSection $voce[1] $chiaro }
    }

    Update-VaultWrapper -Kek $Kek -Kdf $Header.Kdf -Salt $Header.Salt
    foreach ($f in @($script:OldNoteFile, "$($script:OldNoteFile).bak", $script:OldScanFile,
                     $script:OldPrivFile, $script:OldKeyFile)) {
        Remove-FileSecurely $f
    }
    return $true
}

function Remove-PlaintextLeftovers {
    if (-not (Test-VaultLocked)) { return }
    $trovati = @()
    foreach ($f in @($script:NoteFile, "$($script:NoteFile).bak", $script:ScanFile)) {
        if (Test-Path $f) {
            Remove-FileSecurely $f
            $trovati += (Split-Path $f -Leaf)
        }
    }
    if ($trovati.Count -gt 0) {
        Set-Status ('Rimossi dalla cartella i file in chiaro rimasti: {0}.' -f ($trovati -join ', '))
    }
}

function Remove-FileSecurely {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    try {
        $len = (Get-Item -LiteralPath $Path -Force).Length
        if ($len -gt 0) {
            $fs = [IO.File]::OpenWrite($Path)
            try {
                $chunk = New-Entropy ([Math]::Min($len, 65536))
                $scritti = 0
                while ($scritti -lt $len) {
                    $n = [Math]::Min($chunk.Length, $len - $scritti)
                    $fs.Write($chunk, 0, $n)
                    $scritti += $n
                }
                $fs.Flush()
            } finally { $fs.Close() }
        }
    } catch {}
    try { Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue } catch {}
}

$script:Ignored = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

function Read-PrivateData {
    if (-not (Test-VaultOpen)) { return }
    [byte[]]$bytes = Get-VaultSection $script:SezionePrivati
    if (-not $bytes) { return }
    try {
        $priv = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
        if ($priv.IgnoredHosts) { $script:Settings.IgnoredHosts = [string]$priv.IgnoredHosts }
        if ($priv.LastRange)    { $script:Settings.LastRange    = [string]$priv.LastRange }
    } catch {}
}

function Read-IgnoredHosts {
    $script:Ignored.Clear()
    foreach ($h in ($script:Settings.IgnoredHosts -split '[,;\s]+')) {
        if ($h) { [void]$script:Ignored.Add($h) }
    }
}

function Save-IgnoredHosts {
    $script:Settings.IgnoredHosts = (@($script:Ignored | Sort-Object) -join ',')
    Save-Settings
    Save-PrivateData
}

function Save-PrivateData {
    if (-not (Test-VaultOpen)) { return }
    try {
        $json = [pscustomobject]@{
            IgnoredHosts = [string]$script:Settings.IgnoredHosts
            LastRange    = [string]$script:Settings.LastRange
        } | ConvertTo-Json -Depth 3
        Set-VaultSection $script:SezionePrivati ([Text.Encoding]::UTF8.GetBytes($json))
        Save-VaultStore
    } catch {}
}

function Get-ScriptFingerprint {
    $percorso = if ($PSCommandPath) { $PSCommandPath }
                elseif ($MyInvocation.MyCommand.Path) { $MyInvocation.MyCommand.Path }
                else { $null }
    if (-not $percorso -or -not (Test-Path -LiteralPath $percorso)) {
        return @{ Hash = '(non determinabile)'; Path = '(sconosciuto)' }
    }
    try {
        $h = (Get-FileHash -LiteralPath $percorso -Algorithm SHA256).Hash.ToLowerInvariant()
        return @{ Hash = $h; Path = $percorso }
    } catch {
        return @{ Hash = '(non leggibile)'; Path = $percorso }
    }
}

function Get-SystemTheme {
    try {
        $k = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize'
        $v = (Get-ItemProperty -Path $k -Name AppsUseLightTheme -ErrorAction Stop).AppsUseLightTheme
        if ($v -eq 0) { return 'dark' }
        return 'light'
    } catch { return 'light' }
}

$script:OuiRaw = @'
000393=Apple;000502=Apple;000A27=Apple;000A95=Apple;001B63=Apple;001EC2=Apple
002312=Apple;002500=Apple;3C0754=Apple;406C8F=Apple;68A86D=Apple;7C6D62=Apple
90B21F=Apple;A45E60=Apple;ACBC32=Apple;B8E856=Apple;D0E140=Apple;F099BF=Apple
F4F15A=Apple;8866A5=Apple;A8667F=Apple;DC2B2A=Apple;E0B9BA=Apple;F0DBF8=Apple
000C29=VMware;005056=VMware;000569=VMware;001C14=VMware;080027=VirtualBox
00155D=Microsoft Hyper-V;0003FF=Microsoft;000D3A=Microsoft Azure;0050F2=Microsoft
00125A=Microsoft;001DD8=Microsoft;281878=Microsoft;501AC5=Microsoft;5882A8=Microsoft
6045BD=Microsoft;7C1E52=Microsoft;983FD3=Microsoft;C83F26=Microsoft
00163E=Xen;001C42=Parallels;525400=QEMU/KVM;0A0027=VirtualBox host
B827EB=Raspberry Pi;DCA632=Raspberry Pi;E45F01=Raspberry Pi;D83ADD=Raspberry Pi
28CDC1=Raspberry Pi;2CCF67=Raspberry Pi
240AC4=Espressif;30AEA4=Espressif;3C71BF=Espressif;5CCF7F=Espressif;84F3EB=Espressif
A4CF12=Espressif;B4E62D=Espressif;ECFABC=Espressif;7C9EBD=Espressif;C44F33=Espressif
840D8E=Espressif;8CAAB5=Espressif;4C11AE=Espressif;E8DB84=Espressif
001B21=Intel;001E67=Intel;0024D7=Intel;3C970E=Intel;8C1645=Intel;A0A8CD=Intel
94659C=Intel;001517=Intel;3413E8=Intel;7C7A91=Intel;E4A471=Intel;98AF65=Intel
00000C=Cisco;001AA1=Cisco;001B0C=Cisco;002304=Cisco;002699=Cisco;34BDC8=Cisco
500604=Cisco;68BC0C=Cisco;F09E63=Cisco;000E38=Cisco;00E0FE=Cisco;0025B4=Cisco
881544=Cisco Meraki;E0CBBC=Cisco Meraki;00180A=Cisco Meraki
00156D=Ubiquiti;0418D6=Ubiquiti;24A43C=Ubiquiti;44D9E7=Ubiquiti;687251=Ubiquiti
7483C2=Ubiquiti;788A20=Ubiquiti;802AA8=Ubiquiti;B4FBE4=Ubiquiti;DC9FDB=Ubiquiti
F09FC2=Ubiquiti;FCECDA=Ubiquiti;74ACB9=Ubiquiti;E43883=Ubiquiti
000C42=MikroTik;4C5E0C=MikroTik;6C3B6B=MikroTik;744D28=MikroTik;B869F4=MikroTik
C4AD34=MikroTik;488F5A=MikroTik;DC2C6E=MikroTik;2CC81B=MikroTik;789A18=MikroTik
001D0F=TP-Link;14CC20=TP-Link;50C7BF=TP-Link;60E327=TP-Link;98DAC4=TP-Link
A0F3C1=TP-Link;C025E9=TP-Link;EC086B=TP-Link;F4F26D=TP-Link;50D2F5=TP-Link
B0BE76=TP-Link;30DE4B=TP-Link;9C5322=TP-Link;AC84C6=TP-Link
00095B=Netgear;00146C=Netgear;001B2F=Netgear;204E7F=Netgear;4494FC=Netgear
A040A0=Netgear;C03F0E=Netgear;9C3DCF=Netgear;3894ED=Netgear
00055D=D-Link;001B11=D-Link;14D64D=D-Link;1C7EE5=D-Link;28107B=D-Link
5CD998=D-Link;78321B=D-Link;B8A386=D-Link;C8BE19=D-Link
001BFC=ASUS;08606E=ASUS;1C872C=ASUS;2C56DC=ASUS;305A3A=ASUS;38D547=ASUS
50465D=ASUS;704D7B=ASUS;AC220B=ASUS;D850E6=ASUS;F832E4=ASUS;1CB72C=ASUS
000C41=Linksys;001217=Linksys;0014BF=Linksys;001839=Linksys;001A70=Linksys
002129=Linksys;00226B=Linksys;002369=Linksys;20AA4B=Linksys;48F8B3=Linksys
586D8F=Linksys;687F74=Linksys;C05627=Belkin;EC1A59=Belkin
001132=Synology;9009D0=Synology;0011D8=Synology;001D2E=Synology
00089B=QNAP;245EBE=QNAP;00089C=QNAP
0090A9=Western Digital;0014EE=Western Digital;00074D=Zebra;001570=Zebra
001A8C=Sophos;001B17=Palo Alto;B40C25=Palo Alto;001C7F=Check Point
00090F=Fortinet;085B0E=Fortinet;704CA5=Fortinet;906CAC=Fortinet;E023FF=Fortinet
000585=Juniper;2C6BF5=Juniper;3C6104=Juniper;7819F7=Juniper;88E0F3=Juniper
F4CC55=Juniper;54E032=Juniper
000B86=Aruba;04BD88=Aruba;186472=Aruba;204C03=Aruba;24DEC6=Aruba;6CF37F=Aruba
84D47E=Aruba;94B40F=Aruba;B45D50=Aruba;D8C7C8=Aruba
000496=Extreme Networks;00E02B=Extreme Networks
001D2E=Ruckus;2CC5D3=Ruckus;589396=Ruckus;6CAAB3=Ruckus;C0C520=Ruckus;F03E90=Ruckus
001349=Zyxel;0019CB=Zyxel;404A03=Zyxel;5CF4AB=Zyxel;90EF68=Zyxel;B0B2DC=Zyxel
DC4BDD=Zyxel
000F20=HP;001B78=HP;00215A=HP;0025B3=HP;2C4138=HP;3CD92B=HP;6C3BE5=HP
9457A5=HP;98E7F4=HP;B4B52F=HP;D0BF9C=HP;0017A4=HP
0001E6=HP (stampante);00110A=HP (stampante);3C5282=HP (stampante)
9CB654=HP (stampante);D4C9EF=HP (stampante);705A0F=HP (stampante);001F29=HP (stampante)
001422=Dell;001EC9=Dell;00219B=Dell;0024E8=Dell;14FEB5=Dell;1866DA=Dell
24B6FD=Dell;3417EB=Dell;44A842=Dell;54BF64=Dell;74E6E2=Dell;B083FE=Dell
D067E5=Dell;F8BC12=Dell;00B0D0=Dell;90B11C=Dell;F8DB88=Dell
089E01=Lenovo;507B9D=Lenovo;54EE75=Lenovo;6C5F1C=Lenovo;70720D=Lenovo;E86A64=Lenovo
00145E=IBM;00215E=IBM;E41F13=IBM;6CAE8B=IBM
002590=Supermicro;0CC47A=Supermicro;3CECEF=Supermicro;AC1F6B=Supermicro
0012FB=Samsung;001599=Samsung;001632=Samsung;0808C2=Samsung;183F47=Samsung
3423BA=Samsung;5C0A5B=Samsung;781FDB=Samsung;8C71F8=Samsung;BC20A4=Samsung
C819F7=Samsung;E8508B=Samsung;F05A09=Samsung
009EC8=Xiaomi;04CF8C=Xiaomi;286C07=Xiaomi;34CE00=Xiaomi;508F4C=Xiaomi
640980=Xiaomi;742344=Xiaomi;7811DC=Xiaomi;8CBEBE=Xiaomi;A45046=Xiaomi
F0B429=Xiaomi;FC64BA=Xiaomi
001882=Huawei;00E0FC=Huawei;04BD70=Huawei;104780=Huawei;20F3A3=Huawei
283152=Huawei;4846FB=Huawei;5C7D5E=Huawei;70723C=Huawei;84A8E4=Huawei
ACE215=Huawei;E0247F=Huawei;F49FF3=Huawei
0015EB=ZTE;344B50=ZTE;4CAC0A=ZTE;986CF5=ZTE;D05BA8=ZTE
001C62=LG;001E75=LG;10F96F=LG;2C54CF=LG;344DF7=LG;58A2B5=LG;88C9D0=LG
A816D0=LG;CC2D8C=LG
0013A9=Sony;0019C5=Sony;001DBA=Sony;0024BE=Sony;080046=Sony;30F9ED=Sony
544249=Sony;AC9B0A=Sony;FC0FE6=Sony PlayStation
0009BF=Nintendo;0017AB=Nintendo;001AE9=Nintendo;182A7B=Nintendo;34AF2C=Nintendo
58BDA3=Nintendo;98B6E9=Nintendo;E84ECE=Nintendo
08A6BC=Amazon;0C47C9=Amazon;34D270=Amazon;38F73D=Amazon;44650D=Amazon
50DCE7=Amazon;6837E9=Amazon;74C246=Amazon;A002DC=Amazon;B47C9C=Amazon
F0272D=Amazon;FCA183=Amazon
001A11=Google;3C5AB4=Google;546009=Google;641666=Google;6CADF8=Google
A47733=Google;F4F5D8=Google;F4F5E8=Google;20DFB9=Google;DAA119=Google
000E58=Sonos;347E5C=Sonos;48A6B8=Sonos;5CAAFD=Sonos;7828CA=Sonos;949F3E=Sonos
B8E937=Sonos
B0A737=Roku;CC6DA0=Roku;D83134=Roku;DC3A5E=Roku;88DEA9=Roku
001788=Philips Hue;ECB5FA=Philips Hue;70EE50=Netatmo
0452C7=Bose;08DF1F=Bose;2C9FFB=Bose;0005CD=Denon/Marantz;00A0DE=Yamaha
00040E=AVM FRITZ!;0896D7=AVM FRITZ!;246511=AVM FRITZ!;3431C4=AVM FRITZ!
3810D5=AVM FRITZ!;5C4979=AVM FRITZ!;9CC7A6=AVM FRITZ!;C02506=AVM FRITZ!;E0286D=AVM FRITZ!
00147F=Technicolor;001A2B=Technicolor;30918F=Technicolor;4432C8=Technicolor
842615=Technicolor;B04E26=Technicolor
3C81D8=Sagemcom;6002B4=Sagemcom;68A378=Sagemcom;788102=Sagemcom;880355=Sagemcom
9C9726=Sagemcom;F0842F=Sagemcom
001596=Arris;001DCD=Arris;002636=Arris;3C04BF=Arris;749D8F=Arris;94877C=Arris
B077AC=Arris;D404CD=Arris;F8F532=Arris
000FCC=Sercomm;84A423=Sercomm;8C04FF=Sercomm;C83A35=Tenda;0495E6=Tenda;048D38=Netis
000740=Buffalo;001601=Buffalo;4CE676=Buffalo;DCFB02=Buffalo
4419B6=Hikvision;4CBD8F=Hikvision;54C415=Hikvision;8CE748=Hikvision;BCAD28=Hikvision
C056E3=Hikvision;E0CA3C=Hikvision;28572C=Hikvision
3CEF8C=Dahua;4C11BF=Dahua;9002A9=Dahua;E0508B=Dahua;14A78B=Dahua
00408C=Axis;ACCC8E=Axis;B8A44F=Axis
001BA9=Brother;008077=Brother;30055C=Brother;3C2AF4=Brother;780473=Brother
8CD9D6=Brother;B42200=Brother
000085=Canon;001E8F=Canon;180CAC=Canon;2C9EFC=Canon;3C8D20=Canon;888717=Canon
D4C93C=Canon
000048=Epson;0026AB=Epson;381A52=Epson;44D244=Epson;64EB8C=Epson;9CAED3=Epson
A4EE57=Epson;B0E892=Epson
000074=Ricoh;002673=Ricoh;583879=Ricoh;0000AA=Xerox;002085=Xerox;080037=Xerox
9C934E=Xerox;000400=Lexmark;002000=Lexmark;0021B7=Lexmark;002303=Lexmark
0026B9=Lexmark;685B35=Lexmark;0017C8=Kyocera;00C0EE=Kyocera
000B82=Grandstream;C074AD=Grandstream;EC74D7=Grandstream
001565=Yealink;249AD8=Yealink;805EC0=Yealink;0004F2=Polycom;64167F=Polycom
000413=Snom;00040D=Avaya;001B4F=Avaya;3CB15B=Avaya
00E04C=Realtek;001018=Broadcom;0005B5=Broadcom;005043=Marvell
000039=Toshiba;00080D=Toshiba;00266C=Toshiba;00000E=Fujitsu;001742=Fujitsu
901B0E=Fujitsu;0080F0=Panasonic;000B97=Panasonic
4CFCAA=Tesla;98ED5C=Tesla
001A11=Google;3C5AB4=Google;544E90=Google;F4F5D8=Google;F4F5E8=Google;A47733=Google
6466B3=Google Nest;18B430=Google Nest;D8EB46=Google;9C4B44=Google
0071C2=Amazon;34D270=Amazon;40B4CD=Amazon;44650D=Amazon;68374A=Amazon;6C5697=Amazon
74C246=Amazon;848A8D=Amazon;A002DC=Amazon;AC63BE=Amazon;B47C9C=Amazon;F0272D=Amazon
FC65DE=Amazon;50DCE7=Amazon;0C47C9=Amazon;38F73D=Amazon
000E58=Sonos;5CAAFD=Sonos;7828CA=Sonos;949F3E=Sonos;B8E937=Sonos;347E5C=Sonos
000D4B=Roku;08052E=Roku;B0A737=Roku;CC6DA0=Roku;D83134=Roku;AC3A7A=Roku
D0035C=AVM FRITZ!Box;3C37E6=AVM;C80E14=AVM;E0286D=AVM;5C4979=AVM;38102B=AVM
9C6866=AVM;24654A=AVM;BCB55A=AVM
0017C2=Technicolor;002324=Technicolor;3872C0=Technicolor;7C03D8=Technicolor
A0A3E2=Technicolor;C4EA1D=Technicolor;F87B7A=Technicolor
0011F5=Sagemcom;0026B8=Sagemcom;1CB03C=Sagemcom;5CDC96=Sagemcom;7C26F4=Sagemcom
C05627=Sagemcom;E01954=Sagemcom
001DD0=Arris;002326=Arris;0894EF=Arris;3C7A8A=Arris;6C5A2A=Arris;9C3426=Arris
C4EB42=Arris;F8F532=Arris
000FE2=Huawei;00E0FC=Huawei;001882=Huawei;002568=Huawei;04BD70=Huawei;086361=Huawei
104780=Huawei;24DBAC=Huawei;283152=Huawei;308730=Huawei;3CDFBD=Huawei;480031=Huawei
508F4C=Huawei;5CA86A=Huawei;70723C=Huawei;7C6097=Huawei;884477=Huawei;9C28EF=Huawei
AC853D=Huawei;B41513=Huawei;C81451=Huawei;D0374C=Huawei;E0247F=Huawei;F49FF3=Huawei
0015EB=ZTE;004A77=ZTE;344B50=ZTE;4CAC0A=ZTE;646E6C=ZTE;789682=ZTE;8CE117=ZTE
9CA9E4=ZTE;D0154A=ZTE;F46DE2=ZTE
0023CD=TP-Link;002719=TP-Link;144D67=TP-Link;1C3BF3=TP-Link;30B5C2=TP-Link
3C4610=TP-Link;50C7BF=TP-Link;60E327=TP-Link;7CC2C6=TP-Link;9C5322=TP-Link
A42BB0=TP-Link;AC84C6=TP-Link;B0487A=TP-Link;C006C3=TP-Link;C46E1F=TP-Link
D80D17=TP-Link;E848B8=TP-Link;F0A731=TP-Link;003192=TP-Link;5CA6E6=TP-Link
000FB5=Netgear;00095B=Netgear;001B2F=Netgear;008EF2=Netgear;2C3033=Netgear
3C3786=Netgear;405D82=Netgear;6CB0CE=Netgear;841B5E=Netgear;A00460=Netgear
B03956=Netgear;C03F0E=Netgear;E0469A=Netgear;E8FCAF=Netgear;9CD36D=Netgear
001195=D-Link;002191=D-Link;14D64D=D-Link;1CBDB9=D-Link;28107B=D-Link;340804=D-Link
5CD998=D-Link;78542E=D-Link;90940A=D-Link;B8A386=D-Link;C4A81D=D-Link;CCB255=D-Link
001731=ASUS;001BFC=ASUS;002354=ASUS;08606E=ASUS;10BF48=ASUS;1C872C=ASUS;2C56DC=ASUS
305A3A=ASUS;38D547=ASUS;40167E=ASUS;50465D=ASUS;704D7B=ASUS;7824AF=ASUS;9C5C8E=ASUS
AC220B=ASUS;BCEE7B=ASUS;D017C2=ASUS;E03F49=ASUS;F832E4=ASUS;04D4C4=ASUS
0014BF=Linksys;0018F8=Linksys;002369=Linksys;002637=Linksys;20AA4B=Linksys
48F8B3=Linksys;586D8F=Linksys;C0C1C0=Linksys;C8D719=Linksys
0011D8=Ubiquiti;002722=Ubiquiti;0418D6=Ubiquiti;24A43C=Ubiquiti;44D9E7=Ubiquiti
68725A=Ubiquiti;6CE873=Ubiquiti;74ACB9=Ubiquiti;788A20=Ubiquiti;802AA8=Ubiquiti
B4FBE4=Ubiquiti;DC9FDB=Ubiquiti;E063DA=Ubiquiti;F09FC2=Ubiquiti;FCECDA=Ubiquiti
0000AB=MikroTik;08553B=MikroTik;18FD74=MikroTik;2CC81B=MikroTik;48A98A=MikroTik
4C5E0C=MikroTik;6C3B6B=MikroTik;744D28=MikroTik;B869F4=MikroTik;C4AD34=MikroTik
CC2DE0=MikroTik;D4CA6D=MikroTik;E48D8C=MikroTik;F41E57=MikroTik;DC2C6E=MikroTik
000C42=Routerboard;64D154=Routerboard
0009B7=Cisco;000A41=Cisco;001121=Cisco;0018BA=Cisco;001B0C=Cisco;002155=Cisco
00259C=Cisco;08CC68=Cisco;1CDEA7=Cisco;24E9B3=Cisco;3C0E23=Cisco;4403A7=Cisco
502F A8=Cisco;5C5015=Cisco;6416F0=Cisco;70CA9B=Cisco;88F031=Cisco;A0EC F9=Cisco
B838 61=Cisco;C4143C=Cisco;D0C789=Cisco;E8B748=Cisco;F02572=Cisco;F866F2=Cisco
0016B6=Cisco-Linksys;001A70=Cisco-Linksys;687F74=Cisco-Linksys
0001E6=HP;000883=HP;001279=HP;0017A4=HP;001CC4=HP;0021 5A=HP;002481=HP;00306E=HP
1458D0=HP;3464A9=HP;3C4A92=HP;40A8F0=HP;5065F3=HP;6CC217=HP;941882=HP;9CB654=HP
A0481C=HP;B00CD1=HP;D89D67=HP;E4E749=HP;F4CE46=HP;308D99=HP;0080A0=HP
001143=Dell;0014 22=Dell;0018 8B=Dell;001EC9=Dell;002219=Dell;00265E=Dell
14FEB5=Dell;18A99B=Dell;20040F=Dell;246E96=Dell;34E6D7=Dell;44A842=Dell;5448 10=Dell
782BCB=Dell;8CEC4B=Dell;B083FE=Dell;B885 84=Dell;D067E5=Dell;E4F004=Dell;F8BC12=Dell
0006 1B=Lenovo;0012FE=Lenovo;001A6B=Lenovo;3C970E=Lenovo;50 7B9D=Lenovo;68F728=Lenovo
8CDCD4=Lenovo;A4C494=Lenovo;C85B76=Lenovo;E8E0B7=Lenovo
0025 90=Supermicro;003048=Supermicro;0CC47A=Supermicro;3CECEF=Supermicro
7CC255=Supermicro;AC1F6B=Supermicro
001132=Synology;0011 32=Synology;90 09D0=Synology;001B21=Intel;001E67=Intel
0021 6A=Intel;0024D7=Intel;3417EB=Intel;44850 0=Intel;5CE0C5=Intel;7C7A91=Intel
94659C=Intel;A0A8CD=Intel;B4B686=Intel;E4B318=Intel;F8 6363=Intel
000E8F=QNAP;00089B=QNAP;24 5EBE=QNAP;245EBE=QNAP
0008 9F=Western Digital;00 90A9=Western Digital;0014EE=Western Digital
0011 32=Seagate;0010 5A=3Com;00 04 76=3Com
001C0E=Fortinet;000 94D=Fortinet;0809 8F=Fortinet;70 4CA5=Fortinet;90 6CAC=Fortinet
E8 1CBA=Fortinet;08 5B0E=Fortinet
0012 1E=Juniper;0019 E2=Juniper;2C21 72=Juniper;3C61 04=Juniper;54E0 32=Juniper
78 19F7=Juniper;84B5 9C=Juniper;F4 CC55=Juniper
0024 6C=Aruba;186472=Aruba;20 4C03=Aruba;24 DEC6=Aruba;6C F37F=Aruba;94B4 0F=Aruba
9C1C12=Aruba;D8C7C8=Aruba;F0 5C19=Aruba
001349=Zyxel;002 4EB=Zyxel;4C 9EFF=Zyxel;5C F4AB=Zyxel;B0 B2DC=Zyxel;EC A1D0=Zyxel
5844 98=Sony;0013A9=Sony;0019C5=Sony;30F9ED=Sony;78 843C=Sony;AC9B0A=Sony
FC0FE6=Sony;0021 9E=Sony
001E8C=Nintendo;0009BF=Nintendo;0017AB=Nintendo;0019 1D=Nintendo;18 2A7B=Nintendo
34AF2C=Nintendo;40D28A=Nintendo;58BDA3=Nintendo;7CBB8A=Nintendo;98B6E9=Nintendo
002401=Samsung;0012FB=Samsung;0016 32=Samsung;0021 19=Samsung;0023 39=Samsung
08 08C2=Samsung;10 3047=Samsung;20 1339=Samsung;2C 4401=Samsung;34 23BA=Samsung
382DD1=Samsung;50 CCF8=Samsung;5C 0A5B=Samsung;78 1FDB=Samsung;8C 71F8=Samsung
A0 0BBA=Samsung;C8 19F7=Samsung;E8 508B=Samsung;F0 5A09=Samsung;FC C734=Samsung
0025 E5=Xiaomi;3C BD3E=Xiaomi;50 8F4C=Xiaomi;64 09 80=Xiaomi;64B473=Xiaomi
78 11DC=Xiaomi;8C BEBE=Xiaomi;9C 99A0=Xiaomi;A0 86C6=Xiaomi;F0 B429=Xiaomi
001C62=LG;0019 A1=LG;0021 FB=LG;10 683F=LG;3CBDD8=LG;5C AF06=LG;6C DD BC=LG
88 3612=LG;A8 16B2=LG;C4 366C=LG
28 6ED4=Hikvision;44 19B6=Hikvision;4C BD8F=Hikvision;54 C4 15=Hikvision
BC AD28=Hikvision;C0 56E3=Hikvision
3C EF8C=Dahua;4C 11BF=Dahua;90 02A9=Dahua;E0 50 8B=Dahua;38 AF29=Dahua
00 40 8C=Axis;AC CC8E=Axis;B8 A44F=Axis;E8 27 25=Axis
'@

$script:OuiMap = @{}
function Initialize-OuiMap {
    $script:OuiMap = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in ($script:OuiRaw -split "`n")) {
        foreach ($item in ($line.Trim() -split ';')) {
            if ([string]::IsNullOrWhiteSpace($item)) { continue }
            $p = $item.Split('=')
            if ($p.Count -ne 2) { continue }
            $k = ($p[0] -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
            if (($k.Length -eq 6 -or $k.Length -eq 7 -or $k.Length -eq 9) -and -not $script:OuiMap.ContainsKey($k)) {
                $script:OuiMap[$k] = $p[1].Trim()
            }
        }
    }
    if (Test-Path $script:OuiFile) {
        try {
            foreach ($l in [IO.File]::ReadLines($script:OuiFile)) {
                if ($l -match '^\s*"?(MA-[LMS])"?\s*,\s*"?([0-9A-Fa-f]{6,9})"?\s*,\s*"?([^",]+)') {
                    $script:OuiMap[$Matches[2].ToUpperInvariant()] = ($Matches[3].Trim() -replace '\s*(,?\s*(Inc|Ltd|LLC|GmbH|Co|Corp|Corporation|Limited|S\.?A\.?|B\.?V\.?|Technologies|Technology|Electronics)\.?)+$', '')
                    continue
                }
                if ($l -match '^\s*([0-9A-Fa-f]{6,9})[\s\t;,=|-]+(.+)$') {
                    $script:OuiMap[$Matches[1].ToUpperInvariant()] = $Matches[2].Trim()
                }
            }
        } catch {}
    }
}

function Update-OuiDatabase {
    $urls = @(
        'https://standards-oui.ieee.org/oui/oui.csv',
        'https://standards-oui.ieee.org/oui28/mam.csv',
        'https://standards-oui.ieee.org/oui36/oui36.csv'
    )
    $lines = New-Object System.Collections.Generic.List[string]
    $ok = 0
    foreach ($u in $urls) {
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]'Tls12'
            $wc = New-Object System.Net.WebClient
            $wc.Headers.Add('User-Agent', 'DuckNote')
            $txt = $wc.DownloadString($u)
            foreach ($l in ($txt -split "`r?`n")) { [void]$lines.Add($l) }
            $ok++
        } catch {}
    }
    if ($ok -eq 0) { return -1 }
    try {
        [IO.File]::WriteAllLines($script:OuiFile, $lines, [Text.UTF8Encoding]::new($false))
        Initialize-OuiMap
        return $script:OuiMap.Count
    } catch { return -1 }
}
Initialize-OuiMap

$script:RowFields = [ordered]@{
    IP            = 'string'; SortKey       = 'long'  ; Status      = 'string'
    StatusRank    = 'int'   ; Hostname      = 'string'; NetBiosName = 'string'
    Workgroup     = 'string'; Mac           = 'string'; Vendor      = 'string'
    RttMs         = 'string'; RttAvg        = 'double'; Loss        = 'string'
    Ttl           = 'string'; OsGuess       = 'string'; DeviceType  = 'string'
    OpenPorts     = 'string'; PortCount     = 'int'   ; Services    = 'string'
    HttpTitle     = 'string'; HttpServer    = 'string'; TlsSubject  = 'string'
    TlsIssuer     = 'string'; TlsExpiry     = 'string'; SshBanner   = 'string'
    FtpBanner     = 'string'; SmtpBanner    = 'string'; RdpInfo     = 'string'
    SnmpName      = 'string'; SnmpDescr     = 'string'; SnmpLocation= 'string'
    SnmpContact   = 'string'; SnmpUptime    = 'string'; MdnsName    = 'string'
    UpnpDevice    = 'string'; UpnpServer    = 'string'; Shares      = 'string'
    LoggedUser    = 'string'; WmiOs         = 'string'; WmiModel    = 'string'
    WmiSerial     = 'string'; WmiUptime     = 'string'; WmiCpu      = 'string'
    WmiRam        = 'string'; WmiDisks      = 'string'; Domain      = 'string'
    Comment       = 'string'; LastSeen      = 'string'; ScanMs      = 'int'
    Notes         = 'string'; DotColor      = 'string'
    NoteKey       = 'string'
}

$script:SideFields = [ordered]@{
    Key    = 'string'; Title = 'string'; Subtitle = 'string'
    SubVis = 'string'; Dot   = 'string'; IP       = 'string'
    Para   = 'object'
}

function New-ModelSource {
    param([string]$Name, $Fields, [string]$ToString)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("public class $Name : INotifyPropertyChanged {")
    [void]$sb.AppendLine('  public event PropertyChangedEventHandler PropertyChanged;')
    [void]$sb.AppendLine('  void N(string p){ var h=PropertyChanged; if(h!=null) h(this,new PropertyChangedEventArgs(p)); }')
    foreach ($f in $Fields.Keys) {
        $t = $Fields[$f]
        [void]$sb.AppendLine("  private $t _$f; public $t $f { get { return _$f; } set { if(Equals(_$f,value)) return; _$f = value; N(`"$f`"); } }")
    }
    [void]$sb.AppendLine("  public override string ToString(){ return $ToString; }")
    [void]$sb.AppendLine('}')
    return $sb.ToString()
}

if (-not ('DuckNote.ScanRow' -as [type])) {
    Add-Type -Language CSharp -TypeDefinition (
        'using System;using System.ComponentModel;namespace DuckNote {' +
        (New-ModelSource 'ScanRow'  $script:RowFields  'IP') +
        (New-ModelSource 'SideItem' $script:SideFields 'Title') +
        '}')
}

$script:DuckPngBase64 = @'
iVBORw0KGgoAAAANSUhEUgAAAgAAAAIACAYAAAD0eNT6AAAWfmNhQlgAABZ+anVtYgAAAB5qdW1kYzJwYQARABCAAACqADibcQNjMnBhAAAAFlhqdW1i
AAAAR2p1bWRjMm1hABEAEIAAAKoAOJtxA3VybjpjMnBhOjlkZTE0M2ZiLWQxNTctNGNhMy05YzgxLWQ4NTEyNDJkYWM0OAAAAAOTanVtYgAAAClqdW1k
YzJhcwARABCAAACqADibcQNjMnBhLmFzc2VydGlvbnMAAAAAuGp1bWIAAABEanVtZGNib3IAEQAQgAAAqgA4m3ETYzJwYS5pbmdyZWRpZW50LnYzAAAA
ABhjMnNosNWQGORZCXuFpp73DgyfPwAAAGxjYm9yo2lkYzpmb3JtYXRpaW1hZ2UvcG5namluc3RhbmNlSUR4LHhtcDppaWQ6NWI3Y2I1OGUtZjM4Yi00
MzFlLWJiYmItMGYyMGZjMDU0MTkxbHJlbGF0aW9uc2hpcGhwYXJlbnRPZgAAAeJqdW1iAAAAQWp1bWRjYm9yABEAEIAAAKoAOJtxE2MycGEuYWN0aW9u
cy52MgAAAAAYYzJzaOtFoKBoRqR3ClAPRBK1MS0AAAGZY2JvcqJnYWN0aW9uc4KiZmFjdGlvbmtjMnBhLm9wZW5lZGpwYXJhbWV0ZXJzoWtpbmdyZWRp
ZW50c4GiY3VybHgtc2VsZiNqdW1iZj1jMnBhLmFzc2VydGlvbnMvYzJwYS5pbmdyZWRpZW50LnYzZGhhc2hYIEtNC8RqXm9HLzuVUUEFq3Q+c/jna1Wx
nGYAYiusM3nRpGZhY3Rpb254HWNvbS5hbnRocm9waWMuY2xhdWRlLnByb3ZpZGVkanBhcmFtZXRlcnOheB9jb20uYW50aHJvcGljLm9yaWdpbi1jb25m
aWRlbmNlZ3Vua25vd25rZGVzY3JpcHRpb254ZkNsYXVkZSBwcm92aWRlZCB0aGlzIGZpbGUgYXQgdGhlIHJlcXVlc3Qgb2YgYSB1c2VyIGFuZCBtYXkg
aGF2ZSBjcmVhdGVkIG9yIG1vZGlmaWVkIHRoZSBmaWxlIGNvbnRlbnRzLm1zb2Z0d2FyZUFnZW50oWRuYW1lZkNsYXVkZXJhbGxBY3Rpb25zSW5jbHVk
ZWT1AAAAyGp1bWIAAABAanVtZGNib3IAEQAQgAAAqgA4m3ETYzJwYS5oYXNoLmRhdGEAAAAAGGMyc2jw63aFhr43RLLhotmsAVBTAAAAgGNib3KlY2Fs
Z2ZzaGEyNTZjcGFkTQAAAAAAAAAAAAAAAABkaGFzaFggkolARzeo5DcQ+uvhF9z5lmMCgEQXfSjXQ0HkEmsg+s9kbmFtZW5qdW1iZiBtYW5pZmVzdGpl
eGNsdXNpb25zgaJlc3RhcnQYIWZsZW5ndGgZFooAAAI+anVtYgAAACdqdW1kYzJjbAARABCAAACqADibcQNjMnBhLmNsYWltLnYyAAAAAg9jYm9ypWNh
bGdmc2hhMjU2aXNpZ25hdHVyZXhNc2VsZiNqdW1iZj0vYzJwYS91cm46YzJwYTo5ZGUxNDNmYi1kMTU3LTRjYTMtOWM4MS1kODUxMjQyZGFjNDgvYzJw
YS5zaWduYXR1cmVqaW5zdGFuY2VJRHgseG1wOmlpZDpiY2UwY2RjYS01NDhiLTQzZDMtOGEwZS04MTY3NTlhNWI2ZTFyY3JlYXRlZF9hc3NlcnRpb25z
g6JjdXJseC1zZWxmI2p1bWJmPWMycGEuYXNzZXJ0aW9ucy9jMnBhLmluZ3JlZGllbnQudjNkaGFzaFggS00LxGpeb0cvO5VRQQWrdD5z+OdrVbGcZgBi
K6wzedGiY3VybHgqc2VsZiNqdW1iZj1jMnBhLmFzc2VydGlvbnMvYzJwYS5hY3Rpb25zLnYyZGhhc2hYIL+rQrcaqG7ltQ0jroEp9gSWtlqn/Zw1Es/5
IMmtuX9comN1cmx4KXNlbGYjanVtYmY9YzJwYS5hc3NlcnRpb25zL2MycGEuaGFzaC5kYXRhZGhhc2hYICJb79zXdqO7kcIk4CUM4+WQTKJ8i3j6ubPr
XP1KRNlWdGNsYWltX2dlbmVyYXRvcl9pbmZvo2RuYW1lb0FudGhyb3BpYyBGaWxlc2d2ZXJzaW9uZTEuMC4wa3NwZWNWZXJzaW9uZTIuNC4wAAAQOGp1
bWIAAAAoanVtZGMyY3MAEQAQgAAAqgA4m3EDYzJwYS5zaWduYXR1cmUAAAAQCGNib3LShFkCEqIBJhghWQIKMIICBjCCAY2gAwIBAgIUQOWgCu7COdC+
uIP6BkIFPWdVEwAwCgYIKoZIzj0EAwMwSTEXMBUGA1UEChMOQW50aHJvcGljLCBQQkMxLjAsBgNVBAMTJUFudGhyb3BpYyBDb250ZW50IENyZWRlbnRp
YWxzIFJvb3QgQ0EwHhcNMjYwODA3MTg0MzU2WhcNMjgwODA2MTk0MzU2WjBEMRcwFQYDVQQKEw5BbnRocm9waWMsIFBCQzEpMCcGA1UEAxMgQW50aHJv
cGljIENsYXVkZSBDb250ZW50IFNpZ25pbmcwWTATBgcqhkjOPQIBBggqhkjOPQMBBwNCAASYegpry1AYBRTVNL1CpTlbROnY3dey+UrsF9C3phYrATN3
ZHf93Mo8RQN0KOUuOn19P4oWNFWe5n2/She9N7eTo1gwVjAOBgNVHQ8BAf8EBAMCB4AwFQYDVR0lBA4wDAYKKwYBBAGD6F4CATAMBgNVHRMBAf8EAjAA
MB8GA1UdIwQYMBaAFM5R4gSBTmRbI/jjxM+aPpzB11zCMAoGCCqGSM49BAMDA2cAMGQCMDFzHRSeAXrSy1WOzkbhPZ6Km2wGTmZ/2gK18k8BQGXyqz88
Rdrz6CTX9flAnYNVxgIwcF9c3fVhqmJKpi+UhasNUMko69cyX6STPfta3Q8EjyzDjzoyrol46FP6VFHhvUcJoWNwYWRZDZ4AAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD2WEB5999aRwcF
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
'@

$script:ScanLib = @'
function DN-IpToUInt {
    param([string]$Ip)
    try { return [BitConverter]::ToUInt32(([Net.IPAddress]::Parse($Ip)).GetAddressBytes(), 0) }
    catch { return [uint32]0 }
}
function DN-IpSortKey {
    param([string]$Ip)
    try {
        $b = ([Net.IPAddress]::Parse($Ip)).GetAddressBytes()
        if ($b.Length -ne 4) { return [long]0 }
        return ([long]$b[0] -shl 24) -bor ([long]$b[1] -shl 16) -bor ([long]$b[2] -shl 8) -bor [long]$b[3]
    } catch { return [long]0 }
}
function DN-FormatMac {
    param([byte[]]$Bytes, [int]$Len = 6)
    if ($null -eq $Bytes -or $Len -lt 6) { return '' }
    $p = @(); for ($i = 0; $i -lt 6; $i++) { $p += ('{0:X2}' -f $Bytes[$i]) }
    return ($p -join ':')
}
function DN-Clean {
    param([string]$S, [int]$Max = 220)
    if ([string]::IsNullOrEmpty($S)) { return '' }
    $S = $S -replace '[\x00-\x08\x0B\x0C\x0E-\x1F]', ''
    $S = ($S -replace '\s+', ' ').Trim()
    if ($S.Length -gt $Max) { $S = $S.Substring(0, $Max) + '...' }
    return $S
}

function DN-VendorFromMac {
    param([string]$Mac, $Oui)
    if (-not $Mac -or $null -eq $Oui) { return '' }
    $h = ($Mac -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
    if ($h.Length -lt 6) { return '' }
    foreach ($len in 9, 7, 6) {
        if ($h.Length -ge $len) {
            $k = $h.Substring(0, $len)
            if ($Oui.ContainsKey($k)) { return $Oui[$k] }
        }
    }
    try {
        $b0 = [Convert]::ToInt32($h.Substring(0, 2), 16)
        if (($b0 -band 0x01) -ne 0) { return 'Indirizzo multicast' }
        if (($b0 -band 0x02) -ne 0) { return 'MAC locale / randomizzato' }
    } catch {}
    return ''
}

function DN-VendorFromText {
    param([string]$Text)
    if (-not $Text) { return '' }
    $map = @(
        @{ R = 'mikrotik|routeros';                V = 'MikroTik' }
        @{ R = 'ubiquiti|unifi|edgeos|airos';      V = 'Ubiquiti' }
        @{ R = 'fritz!?box|avm';                   V = 'AVM' }
        @{ R = 'synology|diskstation';             V = 'Synology' }
        @{ R = 'qnap|turbonas';                    V = 'QNAP' }
        @{ R = 'openwrt|lede';                     V = 'OpenWrt' }
        @{ R = 'pfsense|opnsense|netgate';         V = 'Netgate' }
        @{ R = 'cisco|ios-xe|nx-os';               V = 'Cisco' }
        @{ R = 'juniper|junos';                    V = 'Juniper' }
        @{ R = 'fortigate|fortios|fortinet';       V = 'Fortinet' }
        @{ R = 'aruba|arubaos';                    V = 'Aruba' }
        @{ R = 'zyxel';                            V = 'Zyxel' }
        @{ R = 'tp-link|tplink|archer';            V = 'TP-Link' }
        @{ R = 'netgear|readynas';                 V = 'Netgear' }
        @{ R = 'd-link|dlink';                     V = 'D-Link' }
        @{ R = 'asuswrt|asus';                     V = 'ASUS' }
        @{ R = 'hikvision|hikconnect';             V = 'Hikvision' }
        @{ R = 'dahua';                            V = 'Dahua' }
        @{ R = 'axis ?communication|axis camera';  V = 'Axis' }
        @{ R = 'hp ?(laserjet|officejet|ethernet)|hewlett'; V = 'HP' }
        @{ R = 'brother';                          V = 'Brother' }
        @{ R = 'epson';                            V = 'Epson' }
        @{ R = 'canon';                            V = 'Canon' }
        @{ R = 'kyocera';                          V = 'Kyocera' }
        @{ R = 'lexmark';                          V = 'Lexmark' }
        @{ R = 'ricoh|aficio';                     V = 'Ricoh' }
        @{ R = 'xerox|phaser';                     V = 'Xerox' }
        @{ R = 'idrac|poweredge|dell';             V = 'Dell' }
        @{ R = 'ilo |integrated lights-out';       V = 'HPE' }
        @{ R = 'supermicro|megarac';               V = 'Supermicro' }
        @{ R = 'vmware|esxi';                      V = 'VMware' }
        @{ R = 'proxmox';                          V = 'Proxmox' }
        @{ R = 'synapse|sonos';                    V = 'Sonos' }
        @{ R = 'roku';                             V = 'Roku' }
        @{ R = 'philips ?hue|hue bridge';          V = 'Philips' }
        @{ R = 'shelly';                           V = 'Shelly' }
        @{ R = 'tasmota|espressif|esp8266|esp32';  V = 'Espressif' }
        @{ R = 'raspbian|raspberry';               V = 'Raspberry Pi' }
        @{ R = 'apple|airport|airplay';            V = 'Apple' }
        @{ R = 'samsung';                          V = 'Samsung' }
        @{ R = 'lg electronics|webos';             V = 'LG' }
        @{ R = 'sony|bravia';                      V = 'Sony' }
        @{ R = 'technicolor';                      V = 'Technicolor' }
        @{ R = 'sagemcom';                         V = 'Sagemcom' }
        @{ R = 'huawei|hicloud';                   V = 'Huawei' }
        @{ R = 'zte ';                             V = 'ZTE' }
        @{ R = 'grandstream';                      V = 'Grandstream' }
        @{ R = 'yealink';                          V = 'Yealink' }
        @{ R = 'polycom|poly ';                    V = 'Polycom' }
    )
    foreach ($m in $map) { if ($Text -match ('(?i)' + $m.R)) { return $m.V } }
    return ''
}

function DN-GetMac {
    param([string]$Ip)
    try {
        $dst = DN-IpToUInt $Ip
        if ($dst -eq 0) { return '' }
        $buf = New-Object byte[] 6
        $len = [uint32]6
        $rc  = [DuckNative.Win32]::SendARP($dst, 0, $buf, [ref]$len)
        if ($rc -eq 0 -and $len -ge 6) { return (DN-FormatMac $buf $len) }
    } catch {}
    try {
        $out = & arp.exe -a $Ip 2>$null
        foreach ($l in $out) {
            if ($l -match '(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9A-Fa-f]{2}(?:[-:][0-9A-Fa-f]{2}){5})') {
                if ($Matches[1] -eq $Ip) { return ($Matches[2].ToUpperInvariant() -replace '-', ':') }
            }
        }
    } catch {}
    return ''
}

function DN-Ping {
    param([string]$Target, [int]$Count = 2, [int]$TimeoutMs = 800)
    $res = @{ Online = $false; Rtts = @(); Ttl = 0; Sent = $Count; Recv = 0; Address = '' }
    $p = New-Object System.Net.NetworkInformation.Ping
    $opt = New-Object System.Net.NetworkInformation.PingOptions
    $opt.DontFragment = $true
    $payload = New-Object byte[] 32
    for ($i = 0; $i -lt $Count; $i++) {
        try {
            $r = $p.Send($Target, $TimeoutMs, $payload, $opt)
            if ($r.Status -eq 'Success') {
                $res.Online = $true
                $res.Recv++
                $res.Rtts += [int]$r.RoundtripTime
                if ($r.Address) { $res.Address = $r.Address.ToString() }
                try { if ($r.Options -and $r.Options.Ttl -gt 0) { $res.Ttl = [int]$r.Options.Ttl } } catch {}
            }
        } catch { break }
    }
    try { $p.Dispose() } catch {}
    return $res
}

function DN-OsFromTtl {
    param([int]$Ttl)
    if ($Ttl -le 0) { return '' }
    if ($Ttl -le 64  -and $Ttl -gt 32)  { return 'Linux / Unix / macOS' }
    if ($Ttl -le 128 -and $Ttl -gt 64)  { return 'Windows' }
    if ($Ttl -le 255 -and $Ttl -gt 128) { return 'Apparato di rete (Cisco/Solaris)' }
    if ($Ttl -le 32) { return 'Embedded / legacy' }
    return ''
}
function DN-HopsFromTtl {
    param([int]$Ttl)
    if ($Ttl -le 0) { return 0 }
    foreach ($init in @(64, 128, 255, 32)) { if ($Ttl -le $init) { return ($init - $Ttl) } }
    return 0
}

function DN-ScanPorts {
    param([string]$Ip, [int[]]$Ports, [int]$TimeoutMs = 500)
    $open = New-Object System.Collections.Generic.List[int]
    if ($null -eq $Ports -or $Ports.Count -eq 0) { return $open }
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($port in $Ports) {
        try {
            $c = New-Object System.Net.Sockets.TcpClient
            $c.SendTimeout = $TimeoutMs; $c.ReceiveTimeout = $TimeoutMs
            $h = $c.BeginConnect($Ip, $port, $null, $null)
            $items.Add([pscustomobject]@{ Port = $port; Client = $c; Handle = $h })
        } catch {}
    }
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    foreach ($it in $items) {
        $left = [int]([Math]::Max(0, ($deadline - [DateTime]::UtcNow).TotalMilliseconds))
        try {
            if ($it.Handle.AsyncWaitHandle.WaitOne($left, $false)) {
                try { $it.Client.EndConnect($it.Handle); if ($it.Client.Connected) { [void]$open.Add($it.Port) } } catch {}
            }
        } catch {}
        try { $it.Client.Close() } catch {}
    }
    return ($open | Sort-Object)
}

function DN-Banner {
    param([string]$Ip, [int]$Port, [int]$TimeoutMs = 900, [string]$Send = '', [int]$MaxBytes = 2048)
    $out = ''
    try {
        $c = New-Object System.Net.Sockets.TcpClient
        $h = $c.BeginConnect($Ip, $Port, $null, $null)
        if (-not $h.AsyncWaitHandle.WaitOne($TimeoutMs, $false)) { $c.Close(); return '' }
        $c.EndConnect($h)
        $s = $c.GetStream()
        $s.ReadTimeout = $TimeoutMs; $s.WriteTimeout = $TimeoutMs
        if ($Send) {
            $b = [Text.Encoding]::ASCII.GetBytes($Send)
            $s.Write($b, 0, $b.Length); $s.Flush()
        }
        $buf = New-Object byte[] $MaxBytes
        $tot = 0
        $end = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
        while ($tot -lt $MaxBytes -and [DateTime]::UtcNow -lt $end) {
            if (-not $s.DataAvailable -and $tot -gt 0) { break }
            $n = 0
            try { $n = $s.Read($buf, $tot, $MaxBytes - $tot) } catch { break }
            if ($n -le 0) { break }
            $tot += $n
            if (-not $s.DataAvailable) { break }
        }
        if ($tot -gt 0) { $out = [Text.Encoding]::UTF8.GetString($buf, 0, $tot) }
        $s.Close(); $c.Close()
    } catch {}
    return $out
}

function DN-HttpProbe {
    param([string]$Ip, [int]$Port, [bool]$UseTls = $false, [int]$TimeoutMs = 1500)
    $r = @{ Title = ''; Server = ''; Code = ''; Powered = ''; Redirect = ''
            TlsSubject = ''; TlsIssuer = ''; TlsExpiry = ''; TlsProto = ''; TlsSan = '' }
    $c = $null; $stream = $null
    try {
        $c = New-Object System.Net.Sockets.TcpClient
        $h = $c.BeginConnect($Ip, $Port, $null, $null)
        if (-not $h.AsyncWaitHandle.WaitOne($TimeoutMs, $false)) { $c.Close(); return $r }
        $c.EndConnect($h)
        $net = $c.GetStream()
        $net.ReadTimeout = $TimeoutMs; $net.WriteTimeout = $TimeoutMs
        if ($UseTls) {
            $cb  = [System.Net.Security.RemoteCertificateValidationCallback] { param($a, $b, $cc, $d) $true }
            $ssl = New-Object System.Net.Security.SslStream($net, $false, $cb)
            $protos = [System.Security.Authentication.SslProtocols]::Tls12
            try { $protos = $protos -bor [System.Security.Authentication.SslProtocols]::Tls11 -bor [System.Security.Authentication.SslProtocols]::Tls } catch {}
            try   { $ssl.AuthenticateAsClient($Ip, $null, $protos, $false) }
            catch { $ssl.AuthenticateAsClient($Ip) }
            try {
                $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)
                $r.TlsSubject = DN-Clean $cert.Subject 160
                $r.TlsIssuer  = DN-Clean $cert.Issuer 160
                $r.TlsExpiry  = $cert.NotAfter.ToString('yyyy-MM-dd')
                foreach ($ext in $cert.Extensions) {
                    if ($ext.Oid.Value -eq '2.5.29.17') { $r.TlsSan = DN-Clean ($ext.Format($false)) 200 }
                }
            } catch {}
            try { $r.TlsProto = $ssl.SslProtocol.ToString() } catch {}
            $stream = $ssl
        } else {
            $stream = $net
        }
        $req = "GET / HTTP/1.1`r`nHost: $Ip`r`nUser-Agent: DuckNote/2.0`r`nAccept: */*`r`nConnection: close`r`n`r`n"
        $rb  = [Text.Encoding]::ASCII.GetBytes($req)
        $stream.Write($rb, 0, $rb.Length); $stream.Flush()
        $ms  = New-Object System.IO.MemoryStream
        $buf = New-Object byte[] 4096
        $end = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs * 2)
        while ($ms.Length -lt 65536 -and [DateTime]::UtcNow -lt $end) {
            $n = 0
            try { $n = $stream.Read($buf, 0, $buf.Length) } catch { break }
            if ($n -le 0) { break }
            $ms.Write($buf, 0, $n)
        }
        $txt = [Text.Encoding]::UTF8.GetString($ms.ToArray())
        if ($txt -match '^HTTP/[\d\.]+\s+(\d{3})')            { $r.Code     = $Matches[1] }
        if ($txt -match '(?im)^Server:\s*(.+?)\s*$')           { $r.Server   = DN-Clean $Matches[1] 120 }
        if ($txt -match '(?im)^X-Powered-By:\s*(.+?)\s*$')     { $r.Powered  = DN-Clean $Matches[1] 120 }
        if ($txt -match '(?im)^Location:\s*(.+?)\s*$')         { $r.Redirect = DN-Clean $Matches[1] 160 }
        if ($txt -match '(?is)<title[^>]*>(.*?)</title>')      { $r.Title    = DN-Clean ($Matches[1] -replace '<[^>]+>', '') 160 }
        if (-not $r.Title -and $txt -match '(?im)^WWW-Authenticate:\s*.*realm="([^"]+)"') { $r.Title = 'realm: ' + (DN-Clean $Matches[1] 80) }
    } catch {}
    finally {
        try { if ($stream) { $stream.Dispose() } } catch {}
        try { if ($c) { $c.Close() } } catch {}
    }
    return $r
}

function DN-Ptr {
    param([string]$Ip, [int]$TimeoutMs = 900, [string]$Server = '')
    if ($Server) { return (DN-DnsQuery -Server $Server -Name (DN-ArpaName $Ip) -Type 12 -TimeoutMs $TimeoutMs) }
    try {
        $ar = [Net.Dns]::BeginGetHostEntry($Ip, $null, $null)
        if ($ar.AsyncWaitHandle.WaitOne($TimeoutMs, $false)) {
            $he = [Net.Dns]::EndGetHostEntry($ar)
            if ($he -and $he.HostName -and $he.HostName -ne $Ip) { return $he.HostName }
        }
    } catch {}
    return ''
}

function DN-Resolve4 {
    param([string]$Name, [int]$TimeoutMs = 1500, [string]$Server = '')
    if ($Server) { return (DN-DnsQuery -Server $Server -Name $Name -Type 1 -TimeoutMs $TimeoutMs) }
    try {
        $ar = [Net.Dns]::BeginGetHostAddresses($Name, $null, $null)
        if ($ar.AsyncWaitHandle.WaitOne($TimeoutMs, $false)) {
            $v4 = [Net.Dns]::EndGetHostAddresses($ar) |
                  Where-Object { $_.AddressFamily -eq 'InterNetwork' } | Select-Object -First 1
            if ($v4) { return $v4.ToString() }
        }
    } catch {}
    return ''
}

function DN-NetBiosEncode {
    param([string]$Name)
    $n = $Name.ToUpperInvariant().PadRight(15).Substring(0, 15)
    $bytes = [Text.Encoding]::ASCII.GetBytes($n)
    $out = New-Object System.Collections.Generic.List[byte]
    foreach ($b in $bytes) {
        [void]$out.Add([byte](0x41 + (($b -shr 4) -band 0x0F)))
        [void]$out.Add([byte](0x41 + ($b -band 0x0F)))
    }
    [void]$out.Add([byte]0x41); [void]$out.Add([byte]0x41)
    return ,([byte[]]$out.ToArray())
}

function DN-NetBios {
    param([string]$Ip, [int]$TimeoutMs = 700)
    $res = @{ Name = ''; Workgroup = ''; Mac = ''; Users = @(); Services = @() }
    $udp = $null
    try {
        $enc = DN-NetBiosEncode '*'
        $pkt = New-Object System.Collections.Generic.List[byte]
        $rnd = New-Object Random
        [void]$pkt.Add([byte]$rnd.Next(0, 255)); [void]$pkt.Add([byte]$rnd.Next(0, 255))
        $pkt.AddRange([byte[]](0x00,0x00, 0x00,0x01, 0x00,0x00, 0x00,0x00, 0x00,0x00))
        [void]$pkt.Add([byte]0x20)
        $pkt.AddRange([byte[]]$enc)
        [void]$pkt.Add([byte]0x00)
        $pkt.AddRange([byte[]](0x00,0x21, 0x00,0x01))

        $udp = New-Object System.Net.Sockets.UdpClient
        $udp.Client.ReceiveTimeout = $TimeoutMs
        $udp.Client.SendTimeout    = $TimeoutMs
        $ep = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Parse($Ip)), 137
        $b  = $pkt.ToArray()
        [void]$udp.Send($b, $b.Length, $ep)
        $any = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Any), 0
        $data = $udp.Receive([ref]$any)
        if ($data.Length -lt 57) { return $res }

        $i = 12
        while ($i -lt $data.Length -and $data[$i] -ne 0) { $i += ($data[$i] + 1) }
        $i += 1 + 2 + 2 + 4 + 2
        if ($i -ge $data.Length) { return $res }
        $count = $data[$i]; $i++
        for ($k = 0; $k -lt $count -and ($i + 17) -le $data.Length; $k++) {
            $nm    = ([Text.Encoding]::ASCII.GetString($data, $i, 15)).Trim()
            $suf   = $data[$i + 15]
            $flags = ([int]$data[$i + 16] -shl 8) -bor [int]$data[$i + 17]
            $group = (($flags -band 0x8000) -ne 0)
            if ($group) {
                if (-not $res.Workgroup -and $suf -in @(0x00, 0x1E)) { $res.Workgroup = $nm }
            } else {
                if     ($suf -eq 0x00 -and -not $res.Name) { $res.Name = $nm }
                elseif ($suf -eq 0x20) { $res.Services += 'File server'; if (-not $res.Name) { $res.Name = $nm } }
                elseif ($suf -eq 0x03) { if ($nm -ne $res.Name) { $res.Users += $nm } }
                elseif ($suf -eq 0x1B) { $res.Services += 'Domain master browser' }
                elseif ($suf -eq 0x1D) { $res.Services += 'Master browser' }
            }
            $i += 18
        }
        if (($i + 6) -le $data.Length) {
            $mb = New-Object byte[] 6
            [Array]::Copy($data, $i, $mb, 0, 6)
            $m = DN-FormatMac $mb 6
            if ($m -ne '00:00:00:00:00:00') { $res.Mac = $m }
        }
    } catch {}
    finally { try { if ($udp) { $udp.Close() } } catch {} }
    return $res
}

function DN-DnsDecodeName {
    param([byte[]]$Data, [int]$Offset)
    $labels = @(); $i = $Offset; $jumped = $false; $guard = 0
    while ($i -lt $Data.Length -and $guard -lt 64) {
        $guard++
        $len = $Data[$i]
        if ($len -eq 0) { $i++; break }
        if (($len -band 0xC0) -eq 0xC0) {
            if (($i + 1) -ge $Data.Length) { break }
            $ptr = ((([int]$len -band 0x3F) -shl 8) -bor [int]$Data[$i + 1])
            if (-not $jumped) { $i += 2 }
            $jumped = $true
            $i2 = $ptr
            if ($i2 -ge $Data.Length -or $i2 -eq $Offset) { break }
            $sub = DN-DnsDecodeName $Data $i2
            if ($sub.Name) { $labels += $sub.Name }
            break
        }
        if (($i + 1 + $len) -gt $Data.Length) { break }
        $labels += [Text.Encoding]::UTF8.GetString($Data, $i + 1, $len)
        $i += 1 + $len
    }
    return @{ Name = ($labels -join '.'); Next = $i }
}

function DN-ArpaName {
    param([string]$Ip)
    $o = $Ip.Split('.')
    if ($o.Count -ne 4) { return '' }
    return "$($o[3]).$($o[2]).$($o[1]).$($o[0]).in-addr.arpa"
}

function DN-DnsLabels {
    param([string]$Name)
    $out = New-Object System.Collections.Generic.List[byte]
    foreach ($lab in $Name.Split('.')) {
        if (-not $lab) { continue }
        $lb = [Text.Encoding]::ASCII.GetBytes($lab)
        [void]$out.Add([byte]$lb.Length); $out.AddRange($lb)
    }
    [void]$out.Add([byte]0x00)
    return ,([byte[]]$out.ToArray())
}

function DN-DnsAnswer {
    param([byte[]]$Data, [int]$Type)
    if ($Data.Length -lt 12) { return '' }
    $anc = ([int]$Data[6] -shl 8) -bor [int]$Data[7]
    if ($anc -le 0) { return '' }
    $qd = ([int]$Data[4] -shl 8) -bor [int]$Data[5]
    $i = 12
    for ($q = 0; $q -lt $qd; $q++) {
        $n = DN-DnsDecodeName $Data $i
        $i = $n.Next + 4
    }
    for ($a = 0; $a -lt $anc -and $i -lt $Data.Length; $a++) {
        $n = DN-DnsDecodeName $Data $i
        $i = $n.Next
        if (($i + 10) -gt $Data.Length) { break }
        $type  = ([int]$Data[$i] -shl 8) -bor [int]$Data[$i + 1]
        $rdlen = ([int]$Data[$i + 8] -shl 8) -bor [int]$Data[$i + 9]
        $rdoff = $i + 10
        if ($type -eq $Type -and $Type -eq 12) {
            $pn = DN-DnsDecodeName $Data $rdoff
            if ($pn.Name) { return $pn.Name }
        }
        if ($type -eq $Type -and $Type -eq 1 -and $rdlen -eq 4 -and ($rdoff + 4) -le $Data.Length) {
            return ('{0}.{1}.{2}.{3}' -f $Data[$rdoff], $Data[$rdoff+1], $Data[$rdoff+2], $Data[$rdoff+3])
        }
        $i = $rdoff + $rdlen
    }
    return ''
}

function DN-DnsQuery {
    param([string]$Server, [string]$Name, [int]$Type = 12, [int]$TimeoutMs = 900)
    if (-not $Name) { return '' }
    $udp = $null
    try {
        $rnd = New-Object Random
        $pkt = New-Object System.Collections.Generic.List[byte]
        [void]$pkt.Add([byte]$rnd.Next(0, 255)); [void]$pkt.Add([byte]$rnd.Next(0, 255))
        $pkt.AddRange([byte[]](0x01,0x00, 0x00,0x01, 0x00,0x00, 0x00,0x00, 0x00,0x00))
        $pkt.AddRange([byte[]](DN-DnsLabels $Name))
        $pkt.AddRange([byte[]]([byte](($Type -shr 8) -band 0xFF), [byte]($Type -band 0xFF), 0x00, 0x01))

        $udp = New-Object System.Net.Sockets.UdpClient
        $udp.Client.ReceiveTimeout = $TimeoutMs
        $udp.Client.SendTimeout    = $TimeoutMs
        $ep = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Parse($Server)), 53
        $b  = $pkt.ToArray()
        [void]$udp.Send($b, $b.Length, $ep)
        $any = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Any), 0
        return (DN-DnsAnswer ($udp.Receive([ref]$any)) $Type)
    } catch { return '' }
    finally { try { if ($udp) { $udp.Close() } } catch {} }
}

function DN-Mdns {
    param([string]$Ip, [int]$TimeoutMs = 600)
    $qname = DN-ArpaName $Ip
    if (-not $qname) { return '' }
    $udp = $null
    try {
        $pkt = New-Object System.Collections.Generic.List[byte]
        $pkt.AddRange([byte[]](0x00,0x00, 0x00,0x00, 0x00,0x01, 0x00,0x00, 0x00,0x00, 0x00,0x00))
        $pkt.AddRange([byte[]](DN-DnsLabels $qname))
        $pkt.AddRange([byte[]](0x00,0x0C, 0x80,0x01))

        $udp = New-Object System.Net.Sockets.UdpClient
        $udp.Client.ReceiveTimeout = $TimeoutMs
        $ep = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Parse($Ip)), 5353
        $b  = $pkt.ToArray()
        [void]$udp.Send($b, $b.Length, $ep)
        $any = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Any), 0
        return (DN-DnsAnswer ($udp.Receive([ref]$any)) 12)
    } catch { return '' }
    finally { try { if ($udp) { $udp.Close() } } catch {} }
}

function DN-Ssdp {
    param([string]$Ip, [int]$TimeoutMs = 800)
    $r = @{ Server = ''; Location = ''; Device = ''; St = '' }
    $udp = $null
    try {
        $msg = "M-SEARCH * HTTP/1.1`r`nHOST: $($Ip):1900`r`nMAN: `"ssdp:discover`"`r`nMX: 1`r`nST: ssdp:all`r`n`r`n"
        $b   = [Text.Encoding]::ASCII.GetBytes($msg)
        $udp = New-Object System.Net.Sockets.UdpClient
        $udp.Client.ReceiveTimeout = $TimeoutMs
        $ep  = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Parse($Ip)), 1900
        [void]$udp.Send($b, $b.Length, $ep)
        $any  = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Any), 0
        $data = $udp.Receive([ref]$any)
        $txt  = [Text.Encoding]::ASCII.GetString($data)
        if ($txt -match '(?im)^SERVER:\s*(.+?)\s*$')   { $r.Server   = DN-Clean $Matches[1] 140 }
        if ($txt -match '(?im)^LOCATION:\s*(.+?)\s*$') { $r.Location = DN-Clean $Matches[1] 200 }
        if ($txt -match '(?im)^ST:\s*(.+?)\s*$')       { $r.St       = DN-Clean $Matches[1] 100 }
    } catch {}
    finally { try { if ($udp) { $udp.Close() } } catch {} }
    if ($r.Location -match '^https?://') {
        try {
            $wc = New-Object System.Net.WebClient
            $wc.Headers.Add('User-Agent', 'DuckNote/2.0')
            $t  = $null
            $task = $wc.DownloadStringTaskAsync($r.Location)
            if ($task.Wait(1500)) { $t = $task.Result }
            if ($t) {
                $fn = ''; $mf = ''; $mo = ''
                if ($t -match '(?is)<friendlyName>(.*?)</friendlyName>')   { $fn = DN-Clean $Matches[1] 80 }
                if ($t -match '(?is)<manufacturer>(.*?)</manufacturer>')   { $mf = DN-Clean $Matches[1] 60 }
                if ($t -match '(?is)<modelName>(.*?)</modelName>')         { $mo = DN-Clean $Matches[1] 60 }
                $r.Device = (@($fn, $mf, $mo) | Where-Object { $_ }) -join ' / '
            }
        } catch {}
    }
    return $r
}

function DN-BerLen {
    param([int]$N)
    if ($N -lt 128) { return ,([byte[]]@([byte]$N)) }
    $tmp = New-Object System.Collections.Generic.List[byte]
    $t = $N
    while ($t -gt 0) { $tmp.Insert(0, [byte]($t -band 0xFF)); $t = $t -shr 8 }
    $o = New-Object System.Collections.Generic.List[byte]
    [void]$o.Add([byte](0x80 -bor $tmp.Count))
    $o.AddRange($tmp)
    return ,([byte[]]$o.ToArray())
}
function DN-Ber {
    param([byte]$Tag, [byte[]]$Val)
    if ($null -eq $Val) { $Val = [byte[]]@() }
    $o = New-Object System.Collections.Generic.List[byte]
    [void]$o.Add($Tag)
    $o.AddRange([byte[]](DN-BerLen $Val.Length))
    if ($Val.Length -gt 0) { $o.AddRange($Val) }
    return ,([byte[]]$o.ToArray())
}
function DN-BerOid {
    param([int[]]$Oid)
    $o = New-Object System.Collections.Generic.List[byte]
    [void]$o.Add([byte](40 * $Oid[0] + $Oid[1]))
    for ($i = 2; $i -lt $Oid.Length; $i++) {
        $v = $Oid[$i]
        if ($v -lt 128) { [void]$o.Add([byte]$v); continue }
        $st = New-Object System.Collections.Generic.List[byte]
        [void]$st.Add([byte]($v -band 0x7F))
        $v = $v -shr 7
        while ($v -gt 0) { $st.Insert(0, [byte](($v -band 0x7F) -bor 0x80)); $v = $v -shr 7 }
        $o.AddRange($st)
    }
    return ,([byte[]]$o.ToArray())
}
function DN-BerChildren {
    param([byte[]]$Data, [int]$Start = 0, [int]$End = -1)
    if ($End -lt 0) { $End = $Data.Length }
    $list = New-Object System.Collections.Generic.List[object]
    $i = $Start
    while (($i + 2) -le $End) {
        $tag = $Data[$i]; $i++
        $len = [int]$Data[$i]; $i++
        if ($len -band 0x80) {
            $nb = $len -band 0x7F
            if ($nb -lt 1 -or $nb -gt 4 -or ($i + $nb) -gt $End) { break }
            $len = 0
            for ($k = 0; $k -lt $nb; $k++) { $len = ($len -shl 8) -bor [int]$Data[$i + $k] }
            $i += $nb
        }
        if ($len -lt 0 -or ($i + $len) -gt $End) { break }
        [void]$list.Add([pscustomobject]@{ Tag = $tag; Offset = $i; Length = $len })
        $i += $len
    }
    return $list
}
function DN-BerValue {
    param([byte[]]$Data, $Node)
    if ($null -eq $Node) { return '' }
    $t = $Node.Tag; $o = $Node.Offset; $l = $Node.Length
    switch ($t) {
        0x04 { return (DN-Clean ([Text.Encoding]::UTF8.GetString($Data, $o, $l)) 400) }
        0x02 { $v = [long]0; for ($k = 0; $k -lt $l; $k++) { $v = ($v -shl 8) -bor [long]$Data[$o + $k] }; return "$v" }
        0x43 {
            $v = [long]0; for ($k = 0; $k -lt $l; $k++) { $v = ($v -shl 8) -bor [long]$Data[$o + $k] }
            $ts = [TimeSpan]::FromSeconds([double]$v / 100.0)
            return ('{0}g {1:00}:{2:00}:{3:00}' -f $ts.Days, $ts.Hours, $ts.Minutes, $ts.Seconds)
        }
        0x06 {
            $parts = @(); $first = [int]$Data[$o]
            $parts += [int][Math]::Floor($first / 40); $parts += ($first % 40)
            $acc = 0
            for ($k = 1; $k -lt $l; $k++) {
                $b = [int]$Data[$o + $k]
                $acc = ($acc -shl 7) -bor ($b -band 0x7F)
                if (($b -band 0x80) -eq 0) { $parts += $acc; $acc = 0 }
            }
            return ($parts -join '.')
        }
        0x40 { $p = @(); for ($k = 0; $k -lt $l; $k++) { $p += [int]$Data[$o + $k] }; return ($p -join '.') }
        default { return '' }
    }
}

function DN-Snmp {
    param([string]$Ip, [string]$Community = 'public', [int]$TimeoutMs = 900)
    $r = @{ Descr = ''; ObjectId = ''; Uptime = ''; Contact = ''; Name = ''; Location = '' }
    $udp = $null
    try {
        $oids = @(
            @{ K = 'Descr';    O = @(1,3,6,1,2,1,1,1,0) },
            @{ K = 'ObjectId'; O = @(1,3,6,1,2,1,1,2,0) },
            @{ K = 'Uptime';   O = @(1,3,6,1,2,1,1,3,0) },
            @{ K = 'Contact';  O = @(1,3,6,1,2,1,1,4,0) },
            @{ K = 'Name';     O = @(1,3,6,1,2,1,1,5,0) },
            @{ K = 'Location'; O = @(1,3,6,1,2,1,1,6,0) }
        )
        $vbList = New-Object System.Collections.Generic.List[byte]
        foreach ($e in $oids) {
            $inner = New-Object System.Collections.Generic.List[byte]
            $inner.AddRange([byte[]](DN-Ber 0x06 (DN-BerOid $e.O)))
            $inner.AddRange([byte[]](DN-Ber 0x05 ([byte[]]@())))
            $vbList.AddRange([byte[]](DN-Ber 0x30 ([byte[]]$inner.ToArray())))
        }
        $varbinds = [byte[]](DN-Ber 0x30 ([byte[]]$vbList.ToArray()))
        $rid = (New-Object Random).Next(1, 2147483000)
        $ridB = New-Object System.Collections.Generic.List[byte]
        $tmp = $rid
        while ($tmp -gt 0) { $ridB.Insert(0, [byte]($tmp -band 0xFF)); $tmp = $tmp -shr 8 }
        if ($ridB.Count -eq 0) { [void]$ridB.Add([byte]0) }
        if (($ridB[0] -band 0x80) -ne 0) { $ridB.Insert(0, [byte]0) }
        $pduBody = New-Object System.Collections.Generic.List[byte]
        $pduBody.AddRange([byte[]](DN-Ber 0x02 ([byte[]]$ridB.ToArray())))
        $pduBody.AddRange([byte[]](DN-Ber 0x02 ([byte[]]@(0))))
        $pduBody.AddRange([byte[]](DN-Ber 0x02 ([byte[]]@(0))))
        $pduBody.AddRange($varbinds)
        $pdu = [byte[]](DN-Ber 0xA0 ([byte[]]$pduBody.ToArray()))
        $msgBody = New-Object System.Collections.Generic.List[byte]
        $msgBody.AddRange([byte[]](DN-Ber 0x02 ([byte[]]@(1))))
        $msgBody.AddRange([byte[]](DN-Ber 0x04 ([Text.Encoding]::ASCII.GetBytes($Community))))
        $msgBody.AddRange($pdu)
        $msg = [byte[]](DN-Ber 0x30 ([byte[]]$msgBody.ToArray()))

        $udp = New-Object System.Net.Sockets.UdpClient
        $udp.Client.ReceiveTimeout = $TimeoutMs
        $ep = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Parse($Ip)), 161
        [void]$udp.Send($msg, $msg.Length, $ep)
        $any  = New-Object System.Net.IPEndPoint ([Net.IPAddress]::Any), 0
        $data = $udp.Receive([ref]$any)
        if ($data.Length -lt 10) { return $r }

        $top = DN-BerChildren $data 0 $data.Length
        if ($top.Count -lt 1) { return $r }
        $inner1 = DN-BerChildren $data $top[0].Offset ($top[0].Offset + $top[0].Length)
        $pduNode = $inner1 | Where-Object { $_.Tag -eq 0xA2 } | Select-Object -First 1
        if (-not $pduNode) { return $r }
        $pduKids = DN-BerChildren $data $pduNode.Offset ($pduNode.Offset + $pduNode.Length)
        $vbNode  = $pduKids | Where-Object { $_.Tag -eq 0x30 } | Select-Object -First 1
        if (-not $vbNode) { return $r }
        $vbs = DN-BerChildren $data $vbNode.Offset ($vbNode.Offset + $vbNode.Length)
        $idx = 0
        foreach ($vb in $vbs) {
            $kids = DN-BerChildren $data $vb.Offset ($vb.Offset + $vb.Length)
            if ($kids.Count -ge 2 -and $idx -lt $oids.Count) {
                $val = DN-BerValue $data $kids[1]
                if ($val) { $r[$oids[$idx].K] = $val }
            }
            $idx++
        }
    } catch {}
    finally { try { if ($udp) { $udp.Close() } } catch {} }
    return $r
}

function DN-Shares {
    param([string]$Ip, [int]$TimeoutMs = 4000)
    $out = @()
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName               = "$env:SystemRoot\System32\net.exe"
        $psi.Arguments              = "view \\$Ip /all"
        $psi.UseShellExecute        = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError  = $true
        $psi.CreateNoWindow         = $true
        $p = [System.Diagnostics.Process]::Start($psi)
        $txt = ''
        if ($p.WaitForExit($TimeoutMs)) { $txt = $p.StandardOutput.ReadToEnd() }
        else { try { $p.Kill() } catch {} }
        foreach ($l in ($txt -split "`r?`n")) {
            if ($l -match '^(\S+)\s+(Disk|Disco|Print|Stampa|IPC)\b') { $out += $Matches[1] }
        }
    } catch {}
    return ($out | Select-Object -Unique)
}

function DN-Wmi {
    param([string]$Ip, $Credential = $null, [int]$TimeoutSec = 8)
    $r = @{ Os = ''; Model = ''; Serial = ''; Uptime = ''; Cpu = ''; Ram = ''; Disks = ''; User = ''; Domain = '' }
    $session = $null
    try {
        $opt = New-CimSessionOption -Protocol Wsman
        if ($Credential) { $session = New-CimSession -ComputerName $Ip -Credential $Credential -SessionOption $opt -OperationTimeoutSec $TimeoutSec -ErrorAction Stop }
        else            { $session = New-CimSession -ComputerName $Ip -SessionOption $opt -OperationTimeoutSec $TimeoutSec -ErrorAction Stop }

        $os = Get-CimInstance -CimSession $session -ClassName Win32_OperatingSystem -OperationTimeoutSec $TimeoutSec -ErrorAction Stop
        if ($os) {
            $r.Os = DN-Clean ("$($os.Caption) $($os.OSArchitecture) build $($os.BuildNumber)") 120
            try {
                $up = (Get-Date) - $os.LastBootUpTime
                $r.Uptime = ('{0}g {1:00}:{2:00}' -f $up.Days, $up.Hours, $up.Minutes)
            } catch {}
            try { $r.Ram = ('{0:N1} GB' -f ($os.TotalVisibleMemorySize / 1MB)) } catch {}
        }
        $cs = Get-CimInstance -CimSession $session -ClassName Win32_ComputerSystem -OperationTimeoutSec $TimeoutSec -ErrorAction SilentlyContinue
        if ($cs) {
            $r.Model  = DN-Clean ("$($cs.Manufacturer) $($cs.Model)") 100
            $r.User   = DN-Clean ("$($cs.UserName)") 80
            $r.Domain = DN-Clean ("$($cs.Domain)") 80
        }
        $bi = Get-CimInstance -CimSession $session -ClassName Win32_BIOS -OperationTimeoutSec $TimeoutSec -ErrorAction SilentlyContinue
        if ($bi) { $r.Serial = DN-Clean ("$($bi.SerialNumber)") 60 }
        $cp = Get-CimInstance -CimSession $session -ClassName Win32_Processor -OperationTimeoutSec $TimeoutSec -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cp) { $r.Cpu = DN-Clean ("$($cp.Name) ($($cp.NumberOfCores)C/$($cp.NumberOfLogicalProcessors)T)") 110 }
        $dk = Get-CimInstance -CimSession $session -ClassName Win32_LogicalDisk -Filter 'DriveType=3' -OperationTimeoutSec $TimeoutSec -ErrorAction SilentlyContinue
        if ($dk) {
            $r.Disks = (($dk | ForEach-Object {
                '{0} {1:N0}/{2:N0} GB' -f $_.DeviceID, ($_.FreeSpace / 1GB), ($_.Size / 1GB)
            }) -join ' | ')
        }
    } catch {}
    finally { try { if ($session) { Remove-CimSession $session -ErrorAction SilentlyContinue } } catch {} }
    return $r
}

function DN-ServiceName {
    param([int]$Port)
    switch ($Port) {
        20    { 'ftp-data' } 21   { 'ftp' }      22   { 'ssh' }      23   { 'telnet' }
        25    { 'smtp' }     53   { 'dns' }      67   { 'dhcp' }     69   { 'tftp' }
        80    { 'http' }     88   { 'kerberos' } 110  { 'pop3' }     111  { 'rpcbind' }
        123   { 'ntp' }      135  { 'msrpc' }    137  { 'netbios-ns' } 139 { 'netbios-ssn' }
        143   { 'imap' }     161  { 'snmp' }     389  { 'ldap' }     443  { 'https' }
        445   { 'smb' }      465  { 'smtps' }    514  { 'syslog' }   515  { 'lpd' }
        548   { 'afp' }      554  { 'rtsp' }     587  { 'submission' } 631 { 'ipp' }
        636   { 'ldaps' }    873  { 'rsync' }    902  { 'vmware' }   993  { 'imaps' }
        995   { 'pop3s' }    1080 { 'socks' }    1194 { 'openvpn' }  1433 { 'mssql' }
        1521  { 'oracle' }   1723 { 'pptp' }     1883 { 'mqtt' }     1900 { 'ssdp' }
        2049  { 'nfs' }      2082 { 'cpanel' }   2222 { 'ssh-alt' }  2375 { 'docker' }
        3000  { 'http-alt' } 3128 { 'squid' }    3268 { 'gc-ldap' }  3306 { 'mysql' }
        3389  { 'rdp' }      3690 { 'svn' }      4444 { 'metasploit' } 5000 { 'upnp/http' }
        5060  { 'sip' }      5222 { 'xmpp' }     5353 { 'mdns' }     5432 { 'postgres' }
        5555  { 'adb' }      5601 { 'kibana' }   5900 { 'vnc' }      5985 { 'winrm' }
        5986  { 'winrm-tls' } 6379 { 'redis' }   6667 { 'irc' }      7070 { 'realserver' }
        8000  { 'http-alt' } 8006 { 'proxmox' }  8080 { 'http-proxy' } 8081 { 'http-alt' }
        8123  { 'home-assistant' } 8443 { 'https-alt' } 8888 { 'http-alt' } 9000 { 'http-alt' }
        9090  { 'cockpit' }  9100 { 'jetdirect' } 9200 { 'elastic' } 10000 { 'webmin' }
        11211 { 'memcached' } 27017 { 'mongodb' } 32400 { 'plex' }   49152 { 'upnp' }
        default { "tcp/$Port" }
    }
}

function DN-DeviceType {
    param([int[]]$Ports, [string]$Vendor, [string]$SnmpDescr, [string]$Upnp, [string]$Http, [string]$Os)
    $p = @{}
    foreach ($x in $Ports) { $p[$x] = $true }
    $blob = ("$Vendor $SnmpDescr $Upnp $Http").ToLowerInvariant()
    if ($p[9100] -or $p[515] -or $p[631])                       { return 'Stampante' }
    if ($p[554] -or $blob -match 'hikvision|dahua|axis|camera|ipcam|nvr') { return 'Videocamera / NVR' }
    if ($p[8006])                                               { return 'Proxmox VE' }
    if ($p[902] -or $blob -match 'esxi|vmware')                 { return 'Host virtualizzazione' }
    if ($p[32400] -or $blob -match 'plex|jellyfin|emby')        { return 'Media server' }
    if ($p[2049] -or $p[548] -or $blob -match 'synology|qnap|nas|truenas') { return 'NAS' }
    if ($blob -match 'router|gateway|mikrotik|ubiquiti|fritz|openwrt|dd-wrt|pfsense|edgeos|ios software') { return 'Router / Gateway' }
    if ($blob -match 'switch|catalyst|procurve|aruba|juniper|ex\d{4}') { return 'Switch gestito' }
    if ($p[5060] -or $blob -match 'grandstream|yealink|polycom|snom|asterisk') { return 'VoIP' }
    if ($p[3389] -or $p[5985] -or $p[445] -or $Os -match 'Windows') { return 'Host Windows' }
    if ($p[22]  -and $Os -match 'Linux')                        { return 'Host Linux / Unix' }
    if ($blob -match 'espressif|shelly|tasmota|sonoff|tuya|hue|nest|sonos|roku|chromecast') { return 'Dispositivo IoT' }
    if ($p[80] -or $p[443] -or $p[8080])                        { return 'Dispositivo con web UI' }
    if ($Ports.Count -gt 0)                                     { return 'Host generico' }
    return ''
}

function DN-DeepScan {
    param([string]$Target, [hashtable]$Opt)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r  = @{
        IP = $Target; Input = $Target; Status = 'Non raggiungibile'; StatusRank = 2
        Hostname = ''; NetBiosName = ''; Workgroup = ''; Mac = ''; Vendor = ''
        RttMs = ''; RttAvg = 0.0; Loss = ''; Ttl = ''; Hops = ''; OsGuess = ''; DeviceType = ''
        OpenPorts = @(); Services = ''; HttpTitle = ''; HttpServer = ''
        TlsSubject = ''; TlsIssuer = ''; TlsExpiry = ''; TlsProto = ''
        SshBanner = ''; FtpBanner = ''; SmtpBanner = ''; RdpInfo = ''
        SnmpName = ''; SnmpDescr = ''; SnmpLocation = ''; SnmpContact = ''; SnmpUptime = ''; SnmpOid = ''
        MdnsName = ''; UpnpDevice = ''; UpnpServer = ''; UpnpLocation = ''
        Shares = ''; LoggedUser = ''; Domain = ''
        WmiOs = ''; WmiModel = ''; WmiSerial = ''; WmiUptime = ''; WmiCpu = ''; WmiRam = ''; WmiDisks = ''
        Notes = ''; Error = ''
        Deep = $false
    }

    $ip = $Target
    if ($Target -notmatch '^\d{1,3}(\.\d{1,3}){3}$') {
        $a4 = DN-Resolve4 -Name $Target -TimeoutMs 1500 -Server $Opt.DnsServer
        if ($a4) { $ip = $a4 }
        $r.Hostname = $Target
    }
    $r.IP = $ip

    $pg = DN-Ping -Target $ip -Count $Opt.PingCount -TimeoutMs $Opt.PingTimeoutMs
    if ($pg.Online) {
        $r.Status = 'Online'; $r.StatusRank = 0
        $avg = ($pg.Rtts | Measure-Object -Average).Average
        $r.RttAvg = [Math]::Round([double]$avg, 1)
        if ($pg.Rtts.Count -gt 1) {
            $mn = ($pg.Rtts | Measure-Object -Minimum).Minimum
            $mx = ($pg.Rtts | Measure-Object -Maximum).Maximum
            $r.RttMs = ('{0} ms (min {1} / max {2})' -f $r.RttAvg, $mn, $mx)
        } else {
            $r.RttMs = ('{0} ms' -f $r.RttAvg)
        }
        $loss = [int]((($pg.Sent - $pg.Recv) / [double]$pg.Sent) * 100)
        $r.Loss = "$loss%"
        if ($pg.Ttl -gt 0) {
            $r.Ttl     = "$($pg.Ttl)"
            $r.OsGuess = DN-OsFromTtl $pg.Ttl
            $h = DN-HopsFromTtl $pg.Ttl
            if ($h -ge 0) { $r.Hops = "$h" }
        }
    }

    $r.Mac = DN-GetMac $ip
    if ($r.Mac) {
        $r.Vendor = DN-VendorFromMac $r.Mac $Opt.Oui
        if (-not $pg.Online) { $r.Status = 'Online (ICMP filtrato)'; $r.StatusRank = 1 }
    }

    $isAlive = ($r.StatusRank -le 1)
    if (-not $isAlive -and -not $Opt.ScanDeadHosts) {
        $sw.Stop(); $r.ScanMs = [int]$sw.ElapsedMilliseconds
        return $r
    }

    $ports = @()
    if ($Opt.Ports -and $Opt.Ports.Count -gt 0) {
        $ports = @(DN-ScanPorts -Ip $ip -Ports $Opt.Ports -TimeoutMs $Opt.PortTimeoutMs)
    }
    $r.OpenPorts = @($ports)
    if ($ports.Count -gt 0 -and -not $isAlive) {
        $r.Status = 'Online (ICMP filtrato)'; $r.StatusRank = 1; $isAlive = $true
    }
    if ($ports.Count -gt 0) {
        $r.Services = (($ports | ForEach-Object { "$_/$(DN-ServiceName $_)" }) -join ', ')
    }
    $ph = @{}; foreach ($x in $ports) { $ph[$x] = $true }

    if (-not $isAlive) {
        $sw.Stop(); $r.ScanMs = [int]$sw.ElapsedMilliseconds
        return $r
    }

    $r.Deep = $true
    if ($Opt.ResolveDns -and -not $r.Hostname) {
        $r.Hostname = DN-Ptr -Ip $ip -TimeoutMs 900 -Server $Opt.DnsServer
    }
    if ($Opt.ProbeNetBios) {
        $nb = DN-NetBios -Ip $ip -TimeoutMs 700
        $r.NetBiosName = $nb.Name
        $r.Workgroup   = $nb.Workgroup
        if (-not $r.Mac -and $nb.Mac) {
            $r.Mac = $nb.Mac
            $r.Vendor = DN-VendorFromMac $r.Mac $Opt.Oui
        }
        if ($nb.Users.Count -gt 0) { $r.LoggedUser = ($nb.Users -join ', ') }
    }
    if ($Opt.ProbeMdns -and -not $r.Hostname) {
        $m = DN-Mdns -Ip $ip -TimeoutMs 600
        if ($m) { $r.MdnsName = $m }
    } elseif ($Opt.ProbeMdns) {
        $m = DN-Mdns -Ip $ip -TimeoutMs 400
        if ($m) { $r.MdnsName = $m }
    }

    if ($Opt.ProbeBanners) {
        if ($ph[22])   { $r.SshBanner  = DN-Clean (DN-Banner -Ip $ip -Port 22   -TimeoutMs 900) 120 }
        if ($ph[21])   { $r.FtpBanner  = DN-Clean (DN-Banner -Ip $ip -Port 21   -TimeoutMs 900) 120 }
        if ($ph[25])   { $r.SmtpBanner = DN-Clean (DN-Banner -Ip $ip -Port 25   -TimeoutMs 900) 120 }
        elseif ($ph[587]) { $r.SmtpBanner = DN-Clean (DN-Banner -Ip $ip -Port 587 -TimeoutMs 900) 120 }
        if ($ph[3389]) { $r.RdpInfo = 'RDP in ascolto' }

        $httpPort = @(80, 8080, 8000, 8006, 5000, 3000, 8888, 9090, 10000) | Where-Object { $ph[$_] } | Select-Object -First 1
        if ($httpPort) {
            $hi = DN-HttpProbe -Ip $ip -Port $httpPort -UseTls $false -TimeoutMs 1500
            $r.HttpTitle  = $hi.Title
            $r.HttpServer = $hi.Server
            if ($hi.Powered) { $r.HttpServer = (@($hi.Server, $hi.Powered) | Where-Object { $_ }) -join ' / ' }
            if (-not $r.HttpTitle -and $hi.Redirect) { $r.HttpTitle = '-> ' + $hi.Redirect }
        }
        $tlsPort = @(443, 8443, 5986, 9443) | Where-Object { $ph[$_] } | Select-Object -First 1
        if ($tlsPort) {
            $hs = DN-HttpProbe -Ip $ip -Port $tlsPort -UseTls $true -TimeoutMs 2000
            $r.TlsSubject = $hs.TlsSubject
            $r.TlsIssuer  = $hs.TlsIssuer
            $r.TlsExpiry  = $hs.TlsExpiry
            $r.TlsProto   = $hs.TlsProto
            if (-not $r.HttpTitle)  { $r.HttpTitle  = $hs.Title }
            if (-not $r.HttpServer) { $r.HttpServer = $hs.Server }
        }
    }

    if ($Opt.ProbeSnmp) {
        $sn = DN-Snmp -Ip $ip -Community $Opt.SnmpCommunity -TimeoutMs 900
        $r.SnmpDescr    = $sn.Descr
        $r.SnmpName     = $sn.Name
        $r.SnmpLocation = $sn.Location
        $r.SnmpContact  = $sn.Contact
        $r.SnmpUptime   = $sn.Uptime
        $r.SnmpOid      = $sn.ObjectId
    }

    if ($Opt.ProbeSsdp) {
        $ss = DN-Ssdp -Ip $ip -TimeoutMs 800
        $r.UpnpServer   = $ss.Server
        $r.UpnpDevice   = $ss.Device
        $r.UpnpLocation = $ss.Location
    }

    if ($Opt.ProbeShares -and $ph[445]) {
        $sh = @(DN-Shares -Ip $ip -TimeoutMs 4000)
        if ($sh.Count -gt 0) { $r.Shares = ($sh -join ', ') }
    }
    if ($Opt.ProbeWmi -and ($ph[5985] -or $ph[5986] -or $ph[135])) {
        $w = DN-Wmi -Ip $ip -Credential $Opt.Credential -TimeoutSec 8
        $r.WmiOs     = $w.Os;    $r.WmiModel = $w.Model; $r.WmiSerial = $w.Serial
        $r.WmiUptime = $w.Uptime;$r.WmiCpu   = $w.Cpu;   $r.WmiRam    = $w.Ram
        $r.WmiDisks  = $w.Disks
        if ($w.User)   { $r.LoggedUser = $w.User }
        if ($w.Domain) { $r.Domain     = $w.Domain }
        if ($w.Os)     { $r.OsGuess    = $w.Os }
    }

    if (-not $r.Vendor -or $r.Vendor -eq 'MAC locale / randomizzato') {
        $hay = @($r.SnmpDescr, $r.SnmpName, $r.UpnpServer, $r.UpnpDevice, $r.HttpServer,
                 $r.HttpTitle, $r.SshBanner, $r.FtpBanner, $r.SmtpBanner, $r.MdnsName,
                 $r.Hostname, $r.NetBiosName, $r.TlsSubject, $r.TlsIssuer) -join ' '
        $guess = DN-VendorFromText $hay
        if ($guess) {
            $r.Vendor = if ($r.Vendor) { $guess + ' (da banner)' } else { $guess + ' (da banner)' }
        }
    }
    if (-not $r.Domain -and $r.Workgroup) { $r.Domain = $r.Workgroup }
    if ($r.SnmpDescr -and (-not $r.OsGuess -or $r.OsGuess -notmatch 'Windows')) {
        if ($r.SnmpDescr -match '(?i)(windows|linux|freebsd|ios|junos|routeros|openwrt|vxworks|darwin)') {
            $r.OsGuess = DN-Clean $r.SnmpDescr 90
        }
    }
    if (-not $r.OsGuess -and $r.SshBanner -match '(?i)ubuntu|debian|freebsd|openbsd|centos|raspbian') { $r.OsGuess = DN-Clean $r.SshBanner 80 }
    $r.DeviceType = DN-DeviceType -Ports $ports -Vendor $r.Vendor -SnmpDescr $r.SnmpDescr `
                                  -Upnp ("$($r.UpnpServer) $($r.UpnpDevice)") -Http ("$($r.HttpServer) $($r.HttpTitle)") -Os $r.OsGuess

    $notes = @()
    if ($ph[23])   { $notes += 'Telnet aperto' }
    if ($ph[21])   { $notes += 'FTP aperto' }
    if ($ph[3389]) { $notes += 'RDP esposto' }
    if ($ph[445] -and $ph[139]) { $notes += 'SMB legacy' }
    if ($r.TlsExpiry) {
        try { if ([datetime]$r.TlsExpiry -lt (Get-Date)) { $notes += 'Certificato TLS scaduto' } } catch {}
    }
    if ($notes.Count -gt 0) { $r.Notes = ($notes -join ' - ') }

    $sw.Stop()
    $r.ScanMs = [int]$sw.ElapsedMilliseconds
    return $r
}
'@

$script:ScanWorker = {
    param($Target, $Opt, $Lib)
    try {
        [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
        try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]'Tls12,Tls11,Tls' } catch {}
        . ([scriptblock]::Create($Lib))
        return (DN-DeepScan -Target $Target -Opt $Opt)
    } catch {
        return @{ IP = $Target; Status = 'Errore'; StatusRank = 3; Error = "$_"; OpenPorts = @() }
    }
}

$script:Pool         = $null
$script:Jobs         = New-Object System.Collections.Generic.List[object]
$script:Rows         = New-Object 'System.Collections.ObjectModel.ObservableCollection[DuckNote.ScanRow]'
$script:RowIndex     = New-Object 'System.Collections.Generic.Dictionary[string,DuckNote.ScanRow]' ([StringComparer]::OrdinalIgnoreCase)
$script:ScanTotal    = 0
$script:ScanDone     = 0
$script:ScanActive   = $false
$script:ScanStart    = $null
$script:HostStates   = @{}
$script:Credential   = $null

function New-ScanPool {
    param([int]$MaxThreads)
    if ($script:Pool) {
        try { $script:Pool.Close(); $script:Pool.Dispose() } catch {}
        $script:Pool = $null
    }
    $iss = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault2()
    try { $iss.ApartmentState = [Threading.ApartmentState]::MTA } catch {}
    try { $iss.ThreadOptions  = [System.Management.Automation.Runspaces.PSThreadOptions]::ReuseThread } catch {}
    $script:Pool = [runspacefactory]::CreateRunspacePool(1, $MaxThreads, $iss, $Host)
    $script:Pool.Open()
}

function Get-ScanOptions {
    $ports = @()
    foreach ($tok in ($script:Settings.Ports -split '[,;\s]+')) {
        if ($tok -match '^(\d+)-(\d+)$') {
            $a = [int]$Matches[1]; $b = [int]$Matches[2]
            if ($a -ge 1 -and $b -le 65535 -and $b -ge $a -and ($b - $a) -le 2048) {
                for ($p = $a; $p -le $b; $p++) { $ports += $p }
            }
        } elseif ($tok -match '^\d+$') {
            $p = [int]$tok
            if ($p -ge 1 -and $p -le 65535) { $ports += $p }
        }
    }
    $ports = $ports | Sort-Object -Unique
    return @{
        PingCount     = [int]$script:Settings.PingCount
        PingTimeoutMs = [int]$script:Settings.PingTimeoutMs
        PortTimeoutMs = [int]$script:Settings.PortTimeoutMs
        ScanDeadHosts = [bool]$script:Settings.ScanDeadHosts
        ResolveDns    = [bool]$script:Settings.ResolveDns
        DnsServer     = [string]$script:Settings.DnsServer
        ProbeNetBios  = [bool]$script:Settings.ProbeNetBios
        ProbeMdns     = [bool]$script:Settings.ProbeMdns
        ProbeSsdp     = [bool]$script:Settings.ProbeSsdp
        ProbeSnmp     = [bool]$script:Settings.ProbeSnmp
        ProbeBanners  = [bool]$script:Settings.ProbeBanners
        ProbeShares   = [bool]$script:Settings.ProbeShares
        ProbeWmi      = [bool]$script:Settings.ProbeWmi
        SnmpCommunity = [string]$script:Settings.SnmpCommunity
        Ports         = @($ports)
        Oui           = $script:OuiMap
        Credential    = $script:Credential
    }
}

function Expand-Targets {
    param([string]$Spec, [int]$Cap = 8192)
    $out  = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    if ([string]::IsNullOrWhiteSpace($Spec)) { return $out }
    foreach ($chunk in ($Spec -split '[,;\r\n]+')) {
        $t = $chunk.Trim()
        if (-not $t) { continue }
        $add = @()
        if ($t -match '^(\d{1,3}(?:\.\d{1,3}){3})/(\d{1,2})$') {
            $ip   = $null
            $bits = [int]$Matches[2]
            if (-not [Net.IPAddress]::TryParse($Matches[1], [ref]$ip)) { continue }
            if ($bits -lt 8 -or $bits -gt 32) { continue }
            $base = $ip.GetAddressBytes()
            [Array]::Reverse($base)
            $b32  = [int64][BitConverter]::ToUInt32($base, 0)
            $full  = 0xFFFFFFFFL
            $mask  = ($full -shl (32 - $bits)) -band $full
            $net   = $b32 -band $mask
            $bcast = $net -bor ($full -bxor $mask)
            $first = if ($bits -ge 31) { $net } else { $net + 1 }
            $last  = if ($bits -ge 31) { $bcast } else { $bcast - 1 }
            if (($last - $first) -gt $Cap) { $last = $first + $Cap }
            for ($u = $first; $u -le $last; $u++) {
                $bb = [BitConverter]::GetBytes([uint32]$u); [Array]::Reverse($bb)
                $add += ([Net.IPAddress]::new($bb)).ToString()
            }
        }
        elseif ($t -match '^(\d{1,3}(?:\.\d{1,3}){3})\s*-\s*(\d{1,3}(?:\.\d{1,3}){3})$') {
            $da = $null; $fino = $null
            if (-not [Net.IPAddress]::TryParse($Matches[1], [ref]$da))   { continue }
            if (-not [Net.IPAddress]::TryParse($Matches[2], [ref]$fino)) { continue }
            $a = $da.GetAddressBytes();   [Array]::Reverse($a)
            $b = $fino.GetAddressBytes(); [Array]::Reverse($b)
            $ua = [BitConverter]::ToUInt32($a, 0); $ub = [BitConverter]::ToUInt32($b, 0)
            if ($ub -lt $ua) { $tmp = $ua; $ua = $ub; $ub = $tmp }
            if (($ub - $ua) -gt $Cap) { $ub = $ua + $Cap }
            for ($u = $ua; $u -le $ub; $u++) {
                $bb = [BitConverter]::GetBytes([uint32]$u); [Array]::Reverse($bb)
                $add += ([Net.IPAddress]::new($bb)).ToString()
            }
        }
        elseif ($t -match '^(\d{1,3}\.\d{1,3}\.\d{1,3})\.(\d{1,3})\s*-\s*(\d{1,3})$') {
            $pre = $Matches[1]; $s = [int]$Matches[2]; $e = [int]$Matches[3]
            if ($e -lt $s) { $tmp = $s; $s = $e; $e = $tmp }
            $s = [Math]::Max(0, $s); $e = [Math]::Min(255, $e)
            for ($u = $s; $u -le $e; $u++) { $add += "$pre.$u" }
        }
        else { $add += $t }

        foreach ($x in $add) {
            if (-not $seen.ContainsKey($x)) { $seen[$x] = $true; [void]$out.Add($x) }
            if ($out.Count -ge $Cap) { break }
        }
        if ($out.Count -ge $Cap) { break }
    }
    return $out
}

function Get-LocalRanges {
    $res = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($ni in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
            if ($ni.OperationalStatus -ne 'Up') { continue }
            if ($ni.NetworkInterfaceType -eq 'Loopback') { continue }
            foreach ($ua in $ni.GetIPProperties().UnicastAddresses) {
                if ($ua.Address.AddressFamily -ne 'InterNetwork') { continue }
                $ipb = $ua.Address.GetAddressBytes()
                if ($ipb[0] -eq 169) { continue }
                $bits = 24
                try { if ($ua.PrefixLength -gt 0) { $bits = [int]$ua.PrefixLength } } catch {}
                if ($bits -lt 22) { $bits = 24 }
                $mb = [Net.IPAddress]::Parse($ua.Address.ToString()).GetAddressBytes()
                [Array]::Reverse($mb)
                $u = [BitConverter]::ToUInt32($mb, 0)
                $mask = [uint32]([uint32]0xFFFFFFFF -shl (32 - $bits))
                $net = $u -band $mask
                $nb = [BitConverter]::GetBytes([uint32]$net); [Array]::Reverse($nb)
                $cidr = ([Net.IPAddress]::new($nb)).ToString() + "/$bits"
                if (-not $res.Contains($cidr)) { [void]$res.Add($cidr) }
            }
        }
    } catch {}
    return $res
}

function Get-OrCreate-Row {
    param([string]$Ip)
    if ($script:RowIndex.ContainsKey($Ip)) { return $script:RowIndex[$Ip] }
    $row = New-Object DuckNote.ScanRow
    $row.IP         = $Ip
    $row.Status     = 'In coda'
    $row.StatusRank = 4
    try {
        $b = ([Net.IPAddress]::Parse($Ip)).GetAddressBytes()
        if ($b.Length -eq 4) {
            $row.SortKey = ([long]$b[0] -shl 24) -bor ([long]$b[1] -shl 16) -bor ([long]$b[2] -shl 8) -bor [long]$b[3]
        }
    } catch { $row.SortKey = 0 }
    $row.DotColor = Get-DotHex 4
    $script:RowIndex[$Ip] = $row
    $script:Rows.Add($row)
    return $row
}

function Apply-ResultToRow {
    param([hashtable]$Res)
    $ip  = [string]$Res.IP
    $key = [string]$Res.Input
    if (-not $key) { $key = $ip }
    if (-not $ip)  { return }
    if ($script:Ignored.Contains($key) -or $script:Ignored.Contains($ip)) { return }
    $row = Get-OrCreate-Row $key
    $row.IP = $ip
    if (-not $script:RowIndex.ContainsKey($ip)) { $script:RowIndex[$ip] = $row }
    try {
        $b = ([Net.IPAddress]::Parse($ip)).GetAddressBytes()
        if ($b.Length -eq 4) {
            $row.SortKey = ([long]$b[0] -shl 24) -bor ([long]$b[1] -shl 16) -bor ([long]$b[2] -shl 8) -bor [long]$b[3]
        }
    } catch {}
    $row.Status       = [string]$Res.Status
    $row.StatusRank   = [int]$Res.StatusRank
    $row.RttMs        = [string]$Res.RttMs
    $row.RttAvg       = [double]$Res.RttAvg
    $row.Loss         = [string]$Res.Loss
    $row.Ttl          = [string]$Res.Ttl
    $row.LastSeen     = (Get-Date).ToString('HH:mm:ss')
    $row.ScanMs       = [int]$Res.ScanMs
    $row.DotColor     = Get-DotHex $row.StatusRank

    if ($Res.Mac) { $row.Mac = [string]$Res.Mac; $row.Vendor = [string]$Res.Vendor }

    $alive = ($row.StatusRank -le 1)
    $script:HostStates[$ip]  = $alive
    $script:HostStates[$key] = $alive
    if ($Res.Hostname) { $script:HostStates[$Res.Hostname] = $alive }

    if (-not $Res.Deep) { Sync-DetailRow $row; return $row }

    if ($Res.Hostname)    { $row.Hostname    = [string]$Res.Hostname }
    if ($Res.NetBiosName) { $row.NetBiosName = [string]$Res.NetBiosName }
    if ($Res.MdnsName)    { $row.MdnsName    = [string]$Res.MdnsName }

    $row.Workgroup    = [string]$Res.Workgroup
    $row.OsGuess      = [string]$Res.OsGuess
    $row.DeviceType   = [string]$Res.DeviceType
    $row.Services     = [string]$Res.Services
    $row.HttpTitle    = [string]$Res.HttpTitle
    $row.HttpServer   = [string]$Res.HttpServer
    $row.TlsSubject   = [string]$Res.TlsSubject
    $row.TlsIssuer    = [string]$Res.TlsIssuer
    $row.TlsExpiry    = [string]$Res.TlsExpiry
    $row.SshBanner    = [string]$Res.SshBanner
    $row.FtpBanner    = [string]$Res.FtpBanner
    $row.SmtpBanner   = [string]$Res.SmtpBanner
    $row.RdpInfo      = [string]$Res.RdpInfo
    $row.SnmpName     = [string]$Res.SnmpName
    $row.SnmpDescr    = [string]$Res.SnmpDescr
    $row.SnmpLocation = [string]$Res.SnmpLocation
    $row.SnmpContact  = [string]$Res.SnmpContact
    $row.SnmpUptime   = [string]$Res.SnmpUptime
    $row.UpnpDevice   = [string]$Res.UpnpDevice
    $row.UpnpServer   = [string]$Res.UpnpServer
    $row.Shares       = [string]$Res.Shares
    $row.LoggedUser   = [string]$Res.LoggedUser
    $row.Domain       = [string]$Res.Domain
    $row.WmiOs        = [string]$Res.WmiOs
    $row.WmiModel     = [string]$Res.WmiModel
    $row.WmiSerial    = [string]$Res.WmiSerial
    $row.WmiUptime    = [string]$Res.WmiUptime
    $row.WmiCpu       = [string]$Res.WmiCpu
    $row.WmiRam       = [string]$Res.WmiRam
    $row.WmiDisks     = [string]$Res.WmiDisks
    $row.Notes        = [string]$Res.Notes
    $op = @($Res.OpenPorts)
    $row.PortCount = $op.Count
    $row.OpenPorts = ($op -join ', ')

    Sync-DetailRow $row
    return $row
}

function Sync-DetailRow {
    param($Row)
    if (-not $script:DetailRow -or $script:DetailRow.IP -ne $Row.IP) { return }
    $script:DetailRow = $Row
    if ($script:DetailOpen) { Fill-Details $Row }
}

$script:IsPS7          = ($PSVersionTable.PSVersion.Major -ge 7)
$script:ScanMode       = 'pool'
$script:ScanQueue      = $null
$script:ScanCancel     = $null
$script:ParallelPS     = $null
$script:ParallelHandle = $null

$script:ScanLibGlobal = $script:ScanLib -replace '(?m)^function\s+(DN-[A-Za-z0-9]+)', 'function global:$1'

. ([scriptblock]::Create($script:ScanLib))

$script:ParallelWorkerText = @'
param($Targets, $Opt, $Lib, $Queue, $Throttle, $Cancel)

$Targets | ForEach-Object -ThrottleLimit $Throttle -Parallel {
    $item = $_
    $tok  = $using:Cancel
    if ($tok.IsCancellationRequested) { return }

    if (-not (Get-Command DN-DeepScan -ErrorAction SilentlyContinue)) {
        . ([scriptblock]::Create($using:Lib))
        [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
        try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]'Tls12,Tls11,Tls' } catch {}
    }

    try {
        $res = DN-DeepScan -Target $item -Opt $using:Opt
    } catch {
        $res = @{ IP = $item; Input = $item; Status = 'Errore'; StatusRank = 3
                  Error = "$($_.Exception.Message)"; OpenPorts = @() }
    }
    ($using:Queue).Enqueue($res)
}
'@

function Start-ParallelScan {
    param([string[]]$Targets, [hashtable]$Opt)
    $script:ScanQueue  = New-Object 'System.Collections.Concurrent.ConcurrentQueue[object]'
    $script:ScanCancel = New-Object System.Threading.CancellationTokenSource
    $script:ParallelPS = [powershell]::Create()
    [void]$script:ParallelPS.AddScript($script:ParallelWorkerText).
        AddArgument([string[]]$Targets).
        AddArgument($Opt).
        AddArgument($script:ScanLibGlobal).
        AddArgument($script:ScanQueue).
        AddArgument([int]$script:Settings.MaxThreads).
        AddArgument($script:ScanCancel)
    $script:ParallelHandle = $script:ParallelPS.BeginInvoke()
}

function Stop-ParallelScan {
    try { if ($script:ScanCancel) { $script:ScanCancel.Cancel() } } catch {}
    try { if ($script:ParallelPS) { [void]$script:ParallelPS.BeginStop($null, $null) } } catch {}
}

function Clear-ParallelScan {
    if ($script:ParallelHandle -and -not $script:ParallelHandle.IsCompleted) { return }
    try { if ($script:ParallelPS) { $script:ParallelPS.Dispose() } } catch {}
    try { if ($script:ScanCancel) { $script:ScanCancel.Dispose() } } catch {}
    $script:ParallelPS     = $null
    $script:ParallelHandle = $null
    $script:ScanCancel     = $null
}

function Start-Scan {
    param([string[]]$Targets, [switch]$KeepExisting)
    if ($script:ScanActive) { return }
    $Targets = @($Targets | Where-Object { $_ -and $_.Trim() -and -not $script:Ignored.Contains($_.Trim()) } |
                 Select-Object -Unique)
    if ($Targets.Count -eq 0) { Set-Status 'Nessun target da analizzare.' ; return }

    if (-not $KeepExisting) {
        $script:Rows.Clear(); $script:RowIndex.Clear()
    }
    $script:ScanMode = if ($script:IsPS7 -and $script:Settings.UseParallel) { 'parallel' } else { 'pool' }

    $opt = Get-ScanOptions
    $script:Jobs.Clear()
    $script:ScanTotal  = $Targets.Count
    $script:ScanDone   = 0
    $script:ScanActive = $true
    $script:ScanStart  = Get-Date

    foreach ($t in $Targets) {
        $row = Get-OrCreate-Row $t
        if ($KeepExisting) { $row.NoteKey = $t }
        else {
            $row.Status = 'In coda'; $row.StatusRank = 4; $row.DotColor = Get-DotHex 4
        }
    }

    if ($script:ScanMode -eq 'parallel') {
        try { Start-ParallelScan -Targets $Targets -Opt $opt }
        catch {
            Set-Status "Motore parallelo non disponibile, uso il pool: $($_.Exception.Message)"
            $script:ScanMode = 'pool'
        }
    }

    if ($script:ScanMode -eq 'pool') {
        try { New-ScanPool -MaxThreads ([int]$script:Settings.MaxThreads) }
        catch { $script:ScanActive = $false; Set-Status "Impossibile creare il pool: $_" ; return }
        foreach ($t in $Targets) {
            $ps = [powershell]::Create()
            $ps.RunspacePool = $script:Pool
            [void]$ps.AddScript($script:ScanWorker.ToString()).AddArgument($t).AddArgument($opt).AddArgument($script:ScanLib)
            $h = $ps.BeginInvoke()
            [void]$script:Jobs.Add([pscustomobject]@{ PS = $ps; Handle = $h; Target = $t })
        }
    }

    Update-ScanUi
    $script:PumpTimer.Start()
}

function Stop-Scan {
    if (-not $script:ScanActive) { return }
    $script:ScanActive = $false

    if ($script:ScanMode -eq 'parallel') {
        Stop-ParallelScan
    } else {
        foreach ($j in @($script:Jobs)) {
            try { if (-not $j.Handle.IsCompleted) { [void]$j.PS.BeginStop($null, $null) } } catch {}
        }
        $script:Jobs.Clear()
        $script:PumpTimer.Stop()
    }

    foreach ($r in $script:Rows) {
        if ($r.StatusRank -eq 4) { $r.Status = 'Annullato'; $r.StatusRank = 5; $r.DotColor = Get-DotHex 5 }
    }
    Update-ScanUi
    Set-Status 'Scansione annullata.'
}

function Pump-ScanResults {
    if ($script:ScanMode -eq 'parallel') { Pump-ParallelResults; return }
    if ($script:Jobs.Count -eq 0) {
        $script:PumpTimer.Stop()
        if ($script:ScanActive) {
            $script:ScanActive = $false
            $el = (Get-Date) - $script:ScanStart
            $up = @($script:Rows | Where-Object { $_.StatusRank -le 1 }).Count
            Set-Status ('Scansione completata: {0} host attivi su {1} in {2:N1} s.' -f $up, $script:ScanTotal, $el.TotalSeconds)
            Update-ScanUi
            Refresh-EditorDots
            Save-ScanSnapshot
        }
        return
    }
    $done = New-Object System.Collections.Generic.List[object]
    $budget = 0
    foreach ($j in $script:Jobs) {
        if (-not $j.Handle.IsCompleted) { continue }
        [void]$done.Add($j)
        $budget++
        if ($budget -ge 48) { break }
    }
    foreach ($j in $done) {
        try {
            $out = $j.PS.EndInvoke($j.Handle)
            foreach ($o in @($out)) {
                if ($o -is [hashtable]) { [void](Apply-ResultToRow $o) }
            }
        } catch {
            $row = Get-OrCreate-Row $j.Target
            $row.Status = 'Errore'; $row.StatusRank = 3; $row.Notes = "$_"; $row.DotColor = Get-DotHex 3
        }
        finally {
            try { $j.PS.Dispose() } catch {}
            [void]$script:Jobs.Remove($j)
            $script:ScanDone++
        }
    }
    if ($done.Count -gt 0) { Update-ScanUi }
}

function Pump-ParallelResults {
    $applied = 0
    if ($script:ScanQueue) {
        $item = $null
        while ($applied -lt 64 -and $script:ScanQueue.TryDequeue([ref]$item)) {
            try { if ($item -is [hashtable]) { [void](Apply-ResultToRow $item) } } catch {}
            $script:ScanDone++
            $applied++
        }
    }
    if ($applied -gt 0) { Update-ScanUi }

    $done = ($null -eq $script:ParallelHandle) -or $script:ParallelHandle.IsCompleted
    if ($done -and (($null -eq $script:ScanQueue) -or $script:ScanQueue.IsEmpty)) {
        $script:PumpTimer.Stop()
        if ($script:ScanActive) {
            $script:ScanActive = $false
            foreach ($r in $script:Rows) {
                if ($r.StatusRank -eq 4) { $r.Status = 'Non completato'; $r.StatusRank = 5; $r.DotColor = Get-DotHex 5 }
            }
            $el = (Get-Date) - $script:ScanStart
            $up = @($script:Rows | Where-Object { $_.StatusRank -le 1 }).Count
            Set-Status ('Scansione completata: {0} host attivi su {1} in {2:N1} s ({3} thread paralleli).' -f `
                        $up, $script:ScanTotal, $el.TotalSeconds, $script:Settings.MaxThreads)
            Update-ScanUi
            Refresh-EditorDots
            Save-ScanSnapshot
        }
        Clear-ParallelScan
    }
}

function Read-ScanSnapshot {
    if (Test-VaultOpen) {
        [byte[]]$b = Get-VaultSection $script:SezioneScansione
        if ($b) { return [Text.Encoding]::UTF8.GetString($b) }
        return $null
    }
    if (Test-VaultLocked) { return $null }
    if (Test-Path $script:ScanFile) { return (Get-Content $script:ScanFile -Raw -Encoding UTF8) }
    return $null
}

function Restore-ScanSnapshot {
    $json = Read-ScanSnapshot
    if (-not $json) { return 0 }
    try { $voci = @(($json | ConvertFrom-Json) | ForEach-Object { $_ }) } catch { return 0 }

    $quanti = 0
    foreach ($v in $voci) {
        if (-not $v.IP) { continue }
        $row = Get-OrCreate-Row $v.IP
        $row.Hostname    = [string]$v.Hostname
        $row.NetBiosName = [string]$v.NetBios
        $row.Mac         = [string]$v.Mac
        $row.Vendor      = [string]$v.Vendor
        $row.OsGuess     = [string]$v.Os
        $row.DeviceType  = [string]$v.Device
        $row.OpenPorts   = [string]$v.Ports
        $row.LastSeen    = [string]$v.Seen
        $row.NoteKey     = [string]$v.Chiave

        $rank = if ($null -ne $v.Rank) { [int]$v.Rank } else { 0 }
        $row.StatusRank  = $rank
        $row.Status      = if ($v.Stato) { [string]$v.Stato } else { 'Da sessione precedente' }
        $row.DotColor    = Get-DotHex $rank

        $vivo = ($rank -le 1)
        foreach ($nome in @($v.IP, $v.Chiave, $v.Hostname)) {
            if ($nome) { $script:HostStates[[string]$nome] = $vivo }
        }
        $quanti++
    }
    return $quanti
}

function Save-ScanSnapshot {
    if ((Test-VaultLocked) -and -not (Test-VaultOpen)) { return }
    try {
        $data = @($script:Rows | Where-Object { $_.StatusRank -le 2 } | ForEach-Object {
            [pscustomobject]@{
                IP = $_.IP; Hostname = $_.Hostname; NetBios = $_.NetBiosName; Mac = $_.Mac
                Vendor = $_.Vendor; Os = $_.OsGuess; Device = $_.DeviceType
                Ports = $_.OpenPorts; Seen = $_.LastSeen
                Stato = $_.Status; Rank = $_.StatusRank; Chiave = $_.NoteKey
            }
        })
        $json = $data | ConvertTo-Json -Depth 3
        if (-not $json) { $json = '[]' }
        if (Test-VaultOpen) {
            Set-VaultSection $script:SezioneScansione ([Text.Encoding]::UTF8.GetBytes($json))
            Save-VaultStore
        } else {
            $json | Out-File $script:ScanFile -Encoding UTF8 -Force
        }
    } catch {}
}

$script:MainXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="DuckNote" Height="740" Width="1180" MinWidth="900" MinHeight="560"
        WindowStartupLocation="CenterScreen" WindowStyle="None" ResizeMode="CanResize"
        AllowsTransparency="True" UseLayoutRounding="True" SnapsToDevicePixels="True"
        TextOptions.TextFormattingMode="Ideal" TextOptions.TextRenderingMode="Auto"
        FontFamily="SF Pro Text, Segoe UI Variable Text, Segoe UI" FontSize="13"
        Background="Transparent">

  <shell:WindowChrome.WindowChrome>
    <shell:WindowChrome CaptionHeight="52" CornerRadius="10" GlassFrameThickness="0"
                        ResizeBorderThickness="7" UseAeroCaptionButtons="False"/>
  </shell:WindowChrome.WindowChrome>

  <Window.Resources>
    <SolidColorBrush x:Key="BgWindow"        Color="#8CFFFFFF"/>
    <SolidColorBrush x:Key="BgSidebar"       Color="#F4F4F5"/>
    <SolidColorBrush x:Key="BgToolbar"       Color="#FFFFFF"/>
    <SolidColorBrush x:Key="BgToolbarGlass"  Color="#99FFFFFF"/>
    <SolidColorBrush x:Key="BgSidebarGlass"  Color="#8CF4F4F5"/>
    <SolidColorBrush x:Key="BgContentGlass"  Color="#59EAEAEC"/>
    <SolidColorBrush x:Key="BgContent"       Color="#FFFFFF"/>
    <SolidColorBrush x:Key="BgGrouped"       Color="#F4F4F5"/>
    <SolidColorBrush x:Key="BgElevated"      Color="#FFFFFF"/>
    <SolidColorBrush x:Key="BgField"         Color="#FFFFFF"/>
    <SolidColorBrush x:Key="BgFieldAlt"      Color="#EFEFF1"/>
    <SolidColorBrush x:Key="BgRowAlt"        Color="#FAFAFB"/>
    <SolidColorBrush x:Key="BgHover"         Color="#ECECEE"/>
    <SolidColorBrush x:Key="BgPressed"       Color="#E0E0E3"/>
    <SolidColorBrush x:Key="Panel"           Color="#E6FFFFFF"/>
    <SolidColorBrush x:Key="PanelSoft"       Color="#DEF4F4F5"/>
    <SolidColorBrush x:Key="EdgeHighlight"   Color="#B3FFFFFF"/>
    <SolidColorBrush x:Key="Separator"       Color="#E2E2E4"/>
    <SolidColorBrush x:Key="SeparatorSoft"   Color="#EFEFF1"/>
    <SolidColorBrush x:Key="BorderControl"   Color="#D5D5D8"/>
    <SolidColorBrush x:Key="Label"           Color="#1F2328"/>
    <SolidColorBrush x:Key="LabelSecondary"  Color="#6A737D"/>
    <SolidColorBrush x:Key="LabelTertiary"   Color="#8D949C"/>
    <SolidColorBrush x:Key="LabelQuaternary" Color="#B6BCC2"/>
    <SolidColorBrush x:Key="LabelOnAccent"   Color="#FFFFFF"/>
    <SolidColorBrush x:Key="Blue"            Color="#2E6BE6"/>
    <SolidColorBrush x:Key="BlueDeep"        Color="#1D52C0"/>
    <SolidColorBrush x:Key="Green"           Color="#1F9D45"/>
    <SolidColorBrush x:Key="Red"             Color="#C0392B"/>
    <SolidColorBrush x:Key="Orange"          Color="#FF8C00"/>
    <SolidColorBrush x:Key="Yellow"          Color="#E0A100"/>
    <SolidColorBrush x:Key="Purple"          Color="#6D5FD6"/>
    <SolidColorBrush x:Key="Indigo"          Color="#4C46B8"/>
    <SolidColorBrush x:Key="Teal"            Color="#0E8C8C"/>
    <SolidColorBrush x:Key="Pink"            Color="#D6337A"/>
    <SolidColorBrush x:Key="Gray"            Color="#8D949C"/>
    <SolidColorBrush x:Key="TLClose"         Color="#C0392B"/>
    <SolidColorBrush x:Key="TLMin"           Color="#FF8C00"/>
    <SolidColorBrush x:Key="TLZoom"          Color="#1F9D45"/>
    <SolidColorBrush x:Key="TLIdle"          Color="#CFCFD3"/>
    <SolidColorBrush x:Key="CodeBg"          Color="#F1F1F3"/>
    <SolidColorBrush x:Key="CodeFg"          Color="#B4009E"/>
    <SolidColorBrush x:Key="SelectionBg"     Color="#FFD9A6"/>
    <SolidColorBrush x:Key="TableHeaderBg"   Color="#F4F4F5"/>
    <SolidColorBrush x:Key="TableBorder"     Color="#E2E2E4"/>
    <SolidColorBrush x:Key="Accent"          Color="#FF8C00"/>
    <SolidColorBrush x:Key="AccentDark"      Color="#E07B00"/>
    <SolidColorBrush x:Key="AccentSoft"      Color="#1FFF8C00"/>
    <SolidColorBrush x:Key="AccentBorder"    Color="#59FF8C00"/>
    <SolidColorBrush x:Key="SelectionRowBg"  Color="#E4E4E8"/>
    <SolidColorBrush x:Key="SheetScrim"      Color="#D6FFFFFF"/>
    <SolidColorBrush x:Key="GlassEdge"       Color="#59FFFFFF"/>
    <SolidColorBrush x:Key="SelectionRowFg"  Color="#141619"/>
    <SolidColorBrush x:Key="AccentText"      Color="#1A1A1A"/>
    <SolidColorBrush x:Key="GridMinor"       Color="#14000000"/>
    <SolidColorBrush x:Key="GridMajor"       Color="#33FF8C00"/>
    <LinearGradientBrush x:Key="AccentFill" StartPoint="0,0" EndPoint="1,1">
      <GradientStop Color="#FFD700" Offset="0"/>
      <GradientStop Color="#FF8C00" Offset="1"/>
    </LinearGradientBrush>
    <SolidColorBrush x:Key="DuckOrange"      Color="#FF8C00"/>
    <SolidColorBrush x:Key="Highlight"       Color="#FFEBC2"/>
    <SolidColorBrush x:Key="HighlightFg"     Color="#1F2328"/>

    <CornerRadius x:Key="RControl">6</CornerRadius>
    <CornerRadius x:Key="RRow">7</CornerRadius>
    <CornerRadius x:Key="RField">8</CornerRadius>
    <CornerRadius x:Key="RContent">12</CornerRadius>
    <CornerRadius x:Key="RPanel">16</CornerRadius>

    <DrawingBrush x:Key="GridBackdrop" TileMode="Tile" Stretch="None"
                  Viewport="0,0,160,160" ViewportUnits="Absolute"
                  RenderOptions.CachingHint="Cache"
                  RenderOptions.CacheInvalidationThresholdMinimum="0.5"
                  RenderOptions.CacheInvalidationThresholdMaximum="2.0">
      <DrawingBrush.Drawing>
        <DrawingGroup>
          <GeometryDrawing Geometry="M0,40 H160 M0,80 H160 M0,120 H160 M40,0 V160 M80,0 V160 M120,0 V160">
            <GeometryDrawing.Pen><Pen Brush="{StaticResource GridMinor}" Thickness="1"/></GeometryDrawing.Pen>
          </GeometryDrawing>
          <GeometryDrawing Geometry="M0,0 H160 M0,0 V160">
            <GeometryDrawing.Pen><Pen Brush="{StaticResource GridMajor}" Thickness="1"/></GeometryDrawing.Pen>
          </GeometryDrawing>
        </DrawingGroup>
      </DrawingBrush.Drawing>
    </DrawingBrush>

    <LinearGradientBrush x:Key="EdgeSheen" StartPoint="0,0" EndPoint="0,1">
      <GradientStop Color="#59FFFFFF" Offset="0"/>
      <GradientStop Color="#00FFFFFF" Offset="0.5"/>
    </LinearGradientBrush>

    <Style x:Key="ScrollThumb" TargetType="Thumb">
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Thumb">
            <Border x:Name="t" CornerRadius="4" Background="{DynamicResource LabelQuaternary}" Margin="3,2,3,2"/>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="t" Property="Background" Value="{DynamicResource LabelTertiary}"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType="ScrollBar">
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Width" Value="11"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ScrollBar">
            <Grid Background="Transparent">
              <Track x:Name="PART_Track" IsDirectionReversed="True">
                <Track.DecreaseRepeatButton>
                  <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/>
                </Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb Style="{StaticResource ScrollThumb}"/></Track.Thumb>
                <Track.IncreaseRepeatButton>
                  <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/>
                </Track.IncreaseRepeatButton>
              </Track>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="Orientation" Value="Horizontal">
          <Setter Property="Height" Value="11"/>
          <Setter Property="Width" Value="Auto"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ScrollBar">
                <Grid Background="Transparent">
                  <Track x:Name="PART_Track" IsDirectionReversed="False">
                    <Track.DecreaseRepeatButton>
                      <RepeatButton Command="ScrollBar.PageLeftCommand" Opacity="0" Focusable="False"/>
                    </Track.DecreaseRepeatButton>
                    <Track.Thumb><Thumb Style="{StaticResource ScrollThumb}"/></Track.Thumb>
                    <Track.IncreaseRepeatButton>
                      <RepeatButton Command="ScrollBar.PageRightCommand" Opacity="0" Focusable="False"/>
                    </Track.IncreaseRepeatButton>
                  </Track>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Trigger>
      </Style.Triggers>
    </Style>

    <Style x:Key="TrafficBtn" TargetType="Button">
      <Setter Property="Width" Value="12"/>
      <Setter Property="Height" Value="12"/>
      <Setter Property="Margin" Value="0,0,8,0"/>
      <Setter Property="Cursor" Value="Arrow"/>
      <Setter Property="Focusable" Value="False"/>
      <Setter Property="shell:WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Grid>
              <Ellipse x:Name="dot" Fill="{TemplateBinding Background}"/>
              <ContentPresenter x:Name="gl" Margin="3" Opacity="0"
                                HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="gl" Property="Opacity" Value="1"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="dot" Property="Opacity" Value="0.7"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="Toolbtn" TargetType="Button">
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Foreground" Value="{DynamicResource LabelSecondary}"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Height" Value="28"/>
      <Setter Property="MinWidth" Value="28"/>
      <Setter Property="Padding" Value="7,0"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="shell:WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="b" CornerRadius="{StaticResource RControl}" Background="{TemplateBinding Background}"
                    Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource BgHover}"/>
                <Setter Property="Foreground" Value="{DynamicResource Label}"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource BgPressed}"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.4"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="PrimaryBtn" TargetType="Button" BasedOn="{StaticResource Toolbtn}">
      <Setter Property="Foreground" Value="{DynamicResource AccentText}"/>
      <Setter Property="Background" Value="{StaticResource AccentFill}"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Height" Value="30"/>
      <Setter Property="Padding" Value="16,0"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="b" CornerRadius="{StaticResource RControl}" Background="{TemplateBinding Background}"
                    Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="b" Property="Opacity" Value="0.88"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="b" Property="Opacity" Value="0.72"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource BgPressed}"/>
                <Setter Property="Foreground" Value="{DynamicResource LabelTertiary}"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="QuietBtn" TargetType="Button">
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Height" Value="30"/>
      <Setter Property="MinWidth" Value="30"/>
      <Setter Property="Padding" Value="12,0"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="shell:WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="b" CornerRadius="{StaticResource RControl}" Background="{DynamicResource PanelSoft}"
                    BorderBrush="{DynamicResource BorderControl}" BorderThickness="1"
                    Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                <Setter TargetName="b" Property="Background" Value="{DynamicResource AccentSoft}"/>
                <Setter Property="Foreground" Value="{DynamicResource Accent}"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="b" Property="Opacity" Value="0.75"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Foreground" Value="{DynamicResource LabelQuaternary}"/>
                <Setter TargetName="b" Property="Opacity" Value="0.6"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="SegBtn" TargetType="RadioButton">
      <Setter Property="Foreground" Value="{DynamicResource LabelSecondary}"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="FontWeight" Value="Medium"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Height" Value="26"/>
      <Setter Property="MinWidth" Value="78"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="shell:WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="RadioButton">
            <Border x:Name="b" CornerRadius="{StaticResource RControl}" Background="{TemplateBinding Background}" Padding="14,0">
              <ContentPresenter x:Name="cp" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource Accent}"/>
              </Trigger>
              <MultiTrigger>
                <MultiTrigger.Conditions>
                  <Condition Property="IsMouseOver" Value="True"/>
                  <Condition Property="IsChecked"   Value="True"/>
                </MultiTrigger.Conditions>
                <Setter Property="Foreground" Value="{DynamicResource AccentText}"/>
              </MultiTrigger>
              <Trigger Property="IsChecked" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource AccentText}"/>
                <Setter Property="FontWeight" Value="SemiBold"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="Field" TargetType="TextBox">
      <Setter Property="Height" Value="28"/>
      <Setter Property="Padding" Value="10,0"/>
      <Setter Property="VerticalContentAlignment" Value="Center"/>
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="CaretBrush" Value="{DynamicResource Label}"/>
      <Setter Property="SelectionBrush" Value="{DynamicResource Accent}"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="TextBox">
            <Grid>
              <Border x:Name="glow" CornerRadius="{StaticResource RContent}" Background="{DynamicResource AccentSoft}"
                      Margin="-3" Opacity="0"/>
              <Border x:Name="b" CornerRadius="{StaticResource RField}" Background="{DynamicResource BgField}"
                      BorderBrush="{DynamicResource BorderControl}" BorderThickness="1">
                <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"
                              VerticalAlignment="Center"/>
              </Border>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource LabelQuaternary}"/>
              </Trigger>
              <Trigger Property="IsKeyboardFocusWithin" Value="True">
                <Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                <Setter TargetName="glow" Property="Opacity" Value="1"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style TargetType="CheckBox">
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="VerticalContentAlignment" Value="Center"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="CheckBox">
            <StackPanel Orientation="Horizontal" Background="Transparent">
              <Border x:Name="box" Width="15" Height="15" CornerRadius="4" VerticalAlignment="Center"
                      Background="{DynamicResource BgField}" BorderBrush="{DynamicResource BorderControl}"
                      BorderThickness="1">
                <Path x:Name="tick" Data="M 0,4 L 3,7 L 8,0" Stroke="{DynamicResource AccentText}"
                      StrokeThickness="1.8" Margin="3" Stretch="Uniform" Visibility="Collapsed"
                      StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
              </Border>
              <ContentPresenter Margin="7,0,0,0" VerticalAlignment="Center"/>
            </StackPanel>
            <ControlTemplate.Triggers>
              <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="box" Property="Background" Value="{StaticResource AccentFill}"/>
                <Setter TargetName="box" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                <Setter TargetName="tick" Property="Visibility" Value="Visible"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.45"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="VeilPwdStyle" TargetType="PasswordBox">
      <Setter Property="FontSize" Value="14"/>
      <Setter Property="Height" Value="36"/>
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Background" Value="{DynamicResource BgField}"/>
      <Setter Property="BorderBrush" Value="{DynamicResource BorderControl}"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Padding" Value="11,0"/>
      <Setter Property="CaretBrush" Value="{DynamicResource Label}"/>
      <Setter Property="PasswordChar" Value="&#x25CF;"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="PasswordBox">
            <Border x:Name="bd" CornerRadius="9" Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
              <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center" Margin="{TemplateBinding Padding}"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsKeyboardFocused" Value="True">
                <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource Accent}"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter TargetName="bd" Property="Opacity" Value="0.45"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="SideItem" TargetType="ListBoxItem">
      <Setter Property="Padding" Value="9,6"/>
      <Setter Property="Margin" Value="6,1"/>
      <Setter Property="Foreground" Value="{DynamicResource LabelSecondary}"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ListBoxItem">
            <Grid>
              <Border x:Name="b" CornerRadius="{StaticResource RRow}" Background="Transparent"
                      BorderBrush="Transparent" BorderThickness="1" Padding="{TemplateBinding Padding}">
                <ContentPresenter/>
              </Border>
              <Rectangle x:Name="bar" Width="3" RadiusX="1.5" RadiusY="1.5" Margin="0,7"
                         HorizontalAlignment="Left" Fill="{StaticResource AccentFill}" Opacity="0"/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource BgHover}"/>
                <Setter Property="Foreground" Value="{DynamicResource Label}"/>
              </Trigger>
              <Trigger Property="IsSelected" Value="True">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource AccentSoft}"/>
                <Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource AccentBorder}"/>
                <Setter TargetName="bar" Property="Opacity" Value="1"/>
                <Setter Property="Foreground" Value="{DynamicResource Label}"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="GridHeader" TargetType="DataGridColumnHeader">
      <Setter Property="Background" Value="{DynamicResource PanelSoft}"/>
      <Setter Property="Foreground" Value="{DynamicResource LabelSecondary}"/>
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Height" Value="28"/>
      <Setter Property="Padding" Value="9,0"/>
      <Setter Property="HorizontalContentAlignment" Value="Left"/>
      <Setter Property="VerticalContentAlignment" Value="Center"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="DataGridColumnHeader">
            <Grid>
              <Border Background="{TemplateBinding Background}" BorderBrush="{DynamicResource SeparatorSoft}"
                      BorderThickness="0,0,1,1" Padding="{TemplateBinding Padding}">
                <ContentPresenter VerticalAlignment="Center"/>
              </Border>
              <Thumb x:Name="PART_RightHeaderGripper" HorizontalAlignment="Right" Width="6" Cursor="SizeWE" Opacity="0"/>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="GridRow" TargetType="DataGridRow">
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Height" Value="29"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="DataGridRow">
            <Border x:Name="row" Background="{TemplateBinding Background}"
                    BorderBrush="Transparent" BorderThickness="3,0,0,0"
                    SnapsToDevicePixels="True">
              <DataGridCellsPresenter ItemsPanel="{TemplateBinding ItemsPanel}"
                                      SnapsToDevicePixels="{TemplateBinding SnapsToDevicePixels}"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsSelected" Value="True">
                <Setter TargetName="row" Property="Background" Value="{DynamicResource SelectionRowBg}"/>
                <Setter TargetName="row" Property="BorderBrush" Value="{StaticResource AccentFill}"/>
                <Setter Property="Foreground" Value="{DynamicResource SelectionRowFg}"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="ItemsControl.AlternationIndex" Value="1">
          <Setter Property="Background" Value="{DynamicResource BgRowAlt}"/>
        </Trigger>
        <Trigger Property="IsMouseOver" Value="True">
          <Setter Property="Background" Value="{DynamicResource BgHover}"/>
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style x:Key="GridCell" TargetType="DataGridCell">
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Padding" Value="9,0"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="DataGridCell">
            <Border Background="Transparent" Padding="{TemplateBinding Padding}">
              <ContentPresenter VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="IsSelected" Value="True">
          <Setter Property="Foreground" Value="{DynamicResource SelectionRowFg}"/>
        </Trigger>
      </Style.Triggers>
    </Style>

    <Style TargetType="ComboBoxItem">
      <Setter Property="Padding" Value="10,5"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ComboBoxItem">
            <Border x:Name="b" CornerRadius="{StaticResource RControl}" Background="Transparent" Padding="{TemplateBinding Padding}">
              <ContentPresenter VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource BgHover}"/>
                <Setter Property="Foreground" Value="{DynamicResource Label}"/>
              </Trigger>
              <Trigger Property="IsSelected" Value="True">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource AccentSoft}"/>
                <Setter Property="Foreground" Value="{DynamicResource Accent}"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style TargetType="ComboBox">
      <Setter Property="Height" Value="28"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ComboBox">
            <Grid>
              <ToggleButton x:Name="tb" Focusable="False" ClickMode="Press"
                            IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                <ToggleButton.Template>
                  <ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="bd" CornerRadius="{StaticResource RField}" Background="{DynamicResource BgField}"
                            BorderBrush="{DynamicResource BorderControl}" BorderThickness="1">
                      <Path HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0"
                            Data="M0,0 L4.5,4.5 L9,0" Stroke="{DynamicResource LabelSecondary}"
                            StrokeThickness="1.4" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                      </Trigger>
                      <Trigger Property="IsChecked" Value="True">
                        <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter Margin="10,0,28,0" VerticalAlignment="Center" IsHitTestVisible="False"
                                Content="{TemplateBinding SelectionBoxItem}"
                                ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/>
              <Popup x:Name="PART_Popup" AllowsTransparency="True" Placement="Bottom" Focusable="False"
                     IsOpen="{TemplateBinding IsDropDownOpen}" PopupAnimation="Fade">
                <Border CornerRadius="{StaticResource RContent}" Background="{DynamicResource BgElevated}" Margin="0,5,0,0"
                        BorderBrush="{DynamicResource Separator}" BorderThickness="1"
                        MinWidth="{TemplateBinding ActualWidth}">
                  <StackPanel IsItemsHost="True" Margin="5"/>
                </Border>
              </Popup>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style TargetType="ContextMenu">
      <Setter Property="HasDropShadow" Value="False"/>
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ContextMenu">
            <Border CornerRadius="{StaticResource RContent}" Background="{DynamicResource BgElevated}"
                    BorderBrush="{DynamicResource Separator}" BorderThickness="1" Padding="5" MinWidth="168">
              <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style TargetType="MenuItem">
      <Setter Property="Padding" Value="10,5"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="MenuItem">
            <Grid>
              <Border x:Name="b" CornerRadius="{StaticResource RControl}" Background="Transparent"
                      Padding="{TemplateBinding Padding}">
                <Grid>
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                  </Grid.ColumnDefinitions>
                  <ContentPresenter ContentSource="Header" VerticalAlignment="Center"/>
                  <Path x:Name="freccia" Grid.Column="1" Visibility="Collapsed" Margin="12,0,0,0"
                        VerticalAlignment="Center" Data="M0,0 L4.5,4.5 L0,9" Stroke="{DynamicResource LabelSecondary}"
                        StrokeThickness="1.4" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                </Grid>
              </Border>
              <Popup x:Name="PART_Popup" AllowsTransparency="True" Placement="Right" Focusable="False"
                     IsOpen="{TemplateBinding IsSubmenuOpen}" PopupAnimation="Fade" HorizontalOffset="4">
                <Border CornerRadius="{StaticResource RContent}" Background="{DynamicResource BgElevated}"
                        BorderBrush="{DynamicResource Separator}" BorderThickness="1" Padding="5" MinWidth="148">
                  <StackPanel IsItemsHost="True"/>
                </Border>
              </Popup>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="Role" Value="SubmenuHeader">
                <Setter TargetName="freccia" Property="Visibility" Value="Visible"/>
              </Trigger>
              <Trigger Property="IsHighlighted" Value="True">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource BgHover}"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Foreground" Value="{DynamicResource LabelQuaternary}"/>
                <Setter Property="Cursor" Value="Arrow"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Separator">
            <Border Height="1" Margin="9,5" Background="{DynamicResource SeparatorSoft}"/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style TargetType="ProgressBar">
      <Setter Property="Height" Value="4"/>
      <Setter Property="Foreground" Value="{StaticResource AccentFill}"/>
      <Setter Property="Background" Value="{DynamicResource BgPressed}"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ProgressBar">
            <Border CornerRadius="2" Background="{TemplateBinding Background}" ClipToBounds="True">
              <Border x:Name="PART_Track" Background="Transparent">
                <Rectangle x:Name="PART_Indicator" HorizontalAlignment="Left" RadiusX="2" RadiusY="2"
                           Fill="{TemplateBinding Foreground}"/>
              </Border>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="ContentCard" TargetType="Border">
      <Setter Property="Background" Value="{DynamicResource Panel}"/>
      <Setter Property="CornerRadius" Value="{StaticResource RContent}"/>
      <Setter Property="BorderBrush" Value="{DynamicResource Separator}"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="ClipToBounds" Value="True"/>
    </Style>

    <Style TargetType="GridSplitter">
      <Setter Property="Background" Value="{DynamicResource Separator}"/>
      <Setter Property="Width" Value="1"/>
      <Setter Property="Cursor" Value="SizeWE"/>
      <Setter Property="Focusable" Value="False"/>
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
          <Setter Property="Background" Value="{StaticResource AccentFill}"/>
        </Trigger>
      </Style.Triggers>
    </Style>
  </Window.Resources>

  <Border x:Name="RootBorder" Background="{DynamicResource BgWindow}"
          BorderBrush="{DynamicResource Separator}" BorderThickness="1" CornerRadius="10">
    <Grid x:Name="RootGrid">

      <Canvas x:Name="DuckLayer" ClipToBounds="True" IsHitTestVisible="False"
              Background="Transparent"/>

      <Grid x:Name="AppGrid">
      <Grid.RowDefinitions>
        <RowDefinition Height="52"/>
        <RowDefinition Height="*"/>
        <RowDefinition Height="27"/>
      </Grid.RowDefinitions>

      <Border Grid.Row="0" Background="{DynamicResource BgToolbarGlass}" CornerRadius="9,9,0,0"
              BorderBrush="{DynamicResource Separator}" BorderThickness="0,0,0,1">
        <Grid Margin="14,0,12,0">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>

          <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,16,0">
            <Button x:Name="BtnClose" Style="{StaticResource TrafficBtn}" Background="{DynamicResource TLClose}" ToolTip="Chiudi">
              <Path Data="M 0,0 L 6,6 M 6,0 L 0,6" Stroke="#5A1416" StrokeThickness="1.2" Stretch="Uniform"
                    StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
            </Button>
            <Button x:Name="BtnMin" Style="{StaticResource TrafficBtn}" Background="{DynamicResource TLMin}" ToolTip="Riduci a icona">
              <Path Data="M 0,3 L 7,3" Stroke="#6B3B00" StrokeThickness="1.2" Stretch="Uniform"
                    StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
            </Button>
            <Button x:Name="BtnZoom" Style="{StaticResource TrafficBtn}" Background="{DynamicResource TLZoom}" ToolTip="Ingrandisci">
              <Path Data="M 0,3 L 7,3 M 3.5,0 L 3.5,6.5" Stroke="#063906" StrokeThickness="1.2" Stretch="Uniform"
                    StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
            </Button>
          </StackPanel>

          <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
            <Grid Width="26" Height="26" VerticalAlignment="Center">
              <Viewbox x:Name="LogoVector" Width="26" Height="26">
                <Canvas Width="101" Height="100">
                  <Path x:Name="LogoDuck" Fill="{StaticResource AccentFill}"
                        Data="F1 M 86,30 A 18,18 0 1 1 50,30 A 18,18 0 1 1 86,30 Z M 84,26.5 L 99,31.5 L 84,37 Z M 56,38 L 80,38 L 76,68 L 54,68 Z M 8,66 A 36,22 0 1 1 80,66 A 36,22 0 1 1 8,66 Z M 14,52 L 2,38 L 26,47 Z"/>
                  <Path Fill="#3A2400" Data="M 74,29 A 3.4,3.4 0 1 1 67.2,29 A 3.4,3.4 0 1 1 74,29 Z"/>
                </Canvas>
              </Viewbox>
              <Image x:Name="LogoImage" Stretch="Uniform" Visibility="Collapsed"
                     RenderOptions.BitmapScalingMode="HighQuality"/>
            </Grid>
            <TextBlock Text="DuckNote" FontSize="13" FontWeight="SemiBold" Margin="9,0,0,0"
                       Foreground="{DynamicResource Label}" VerticalAlignment="Center"/>
          </StackPanel>

          <Border Grid.Column="2" HorizontalAlignment="Center" VerticalAlignment="Center"
                  CornerRadius="{StaticResource RField}" Background="{DynamicResource BgFieldAlt}" Padding="3"
                  BorderBrush="{DynamicResource Separator}" BorderThickness="1"
                  shell:WindowChrome.IsHitTestVisibleInChrome="True">
            <Grid x:Name="SegHost">
              <Border x:Name="SegPill" Width="92" Height="26" HorizontalAlignment="Left"
                      CornerRadius="{StaticResource RControl}" Background="{StaticResource AccentFill}">
                <Border.RenderTransform>
                  <TransformGroup>
                    <ScaleTransform x:Name="SegPillS" CenterX="46" CenterY="13" ScaleX="1" ScaleY="1"/>
                    <TranslateTransform x:Name="SegPillT" X="0"/>
                  </TransformGroup>
                </Border.RenderTransform>
              </Border>
              <StackPanel Orientation="Horizontal">
                <RadioButton x:Name="TabNote" Style="{StaticResource SegBtn}" Content="Note"
                             Width="92" GroupName="view"/>
                <RadioButton x:Name="TabNet"  Style="{StaticResource SegBtn}" Content="Rete"
                             Width="92" GroupName="view"/>
              </StackPanel>
            </Grid>
          </Border>

          <StackPanel Grid.Column="3" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,12,0">
            <StackPanel Margin="0,0,15,0">
              <TextBlock x:Name="StatHosts" Text="0" FontSize="14" FontWeight="SemiBold"
                         HorizontalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <TextBlock Text="host" FontSize="10" HorizontalAlignment="Center" Margin="0,-3,0,0"
                         Foreground="{DynamicResource LabelTertiary}"/>
            </StackPanel>
            <StackPanel Margin="0,0,15,0">
              <TextBlock x:Name="StatUp" Text="0" FontSize="14" FontWeight="SemiBold"
                         HorizontalAlignment="Center" Foreground="{DynamicResource Green}"/>
              <TextBlock Text="attivi" FontSize="10" HorizontalAlignment="Center" Margin="0,-3,0,0"
                         Foreground="{DynamicResource LabelTertiary}"/>
            </StackPanel>
            <StackPanel>
              <TextBlock x:Name="StatDown" Text="0" FontSize="14" FontWeight="SemiBold"
                         HorizontalAlignment="Center" Foreground="{DynamicResource Red}"/>
              <TextBlock Text="spenti" FontSize="10" HorizontalAlignment="Center" Margin="0,-3,0,0"
                         Foreground="{DynamicResource LabelTertiary}"/>
            </StackPanel>
          </StackPanel>

          <Border Grid.Column="4" VerticalAlignment="Center" Margin="0,0,10,0" CornerRadius="11"
                  Background="{DynamicResource PanelSoft}" BorderBrush="{DynamicResource Separator}"
                  BorderThickness="1" Padding="9,3">
            <StackPanel Orientation="Horizontal">
              <Path x:Name="MonGlyph" Margin="0,0,6,0" VerticalAlignment="Center"
                    Width="13" Height="13" Stretch="Uniform" StrokeThickness="1.5"
                    Stroke="{DynamicResource Gray}" StrokeStartLineCap="Round" StrokeEndLineCap="Round"
                    StrokeLineJoin="Round"
                    Data="M14.8,8 A6.8,6.8 0 1 1 1.2,8 A6.8,6.8 0 1 1 14.8,8 M8,3.9 L8,8.3 L11.1,10.1"/>
              <TextBlock x:Name="MonLbl" Text="fermo" FontSize="11" Foreground="{DynamicResource LabelSecondary}"
                         VerticalAlignment="Center"/>
            </StackPanel>
          </Border>

          <StackPanel Grid.Column="5" Orientation="Horizontal" VerticalAlignment="Center">
            <Button x:Name="BtnSidebar" Style="{StaticResource Toolbtn}" ToolTip="Mostra o nascondi la barra laterale">
              <Path Data="M2,3 H18 V15 H2 Z M7.5,3 V15" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                    StrokeThickness="1.3" Width="18" Height="14" Stretch="Uniform"/>
            </Button>
            <Button x:Name="BtnScan" Style="{StaticResource Toolbtn}" ToolTip="Analizza subito gli host della nota (F5)">
              <Path Data="M13.4,10.6 A6,6 0 1 1 12.6,4.6 M15.4,2.2 L12.6,5.2 L9.4,3.8"
                    Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                    StrokeThickness="1.4" Width="16" Height="16" Stretch="Uniform"
                    StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"/>
            </Button>
            <Button x:Name="BtnLock" Style="{StaticResource Toolbtn}" Visibility="Collapsed"
                    ToolTip="Blocca adesso (Ctrl+L)">
              <Canvas Width="14" Height="16">
                <Path Data="M3,7 V4.6 A4,4 0 0 1 11,4.6 V7"
                      Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                      StrokeThickness="1.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                <Rectangle Canvas.Left="1" Canvas.Top="6.8" Width="12" Height="8.4" RadiusX="2" RadiusY="2"
                           Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
              </Canvas>
            </Button>
            <Button x:Name="BtnTheme" Style="{StaticResource Toolbtn}" ToolTip="Aspetto chiaro / scuro">
              <Path x:Name="ThemeGlyph" Data="M 10,2 A 8,8 0 1 0 18,10 A 6,6 0 1 1 10,2 Z"
                    Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" Width="15" Height="15" Stretch="Uniform"/>
            </Button>
            <Button x:Name="BtnPrefs" Style="{StaticResource Toolbtn}" ToolTip="Impostazioni (Ctrl+,)">
              <Canvas Width="16" Height="14">
                <Rectangle Canvas.Left="0" Canvas.Top="1.6"  Width="16" Height="1.2" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                <Rectangle Canvas.Left="0" Canvas.Top="6.4"  Width="16" Height="1.2" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                <Rectangle Canvas.Left="0" Canvas.Top="11.2" Width="16" Height="1.2" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                <Ellipse Canvas.Left="9.5" Canvas.Top="0.2"  Width="4" Height="4" Fill="{DynamicResource BgToolbar}" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="1.2"/>
                <Ellipse Canvas.Left="3"   Canvas.Top="5"    Width="4" Height="4" Fill="{DynamicResource BgToolbar}" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="1.2"/>
                <Ellipse Canvas.Left="8"   Canvas.Top="9.8"  Width="4" Height="4" Fill="{DynamicResource BgToolbar}" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="1.2"/>
              </Canvas>
            </Button>
          </StackPanel>
        </Grid>
      </Border>

      <Grid Grid.Row="1">
        <Grid.ColumnDefinitions>
          <ColumnDefinition x:Name="ColSidebar" Width="232" MinWidth="170"/>
          <ColumnDefinition Width="Auto"/>
          <ColumnDefinition Width="*" MinWidth="420"/>
        </Grid.ColumnDefinitions>

        <Border Grid.Column="0" x:Name="SidebarPanel" Background="{DynamicResource BgSidebarGlass}"
                ClipToBounds="True">
          <Grid x:Name="SideInner" Width="232" HorizontalAlignment="Left">
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="*"/>
              <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>
            <StackPanel Grid.Row="0" Margin="12,10,12,8">
              <Border CornerRadius="{StaticResource RField}" Background="{DynamicResource BgFieldAlt}" Padding="2" Margin="0,0,0,8"
                      BorderBrush="{DynamicResource Separator}" BorderThickness="1">
                <Grid x:Name="SideSegHost">
                  <Border x:Name="SideSegPill" Height="26" HorizontalAlignment="Left"
                          CornerRadius="{StaticResource RControl}" Background="{StaticResource AccentFill}">
                    <Border.RenderTransform>
                      <TransformGroup>
                        <ScaleTransform x:Name="SideSegPillS" CenterY="13" ScaleX="1" ScaleY="1"/>
                        <TranslateTransform x:Name="SideSegPillT" X="0"/>
                      </TransformGroup>
                    </Border.RenderTransform>
                  </Border>
                  <UniformGrid Rows="1" Columns="2">
                    <RadioButton x:Name="SideModeHost" Style="{StaticResource SegBtn}" Content="Host"
                                 IsChecked="True" GroupName="sidemode" MinWidth="0"/>
                    <RadioButton x:Name="SideModeOutline" Style="{StaticResource SegBtn}" Content="Struttura"
                                 GroupName="sidemode" MinWidth="0"/>
                  </UniformGrid>
                </Grid>
              </Border>
              <Grid>
                <TextBox x:Name="SideSearch" Style="{StaticResource Field}" Background="{DynamicResource BgField}"/>
                <TextBlock x:Name="SideSearchHint" Text="Filtra host" IsHitTestVisible="False" Margin="9,0,0,0"
                           VerticalAlignment="Center" FontSize="12" Foreground="{DynamicResource LabelTertiary}"/>
              </Grid>
            </StackPanel>
            <ListBox x:Name="SideList" Grid.Row="1" Background="Transparent" BorderThickness="0"
                     ItemContainerStyle="{StaticResource SideItem}"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                     VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling">
              <ListBox.RenderTransform>
                <TranslateTransform x:Name="SideListT" X="0"/>
              </ListBox.RenderTransform>
              <ListBox.ContextMenu>
                <ContextMenu x:Name="HostMenu">
                  <MenuItem x:Name="HostMenuIgnore" Header="Non analizzare"/>
                  <Separator/>
                  <MenuItem x:Name="HostMenuIgnored" Header="Ignorati"/>
                </ContextMenu>
              </ListBox.ContextMenu>
              <ListBox.ItemTemplate>
                <DataTemplate>
                  <Grid>
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="Auto"/>
                      <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Ellipse Grid.Column="0" Width="7" Height="7" VerticalAlignment="Center" Margin="0,0,8,0"
                             Fill="{Binding Dot}"/>
                    <StackPanel Grid.Column="1">
                      <TextBlock Text="{Binding Title}" FontSize="12" TextTrimming="CharacterEllipsis"/>
                      <TextBlock Text="{Binding Subtitle}" FontSize="10" Opacity="0.62"
                                 TextTrimming="CharacterEllipsis" Visibility="{Binding SubVis}"/>
                    </StackPanel>
                  </Grid>
                </DataTemplate>
              </ListBox.ItemTemplate>
            </ListBox>
            <Border Grid.Row="2" BorderBrush="{DynamicResource Separator}" BorderThickness="0,1,0,0" Padding="12,8">
              <TextBlock x:Name="SideFoot" FontSize="10" Foreground="{DynamicResource LabelTertiary}"
                         Text="Nessun host"/>
            </Border>
          </Grid>
        </Border>

        <GridSplitter Grid.Column="1" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" ResizeBehavior="PreviousAndNext"/>

        <Grid Grid.Column="2" Background="{DynamicResource BgContentGlass}">

          <Rectangle Fill="{StaticResource GridBackdrop}" IsHitTestVisible="False"
                     CacheMode="BitmapCache"/>

          <Grid x:Name="ViewNotes" RenderTransformOrigin="0.5,0.5" Margin="18">
            <Grid.RenderTransform><TranslateTransform x:Name="ViewNotesT"/></Grid.RenderTransform>
            <Border Style="{StaticResource ContentCard}">
            <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <Border Grid.Row="0" Background="Transparent"
                    BorderBrush="{DynamicResource SeparatorSoft}" BorderThickness="0,0,0,1" Padding="12,7">
              <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
              <StackPanel Orientation="Horizontal">
                <Button x:Name="FmtUndo" Style="{StaticResource Toolbtn}" ToolTip="Annulla (Ctrl+Z)">
                  <Path Data="M7,4 L2,8.5 L7,13 M2.5,8.5 H11 A4.5,4.5 0 0 1 11,17.5 H7" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                        StrokeThickness="1.4" Width="16" Height="15" Stretch="Uniform" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                </Button>
                <Button x:Name="FmtRedo" Style="{StaticResource Toolbtn}" ToolTip="Ripristina (Ctrl+Y)">
                  <Path Data="M13,4 L18,8.5 L13,13 M17.5,8.5 H9 A4.5,4.5 0 0 0 9,17.5 H13" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                        StrokeThickness="1.4" Width="16" Height="15" Stretch="Uniform" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                </Button>
                <Rectangle Width="1" Height="18" Fill="{DynamicResource Separator}" Margin="7,0"/>
                <Button x:Name="FmtH1" Style="{StaticResource Toolbtn}" Content="H1" FontWeight="Bold" FontSize="12" ToolTip="Titolo"/>
                <Button x:Name="FmtH2" Style="{StaticResource Toolbtn}" Content="H2" FontWeight="Bold" FontSize="11" ToolTip="Sottotitolo"/>
                <Button x:Name="FmtH3" Style="{StaticResource Toolbtn}" Content="H3" FontWeight="Bold" FontSize="10" ToolTip="Intestazione"/>
                <Rectangle Width="1" Height="18" Fill="{DynamicResource Separator}" Margin="7,0"/>
                <Button x:Name="FmtBold"   Style="{StaticResource Toolbtn}" Content="B" FontWeight="Bold" ToolTip="Grassetto (Ctrl+B)"/>
                <Button x:Name="FmtItalic" Style="{StaticResource Toolbtn}" Content="I" FontStyle="Italic" ToolTip="Corsivo (Ctrl+I)"/>
                <Button x:Name="FmtUnder"  Style="{StaticResource Toolbtn}" Content="U" ToolTip="Sottolineato (Ctrl+U)"/>
                <Button x:Name="FmtStrike" Style="{StaticResource Toolbtn}" Content="S" ToolTip="Barrato (~~testo~~)">
                  <Button.ContentTemplate>
                    <DataTemplate>
                      <Grid>
                        <TextBlock Text="S" FontSize="13"/>
                        <Rectangle Height="1.2" VerticalAlignment="Center" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                      </Grid>
                    </DataTemplate>
                  </Button.ContentTemplate>
                </Button>
                <Button x:Name="FmtMark" Style="{StaticResource Toolbtn}" ToolTip="Evidenzia (==testo==)">
                  <Border Background="{DynamicResource Highlight}" CornerRadius="2" Padding="4,1">
                    <TextBlock Text="A" FontSize="11" FontWeight="SemiBold" Foreground="{DynamicResource HighlightFg}"/>
                  </Border>
                </Button>
                <Button x:Name="FmtCode"   Style="{StaticResource Toolbtn}" Content="&lt;/&gt;" FontSize="11" ToolTip="Codice inline"/>
                <Button x:Name="FmtCodeBlock" Style="{StaticResource Toolbtn}" ToolTip="Blocco di codice">
                  <Path Data="M6,3 L1,9 L6,15 M12,3 L17,9 L12,15" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                        StrokeThickness="1.4" Width="16" Height="14" Stretch="Uniform" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                </Button>
                <Rectangle Width="1" Height="18" Fill="{DynamicResource Separator}" Margin="7,0"/>
                <Button x:Name="FmtBullet" Style="{StaticResource Toolbtn}" ToolTip="Elenco puntato (Ctrl+Shift+8)">
                  <Canvas Width="17" Height="13">
                    <Ellipse Canvas.Left="0" Canvas.Top="1"   Width="3" Height="3" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <Ellipse Canvas.Left="0" Canvas.Top="5.5" Width="3" Height="3" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <Ellipse Canvas.Left="0" Canvas.Top="10"  Width="3" Height="3" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <Rectangle Canvas.Left="6" Canvas.Top="1.8"  Width="11" Height="1.4" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <Rectangle Canvas.Left="6" Canvas.Top="6.3"  Width="11" Height="1.4" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <Rectangle Canvas.Left="6" Canvas.Top="10.8" Width="11" Height="1.4" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                  </Canvas>
                </Button>
                <Button x:Name="FmtNumber" Style="{StaticResource Toolbtn}" ToolTip="Elenco numerato (Ctrl+Shift+7)">
                  <Canvas Width="17" Height="13">
                    <TextBlock Canvas.Left="0" Canvas.Top="-1"  FontSize="7" Text="1" Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <TextBlock Canvas.Left="0" Canvas.Top="3.4" FontSize="7" Text="2" Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <TextBlock Canvas.Left="0" Canvas.Top="7.8" FontSize="7" Text="3" Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <Rectangle Canvas.Left="6" Canvas.Top="1.8"  Width="11" Height="1.4" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <Rectangle Canvas.Left="6" Canvas.Top="6.3"  Width="11" Height="1.4" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                    <Rectangle Canvas.Left="6" Canvas.Top="10.8" Width="11" Height="1.4" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                  </Canvas>
                </Button>
                <Button x:Name="FmtTodo" Style="{StaticResource Toolbtn}" ToolTip="Casella di controllo (- [ ])">
                  <Canvas Width="17" Height="13">
                    <Rectangle Canvas.Left="0" Canvas.Top="1" Width="9" Height="9" RadiusX="2" RadiusY="2"
                               Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="1.2"/>
                    <Path Data="M2,5.5 L4,8 L7.5,3" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="1.4"
                          StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                    <Rectangle Canvas.Left="12" Canvas.Top="5" Width="5" Height="1.4" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                  </Canvas>
                </Button>
                <Button x:Name="FmtQuote" Style="{StaticResource Toolbtn}" Content="&#8220;" FontSize="17" ToolTip="Citazione"/>
                <Button x:Name="FmtRule" Style="{StaticResource Toolbtn}" ToolTip="Riga orizzontale">
                  <Rectangle Width="16" Height="1.6" Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"/>
                </Button>
                <Rectangle Width="1" Height="18" Fill="{DynamicResource Separator}" Margin="7,0"/>
                <Button x:Name="TblNew" Style="{StaticResource Toolbtn}" ToolTip="Inserisci tabella">
                  <Path Data="M1,2 H17 V16 H1 Z M1,6.6 H17 M1,11.3 H17 M6.3,2 V16 M11.6,2 V16"
                        Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                        StrokeThickness="1.2" Width="16" Height="14" Stretch="Uniform"/>
                </Button>
                <Button x:Name="TblRowAdd" Style="{StaticResource Toolbtn}" Content="+ riga"  FontSize="11" ToolTip="Aggiungi riga sotto"/>
                <Button x:Name="TblColAdd" Style="{StaticResource Toolbtn}" Content="+ col"   FontSize="11" ToolTip="Aggiungi colonna a destra"/>
                <Button x:Name="TblRowDel" Style="{StaticResource Toolbtn}" Content="- riga"  FontSize="11" ToolTip="Elimina riga corrente"/>
                <Button x:Name="TblColDel" Style="{StaticResource Toolbtn}" Content="- col"   FontSize="11" ToolTip="Elimina colonna corrente"/>
                <Rectangle Width="1" Height="18" Fill="{DynamicResource Separator}" Margin="7,0"/>
                <Button x:Name="FmtLink" Style="{StaticResource Toolbtn}" ToolTip="Inserisci collegamento">
                  <Path Data="M7.5,11.5 A3.5,3.5 0 0 1 7.5,6.5 L10,4 A3.5,3.5 0 0 1 15,9 L13.8,10.2 M10.5,6.5 A3.5,3.5 0 0 1 10.5,11.5 L8,14 A3.5,3.5 0 0 1 3,9 L4.2,7.8"
                        Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="1.3" Width="17" Height="15" Stretch="Uniform"
                        StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                </Button>
                <Button x:Name="FmtFind" Style="{StaticResource Toolbtn}" ToolTip="Trova e sostituisci (Ctrl+F)">
                  <Path Data="M8,14 A6,6 0 1 1 8,2 A6,6 0 1 1 8,14 M12.4,12.4 L17,17"
                        Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="1.4" Width="16" Height="16" Stretch="Uniform"
                        StrokeStartLineCap="Round"/>
                </Button>
                <Button x:Name="FmtClear" Style="{StaticResource Toolbtn}" Content="Aa" FontSize="11" ToolTip="Rimuovi formattazione"/>
              </StackPanel>
              </ScrollViewer>
            </Border>

            <Border x:Name="FindBar" Grid.Row="1" Visibility="Collapsed" Padding="12,0" Opacity="0"
                    MaxHeight="0" Background="{DynamicResource BgFieldAlt}"
                    BorderBrush="{DynamicResource SeparatorSoft}" BorderThickness="0,0,0,1"
                    RenderTransformOrigin="0.5,0">
              <Border.RenderTransform>
                <TransformGroup>
                  <ScaleTransform x:Name="FindBarS" ScaleY="1"/>
                  <TranslateTransform x:Name="FindBarT" Y="-16"/>
                </TransformGroup>
              </Border.RenderTransform>
              <Grid Height="46">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="*" MinWidth="120"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="*" MinWidth="120"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <Grid Grid.Column="0" Margin="0,0,8,0" VerticalAlignment="Center">
                  <TextBox x:Name="FindBox" Style="{StaticResource Field}"/>
                  <TextBlock x:Name="FindHint" Text="Trova" IsHitTestVisible="False" Margin="11,0,0,0"
                             VerticalAlignment="Center" FontSize="12" Foreground="{DynamicResource LabelTertiary}"/>
                </Grid>

                <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                  <Button x:Name="FindPrev" Style="{StaticResource Toolbtn}" Content="&#8593;" ToolTip="Precedente (Shift+F3)"/>
                  <Button x:Name="FindNext" Style="{StaticResource Toolbtn}" Content="&#8595;" ToolTip="Successivo (F3)"/>
                </StackPanel>

                <TextBlock x:Name="FindCount" Grid.Column="2" VerticalAlignment="Center" FontSize="11"
                           Margin="8,0,12,0" MinWidth="74" TextAlignment="Right"
                           Foreground="{DynamicResource LabelSecondary}"/>

                <Rectangle Grid.Column="3" Width="1" Height="20" Margin="0,0,12,0"
                           Fill="{DynamicResource Separator}" VerticalAlignment="Center"/>

                <Grid Grid.Column="4" Margin="0,0,8,0" VerticalAlignment="Center">
                  <TextBox x:Name="ReplBox" Style="{StaticResource Field}"/>
                  <TextBlock x:Name="ReplHint" Text="Sostituisci con" IsHitTestVisible="False" Margin="11,0,0,0"
                             VerticalAlignment="Center" FontSize="12" Foreground="{DynamicResource LabelTertiary}"/>
                </Grid>

                <StackPanel Grid.Column="5" Orientation="Horizontal" VerticalAlignment="Center">
                  <Button x:Name="ReplOne" Style="{StaticResource QuietBtn}" Content="Sostituisci"/>
                  <Button x:Name="ReplAll" Style="{StaticResource QuietBtn}" Content="Tutte" Margin="8,0,0,0"/>
                  <CheckBox x:Name="FindCase" Content="Aa" VerticalAlignment="Center" Margin="14,0,0,0"
                            ToolTip="Distingui maiuscole e minuscole"/>
                </StackPanel>

                <Button x:Name="FindClose" Grid.Column="6" Style="{StaticResource Toolbtn}" Content="&#10005;"
                        FontSize="11" Margin="12,0,0,0" VerticalAlignment="Center" ToolTip="Chiudi (Esc)"/>
              </Grid>
            </Border>

            <RichTextBox x:Name="Editor" Grid.Row="2"
                         AcceptsReturn="True" AcceptsTab="True"
                         VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
                         BorderThickness="0" Padding="34,24,34,40"
                         Background="Transparent"
                         Foreground="{DynamicResource Label}"
                         CaretBrush="{DynamicResource Label}"
                         SelectionBrush="{DynamicResource SelectionBg}" SelectionOpacity="0.55"
                         SpellCheck.IsEnabled="False" IsUndoEnabled="True" UndoLimit="300">
              <RichTextBox.ContextMenu>
                <ContextMenu x:Name="EdMenu">
                  <MenuItem x:Name="EdMenuWatch" Header="Pinga"/>
                  <MenuItem x:Name="EdMenuPing" Header="Analizza adesso"/>
                  <MenuItem x:Name="EdMenuCopyHost" Header="Copia indirizzo"/>
                  <Separator x:Name="EdMenuSep"/>
                  <MenuItem x:Name="EdMenuCut" Header="Taglia"/>
                  <MenuItem x:Name="EdMenuCopy" Header="Copia"/>
                  <MenuItem x:Name="EdMenuPaste" Header="Incolla"/>
                  <MenuItem x:Name="EdMenuPasteRaw" Header="Incolla senza formato"/>
                  <Separator/>
                  <MenuItem x:Name="EdMenuAll" Header="Seleziona tutto"/>
                </ContextMenu>
              </RichTextBox.ContextMenu>
            </RichTextBox>
            </Grid>
            </Border>
            <Border CornerRadius="{StaticResource RContent}" BorderThickness="1"
                    BorderBrush="{StaticResource EdgeSheen}" IsHitTestVisible="False"/>
          </Grid>

          <Grid x:Name="ViewNet" Visibility="Collapsed" RenderTransformOrigin="0.5,0.5" Margin="18">
            <Grid.RenderTransform><TranslateTransform x:Name="ViewNetT"/></Grid.RenderTransform>
            <Border Style="{StaticResource ContentCard}">
            <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <Border Grid.Row="0" BorderBrush="{DynamicResource SeparatorSoft}" BorderThickness="0,0,0,1" Padding="14,10">
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition x:Name="ColFilterSlot" Width="*" MinWidth="120"/>
                </Grid.ColumnDefinitions>

                <Grid Grid.Column="0" Width="196" Margin="0,0,8,0">
                  <TextBox x:Name="RangeBox" Style="{StaticResource Field}"/>
                  <TextBlock x:Name="RangeHint" Text="192.168.1.0/24  oppure  .1-254"
                             IsHitTestVisible="False" Margin="11,0,0,0" VerticalAlignment="Center"
                             FontSize="12" Foreground="{DynamicResource LabelTertiary}"/>
                </Grid>

                <StackPanel Grid.Column="1" Orientation="Horizontal">
                  <Button x:Name="BtnScanRange" Style="{StaticResource PrimaryBtn}" Content="Analizza"/>
                  <Button x:Name="BtnScanNote"  Style="{StaticResource QuietBtn}" Content="Host della nota" Margin="8,0,0,0"/>
                  <Button x:Name="BtnStop"      Style="{StaticResource QuietBtn}" Content="Ferma" Margin="8,0,0,0" IsEnabled="False"/>
                </StackPanel>

                <Rectangle Grid.Column="2" Width="1" Height="20" Fill="{DynamicResource Separator}" Margin="12,0"/>

                <StackPanel Grid.Column="3" Orientation="Horizontal">
                  <Button x:Name="BtnExport" Style="{StaticResource Toolbtn}" ToolTip="Esporta CSV">
                    <Path Data="M9,2 V10.5 M5.5,7 L9,10.5 L12.5,7 M3,12.5 V15 H15 V12.5"
                          Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                          StrokeThickness="1.4" Width="16" Height="15" Stretch="Uniform"
                          StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                  </Button>
                  <Button x:Name="BtnToNote" Style="{StaticResource Toolbtn}" ToolTip="Invia alla nota">
                    <Path Data="M4,2.5 H10 L13.5,6 V15.5 H4 Z M10,2.5 V6 H13.5 M6.5,9.5 H11 M6.5,12.5 H11"
                          Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                          StrokeThickness="1.4" Width="15" Height="16" Stretch="Uniform"
                          StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                  </Button>
                  <Button x:Name="BtnInspect" Style="{StaticResource Toolbtn}" ToolTip="Dettagli dell'host (finestra a parte)">
                    <Path x:Name="GlyphInspect" Data="M2,3 H16 V15 H2 Z M10.5,3 V15"
                          Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
                          StrokeThickness="1.4" Width="17" Height="14" Stretch="Uniform"/>
                  </Button>
                </StackPanel>

                <Grid Grid.Column="4" ClipToBounds="True" Margin="12,0,0,0">
                  <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                    <ComboBox x:Name="CmbStatus" Width="126" VerticalAlignment="Center" Margin="0,0,10,0"
                              ToolTip="Restringe la tabella a un insieme di host">
                      <ComboBoxItem Content="Tutti gli host" Tag="tutti" IsSelected="True"/>
                      <ComboBoxItem Content="Solo attivi"    Tag="attivi"/>
                      <ComboBoxItem Content="Solo spenti"    Tag="spenti"/>
                      <ComboBoxItem Content="Con porte"      Tag="porte"/>
                      <ComboBoxItem Content="Con rilievi"    Tag="rilievi"/>
                    </ComboBox>
                    <Grid x:Name="FilterWrap" Width="0" ClipToBounds="True" VerticalAlignment="Center">
                      <Grid x:Name="FilterField" Width="212" HorizontalAlignment="Right" Margin="0,0,6,0">
                        <TextBox x:Name="GridFilter" Style="{StaticResource Field}"/>
                        <TextBlock x:Name="GridFilterHint" Text="Cerca tra i risultati" IsHitTestVisible="False"
                                   Margin="11,0,0,0" VerticalAlignment="Center" FontSize="12"
                                   Foreground="{DynamicResource LabelTertiary}"/>
                      </Grid>
                    </Grid>
                    <Button x:Name="BtnFilterOpen" Style="{StaticResource Toolbtn}"
                            ToolTip="Cerca tra i risultati — testo libero oppure per campo: ip: nome: mac: porta: os: tipo: produttore: gruppo: web: nota:">
                      <Path x:Name="GlyphFilter"
                            Data="M8,14 A6,6 0 1 1 8,2 A6,6 0 1 1 8,14 M12.4,12.4 L17,17"
                            Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="1.5" Width="16" Height="16"
                            Stretch="Uniform" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                    </Button>
                  </StackPanel>
                </Grid>
              </Grid>
            </Border>

            <Grid Grid.Row="1">
              <DataGrid x:Name="NetGrid"
                        AutoGenerateColumns="False" IsReadOnly="True" CanUserAddRows="False"
                        HeadersVisibility="Column" GridLinesVisibility="Horizontal"
                        SelectionMode="Extended" SelectionUnit="FullRow"
                        AlternationCount="2" EnableRowVirtualization="True" EnableColumnVirtualization="False"
                        VirtualizingPanel.VirtualizationMode="Recycling"
                        Background="Transparent" BorderThickness="0"
                        HorizontalGridLinesBrush="{DynamicResource SeparatorSoft}"
                        RowStyle="{StaticResource GridRow}" CellStyle="{StaticResource GridCell}"
                        ColumnHeaderStyle="{StaticResource GridHeader}"
                        ScrollViewer.CanContentScroll="True">
                <DataGrid.Columns>
                  <DataGridTemplateColumn Header="" Width="30" SortMemberPath="StatusRank" CanUserResize="False">
                    <DataGridTemplateColumn.CellTemplate>
                      <DataTemplate>
                        <Ellipse Width="8" Height="8" HorizontalAlignment="Center" VerticalAlignment="Center"
                                 Fill="{Binding DotColor}"/>
                      </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                  </DataGridTemplateColumn>
                  <DataGridTextColumn Header="Indirizzo IP" Binding="{Binding IP}" SortMemberPath="SortKey" Width="116"/>
                  <DataGridTextColumn Header="Nome host"   Binding="{Binding Hostname}" Width="1.2*" MinWidth="120"/>
                  <DataGridTextColumn Header="MAC"         Binding="{Binding Mac}" Width="124"/>
                  <DataGridTextColumn Header="Produttore"  Binding="{Binding Vendor}" Width="1*" MinWidth="90"/>
                  <DataGridTextColumn Header="Risposta"    Binding="{Binding RttMs}" SortMemberPath="RttAvg" Width="70"/>
                  <DataGridTextColumn Header="Sistema"     Binding="{Binding OsGuess}" Width="1.1*" MinWidth="100"/>
                  <DataGridTextColumn Header="Porte aperte" Binding="{Binding OpenPorts}" SortMemberPath="PortCount" Width="1.6*" MinWidth="140"/>
                </DataGrid.Columns>
              </DataGrid>
            </Grid>
            </Grid>
            </Border>
            <Border CornerRadius="{StaticResource RContent}" BorderThickness="1"
                    BorderBrush="{StaticResource EdgeSheen}" IsHitTestVisible="False"/>
          </Grid>
        </Grid>
      </Grid>

      <Border Grid.Row="2" Background="{DynamicResource BgToolbarGlass}" CornerRadius="0,0,9,9"
              BorderBrush="{DynamicResource Separator}" BorderThickness="0,1,0,0">
        <Grid Margin="14,0,14,0">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <TextBlock x:Name="StatusText" Grid.Column="0" VerticalAlignment="Center" FontSize="11"
                     Foreground="{DynamicResource LabelSecondary}" TextTrimming="CharacterEllipsis" Text="Pronto"/>
          <ProgressBar x:Name="ScanProgress" Grid.Column="1" Width="150" VerticalAlignment="Center"
                       Margin="12,0" Visibility="Collapsed" Minimum="0" Maximum="100"/>
          <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
            <TextBlock x:Name="WordCount" VerticalAlignment="Center" FontSize="11" Margin="0,0,14,0"
                       Foreground="{DynamicResource LabelTertiary}" Text=""/>
            <TextBlock x:Name="ZoomText" VerticalAlignment="Center" FontSize="11" Margin="0,0,14,0"
                       Foreground="{DynamicResource LabelTertiary}" Text="100%"
                       ToolTip="Ctrl + rotellina per ingrandire, Ctrl+0 per reimpostare"/>
            <TextBlock x:Name="CountText" VerticalAlignment="Center" FontSize="11"
                       Foreground="{DynamicResource LabelTertiary}" Text=""/>
          </StackPanel>
        </Grid>
      </Border>
      </Grid>

      <Border x:Name="LockVeil" Panel.ZIndex="99" Visibility="Collapsed" CornerRadius="9"
              Background="{DynamicResource BgSidebar}">
        <Grid>
          <Grid.RenderTransform><TranslateTransform x:Name="VeilShake"/></Grid.RenderTransform>
          <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" Width="360">

            <StackPanel x:Name="VeilLoader">
              <StackPanel.RenderTransform>
                <TranslateTransform x:Name="VeilLoaderT"/>
              </StackPanel.RenderTransform>

            <Grid Height="132">
              <Path x:Name="VeilRing1" Width="116" Height="116" HorizontalAlignment="Center" VerticalAlignment="Center"
                    Data="M 58,6 A 52,52 0 1 1 6,58" Stroke="{DynamicResource Accent}" StrokeThickness="3"
                    StrokeStartLineCap="Round" StrokeEndLineCap="Round" RenderTransformOrigin="0.5,0.5" Opacity="0.5">
                <Path.RenderTransform><RotateTransform x:Name="VeilRot1"/></Path.RenderTransform>
              </Path>
              <Path x:Name="VeilRing2" Width="92" Height="92" HorizontalAlignment="Center" VerticalAlignment="Center"
                    Data="M 46,6 A 40,40 0 0 1 86,46" Stroke="{DynamicResource Blue}" StrokeThickness="3"
                    StrokeStartLineCap="Round" StrokeEndLineCap="Round" RenderTransformOrigin="0.5,0.5" Opacity="0.45">
                <Path.RenderTransform><RotateTransform x:Name="VeilRot2"/></Path.RenderTransform>
              </Path>
              <Ellipse x:Name="VeilSeal" Width="116" Height="116" HorizontalAlignment="Center" VerticalAlignment="Center"
                       Stroke="{DynamicResource Green}" StrokeThickness="3" Opacity="0" RenderTransformOrigin="0.5,0.5">
                <Ellipse.RenderTransform><ScaleTransform x:Name="VeilSealS" ScaleX="0.6" ScaleY="0.6"/></Ellipse.RenderTransform>
              </Ellipse>
              <Grid Width="58" Height="58" HorizontalAlignment="Center" VerticalAlignment="Center"
                    RenderTransformOrigin="0.5,0.5">
                <Grid.RenderTransform>
                  <TransformGroup>
                    <ScaleTransform x:Name="VeilDuckS" ScaleX="1" ScaleY="1"/>
                    <TranslateTransform x:Name="VeilDuckT"/>
                  </TransformGroup>
                </Grid.RenderTransform>
                <Image x:Name="VeilImg" Stretch="Uniform" Visibility="Collapsed"
                       RenderOptions.BitmapScalingMode="HighQuality"/>
                <Viewbox x:Name="VeilVec" Stretch="Uniform">
                  <Canvas Width="99" Height="100">
                    <Path x:Name="VeilDuck" Fill="{DynamicResource Accent}"/>
                  </Canvas>
                </Viewbox>
              </Grid>
            </Grid>

            <TextBlock x:Name="VeilWork" FontSize="12" Margin="0,14,0,0" LineHeight="17"
                       HorizontalAlignment="Center" TextWrapping="Wrap" TextAlignment="Center"
                       Foreground="{DynamicResource LabelSecondary}" Visibility="Collapsed"/>
            </StackPanel>

            <StackPanel x:Name="VeilBody">
              <StackPanel.RenderTransform>
                <TranslateTransform x:Name="VeilBodyT"/>
              </StackPanel.RenderTransform>

            <TextBlock x:Name="VeilTitle" Text="La nota e' chiusa" FontSize="19" FontWeight="SemiBold" Margin="0,8,0,0"
                       HorizontalAlignment="Center" Foreground="{DynamicResource Label}"/>
            <TextBlock x:Name="VeilSub" Text="Inserisci la password per riaprirla." FontSize="12.5"
                       Margin="0,8,0,0" HorizontalAlignment="Center" TextWrapping="Wrap" TextAlignment="Center"
                       Foreground="{DynamicResource Label}" Opacity="0.78"/>

            <Border x:Name="VeilCard" CornerRadius="12" Background="{DynamicResource SheetScrim}" Margin="0,18,0,0"
                    Padding="15,13" BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
              <StackPanel x:Name="VeilForm">
                <TextBlock Text="Password" FontSize="11" FontWeight="SemiBold" Margin="2,0,0,5"
                           Foreground="{DynamicResource LabelSecondary}"/>
                <PasswordBox x:Name="VeilPwd" Style="{StaticResource VeilPwdStyle}"/>
                <Button x:Name="VeilGo" Style="{StaticResource PrimaryBtn}" Content="Sblocca"
                        HorizontalAlignment="Right" Margin="0,12,0,0" Height="32" Padding="22,0"/>
              </StackPanel>
            </Border>

            <TextBlock x:Name="VeilMsg" FontSize="12.5" FontWeight="SemiBold" Margin="0,13,0,0"
                       HorizontalAlignment="Center" TextWrapping="Wrap" TextAlignment="Center"
                       Foreground="{DynamicResource Red}" Visibility="Collapsed"/>
            </StackPanel>
          </StackPanel>
        </Grid>
      </Border>
    </Grid>
  </Border>
</Window>
'@

$script:Editor        = $null
$script:IsFormatting  = $false
$script:SkipFormat    = $false
$script:LastPara      = $null
$script:DirtyParas    = New-Object 'System.Collections.Generic.HashSet[object]'
$script:DOT           = [string][char]0x25CF

$script:BrText   = New-Object System.Windows.Media.SolidColorBrush
$script:BrSec    = New-Object System.Windows.Media.SolidColorBrush
$script:BrTer    = New-Object System.Windows.Media.SolidColorBrush
$script:BrCodeBg = New-Object System.Windows.Media.SolidColorBrush
$script:BrCodeFg = New-Object System.Windows.Media.SolidColorBrush
$script:BrLink   = New-Object System.Windows.Media.SolidColorBrush
$script:BrUp     = New-Object System.Windows.Media.SolidColorBrush
$script:BrDown   = New-Object System.Windows.Media.SolidColorBrush
$script:BrUnkn   = New-Object System.Windows.Media.SolidColorBrush
$script:BrTblLn  = New-Object System.Windows.Media.SolidColorBrush
$script:BrTblHd  = New-Object System.Windows.Media.SolidColorBrush
$script:BrMarkBg = New-Object System.Windows.Media.SolidColorBrush
$script:BrMarkFg = New-Object System.Windows.Media.SolidColorBrush
$script:BrDone   = New-Object System.Windows.Media.SolidColorBrush

function ConvertFrom-Hex {
    param([string]$Hex)
    if ($Hex.StartsWith('#')) { $Hex = $Hex.Substring(1) }
    $a = 255
    if ($Hex.Length -eq 8) {
        $a   = [Convert]::ToInt32($Hex.Substring(0,2),16)
        $Hex = $Hex.Substring(2)
    }
    [System.Windows.Media.Color]::FromArgb(
        $a,
        [Convert]::ToInt32($Hex.Substring(0,2),16),
        [Convert]::ToInt32($Hex.Substring(2,2),16),
        [Convert]::ToInt32($Hex.Substring(4,2),16))
}

function Sync-EditorBrushes {
    $t = $script:Tokens[$script:Settings.Theme]
    $script:BrText.Color   = ConvertFrom-Hex $t.Label
    $script:BrSec.Color    = ConvertFrom-Hex $t.LabelSecondary
    $script:BrTer.Color    = ConvertFrom-Hex $t.LabelTertiary
    $script:BrCodeBg.Color = ConvertFrom-Hex $t.CodeBg
    $script:BrCodeFg.Color = ConvertFrom-Hex $t.CodeFg
    $script:BrLink.Color   = ConvertFrom-Hex $t.Blue
    $script:BrUp.Color     = ConvertFrom-Hex $t.Green
    $script:BrDown.Color   = ConvertFrom-Hex $t.Red
    $script:BrUnkn.Color   = ConvertFrom-Hex $t.LabelQuaternary
    $script:BrTblLn.Color  = ConvertFrom-Hex $t.TableBorder
    $script:BrTblHd.Color  = ConvertFrom-Hex $t.TableHeaderBg
    $script:BrMarkBg.Color = ConvertFrom-Hex $t.Highlight
    $script:BrMarkFg.Color = ConvertFrom-Hex $t.HighlightFg
    $script:BrDone.Color   = ConvertFrom-Hex $t.LabelTertiary
}

$script:RxOct  = '(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)'
$script:RxIpv4 = [regex]("(?<![\w.])(?:$($script:RxOct)\.){3}$($script:RxOct)(?:/\d{1,2})?(?!\w)(?!\.\d)")

$script:RxIpv6 = [regex](
    '(?<![0-9A-Fa-f:.])(?:' +
    '(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}' +
    '|(?:[0-9A-Fa-f]{1,4}:){1,7}:' +
    '|(?:[0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4}' +
    '|(?:[0-9A-Fa-f]{1,4}:){1,5}(?::[0-9A-Fa-f]{1,4}){1,2}' +
    '|(?:[0-9A-Fa-f]{1,4}:){1,4}(?::[0-9A-Fa-f]{1,4}){1,3}' +
    '|(?:[0-9A-Fa-f]{1,4}:){1,3}(?::[0-9A-Fa-f]{1,4}){1,4}' +
    '|(?:[0-9A-Fa-f]{1,4}:){1,2}(?::[0-9A-Fa-f]{1,4}){1,5}' +
    '|[0-9A-Fa-f]{1,4}:(?::[0-9A-Fa-f]{1,4}){1,6}' +
    '|:(?::[0-9A-Fa-f]{1,4}){1,7}' +
    "|(?:[0-9A-Fa-f]{1,4}:){1,6}:(?:$($script:RxOct)\.){3}$($script:RxOct)" +
    "|::(?:[Ff]{4}(?::0{1,4})?:)?(?:$($script:RxOct)\.){3}$($script:RxOct)" +
    ')(?:%[A-Za-z0-9_.-]+)?(?![0-9A-Fa-f:])(?!\.\d)'
)

$script:RxFqdn = [regex](
    '(?<![\w.@-])(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?![\w-])'
)

$script:NonTld = @(
    'md','txt','rtf','exe','dll','msi','png','jpg','jpeg','gif','svg','webp','ico','bmp','tiff'
    'css','js','jsx','ts','tsx','py','rb','go','rs','java','cs','cpp','hpp','sh','bash','zsh'
    'bat','cmd','xml','json','yaml','yml','toml','ini','cfg','conf','log','lock','env'
    'bak','tmp','swp','old','orig','csv','tsv','pdf','doc','docx','xls','xlsx','ppt','pptx'
    'zip','tar','gz','bz','xz','rar','iso','img','bin','dat','db','sqlite','sql','html','htm'
    'xaml','razor','vue','svelte','mp','wav','flac','ogg','ttf','otf','woff','eot'
)

function Test-Fqdn {
    param([string]$Name)
    if ($Name.Length -gt 253) { return $false }
    $tld = $Name.Substring($Name.LastIndexOf('.') + 1)
    if ($script:NonTld -contains $tld.ToLowerInvariant()) { return $false }
    return ($tld -ceq $tld.ToLowerInvariant() -or $tld -ceq $tld.ToUpperInvariant())
}

function Find-HostTokens {
    param([string]$Text)
    $out = New-Object System.Collections.Generic.List[object]
    if ([string]::IsNullOrWhiteSpace($Text)) { return ,$out }
    $presi = New-Object System.Collections.Generic.List[object]

    foreach ($rx in @($script:RxIpv6, $script:RxIpv4, $script:RxFqdn)) {
        $isFqdn = [object]::ReferenceEquals($rx, $script:RxFqdn)
        foreach ($m in $rx.Matches($Text)) {
            if ($isFqdn -and -not (Test-Fqdn $m.Value)) { continue }
            $fine = $m.Index + $m.Length
            $urta = $false
            foreach ($s in $presi) { if ($m.Index -lt $s.Fine -and $fine -gt $s.Inizio) { $urta = $true; break } }
            if ($urta) { continue }
            [void]$presi.Add([pscustomobject]@{ Inizio = $m.Index; Fine = $fine })
            [void]$out.Add([pscustomobject]@{ Start = $m.Index; Length = $m.Length; Value = $m.Value })
        }
    }
    return ,(@($out | Sort-Object Start))
}

function Parse-Line {
    param([string]$Line)
    if ([string]::IsNullOrWhiteSpace($Line)) { return @{ Type = 'empty' } }
    $work = $Line.TrimStart()
    if ($work.StartsWith($script:DOT)) { $work = $work.Substring(1).TrimStart() }
    if ($work.StartsWith(';'))         { return @{ Type = 'comment'; RawText = $Line.TrimStart() } }
    if ($work -match '^(>+)\s?(.*)$')  { return @{ Type = 'quote'; Marker = $Matches[1]; Body = $Matches[2] } }
    if ($work -match '^(-{3,}|_{3,}|\*{3,})\s*$') { return @{ Type = 'rule'; RawText = $work } }
    if ($work -match '^(```|~~~)(.*)$') { return @{ Type = 'fence'; RawText = $work; Lang = $Matches[2].Trim() } }
    if ($work -match '^[-*+]\s+\[([ xX])\]\s?(.*)$') {
        return @{ Type = 'todo'; Done = ($Matches[1] -ne ' '); Body = $Matches[2]; Indent = ($Line.Length - $Line.TrimStart().Length) }
    }
    return @{ Type = 'text'; RawText = $Line }
}

function Get-HostBrush {
    param([string]$Name)
    if ($script:HostStates.ContainsKey($Name)) {
        if ($script:HostStates[$Name]) { return $script:BrUp } else { return $script:BrDown }
    }
    return $script:BrUnkn
}

$script:MdRegex = [regex]'(\*\*[^\*\r\n]+?\*\*)|(__[^_\r\n]+?__)|(~~[^~\r\n]+?~~)|(==[^=\r\n]+?==)|(\*[^\*\r\n]+?\*)|(_[^_\r\n]+?_)|(`[^`\r\n]+?`)|((?:https?://|www\.)[^\s]+)'

function New-Run {
    param([string]$Text, $Brush = $null)
    $r = New-Object System.Windows.Documents.Run $Text
    if ($Brush) { $r.Foreground = $Brush } else { $r.Foreground = $script:BrText }
    return $r
}

function Add-TextRuns {
    param([System.Windows.Documents.Paragraph]$Para, [string]$Text, [scriptblock]$Stile = $null)
    if ([string]::IsNullOrEmpty($Text)) { return }

    $aggiungi = {
        param($Run, [bool]$IsHost, [string]$Nome)
        if ($Stile) { & $Stile $Run }
        if ($IsHost) {
            $Run.Foreground = Get-HostBrush $Nome
            $Run.FontFamily = New-Object System.Windows.Media.FontFamily $script:FontMono
            $Run.Tag        = 'host:' + $Nome
        }
        [void]$Para.Inlines.Add($Run)
    }

    $last = 0
    foreach ($t in (Find-HostTokens $Text)) {
        if ($t.Start -gt $last) {
            & $aggiungi (New-Run $Text.Substring($last, $t.Start - $last)) $false ''
        }
        & $aggiungi (New-Run $t.Value) $true $t.Value
        $last = $t.Start + $t.Length
    }
    if ($last -lt $Text.Length) { & $aggiungi (New-Run $Text.Substring($last)) $false '' }
}

function Add-MarkdownInlines {
    param([System.Windows.Documents.Paragraph]$Para, [string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return }

    if ($Text -match '^(#{1,3})(\s+)(.*)$') {
        $lvl = $Matches[1].Length
        $size = @(21, 17, 15)[$lvl - 1]
        $mk = New-Run ($Matches[1] + $Matches[2]) $script:BrTer
        $mk.FontSize = $size
        [void]$Para.Inlines.Add($mk)
        $hd = New-Run $Matches[3] $script:BrText
        $hd.FontSize   = $size
        $hd.FontWeight = [System.Windows.FontWeights]::SemiBold
        [void]$Para.Inlines.Add($hd)
        return
    }

    $last = 0
    foreach ($m in $script:MdRegex.Matches($Text)) {
        if ($m.Index -gt $last) {
            Add-TextRuns $Para $Text.Substring($last, $m.Index - $last)
        }
        $v = $m.Value
        $ml = 1; $kind = 'code'
        if     ($m.Groups[1].Success) { $ml = 2; $kind = 'bold' }
        elseif ($m.Groups[2].Success) { $ml = 2; $kind = 'under' }
        elseif ($m.Groups[3].Success) { $ml = 2; $kind = 'strike' }
        elseif ($m.Groups[4].Success) { $ml = 2; $kind = 'mark' }
        elseif ($m.Groups[5].Success) { $ml = 1; $kind = 'italic' }
        elseif ($m.Groups[6].Success) { $ml = 1; $kind = 'italic' }
        elseif ($m.Groups[7].Success) { $ml = 1; $kind = 'code' }
        else                          { $ml = 0; $kind = 'link' }

        if ($ml -gt 0) { [void]$Para.Inlines.Add((New-Run $v.Substring(0, $ml) $script:BrTer)) }
        $inner = if ($ml -gt 0) { $v.Substring($ml, $v.Length - 2 * $ml) } else { $v }
        $stile = switch ($kind) {
            'bold'   { { param($r) $r.FontWeight = [System.Windows.FontWeights]::Bold } }
            'italic' { { param($r) $r.FontStyle  = [System.Windows.FontStyles]::Italic } }
            'under'  { { param($r) $r.TextDecorations = [System.Windows.TextDecorations]::Underline } }
            'strike' { { param($r) $r.TextDecorations = [System.Windows.TextDecorations]::Strikethrough
                                   $r.Foreground = $script:BrSec } }
            'mark'   { { param($r) $r.Background = $script:BrMarkBg
                                   $r.Foreground = $script:BrMarkFg } }
            'link'   { { param($r) $r.Foreground = $script:BrLink
                                   $r.TextDecorations = [System.Windows.TextDecorations]::Underline } }
            'code'   { { param($r) $r.FontFamily = New-Object System.Windows.Media.FontFamily $script:FontMono
                                   $r.Background = $script:BrCodeBg
                                   $r.Foreground = $script:BrCodeFg } }
        }
        if ($kind -in @('code', 'link')) {
            $cr = New-Run $inner
            & $stile $cr
            [void]$Para.Inlines.Add($cr)
        } else {
            Add-TextRuns $Para $inner $stile
        }
        if ($ml -gt 0) { [void]$Para.Inlines.Add((New-Run $v.Substring($v.Length - $ml, $ml) $script:BrTer)) }
        $last = $m.Index + $m.Length
    }
    if ($last -lt $Text.Length) { Add-TextRuns $Para $Text.Substring($last) }
    if ($Para.Inlines.Count -eq 0) { [void]$Para.Inlines.Add((New-Run $Text)) }
}

function Render-Paragraph {
    param([System.Windows.Documents.Paragraph]$Para, [string]$Line)
    $Para.TextDecorations = $null
    $Para.Background      = $null
    if (-not $script:Settings.LiveFormatting) {
        if (-not [string]::IsNullOrEmpty($Line)) { [void]$Para.Inlines.Add((New-Run $Line)) }
        return
    }
    if ([string]::IsNullOrWhiteSpace($Line)) { return }
    $info = Parse-Line $Line
    switch ($info.Type) {
        'comment' {
            $r = New-Run $info.RawText $script:BrTer
            $r.FontStyle = [System.Windows.FontStyles]::Italic
            [void]$Para.Inlines.Add($r)
        }
        'quote' {
            $b = New-Run ($info.Marker + ' ') $script:BrTer
            [void]$Para.Inlines.Add($b)
            $q = New-Run $info.Body $script:BrSec
            $q.FontStyle = [System.Windows.FontStyles]::Italic
            [void]$Para.Inlines.Add($q)
        }
        'rule' {
            $r = New-Run $info.RawText $script:BrTer
            [void]$Para.Inlines.Add($r)
        }
        'fence' {
            $r = New-Run $info.RawText $script:BrTer
            $r.FontFamily = New-Object System.Windows.Media.FontFamily $script:FontMono
            [void]$Para.Inlines.Add($r)
            $Para.Background = $script:BrCodeBg
        }
        'todo' {
            $pad = ''
            if ($info.Indent -gt 0) { $pad = ' ' * $info.Indent }
            $box = New-Run ($pad + $(if ($info.Done) { '- [x] ' } else { '- [ ] ' }))
            $box.FontFamily = New-Object System.Windows.Media.FontFamily $script:FontMono
            $box.FontWeight = [System.Windows.FontWeights]::SemiBold
            $box.Foreground = if ($info.Done) { $script:BrUp } else { $script:BrSec }
            $box.Tag = 'todo'
            $box.Cursor = [System.Windows.Input.Cursors]::Hand
            [void]$Para.Inlines.Add($box)
            if ($info.Done) {
                $body = New-Run $info.Body
                $body.TextDecorations = [System.Windows.TextDecorations]::Strikethrough
                $body.Foreground = $script:BrDone
                [void]$Para.Inlines.Add($body)
            } else {
                Add-TextRuns $Para $info.Body
            }
        }
        default { Add-MarkdownInlines $Para $info.RawText }
    }
}

function Get-ParagraphText {
    param($Para)
    try { return (New-Object System.Windows.Documents.TextRange($Para.ContentStart, $Para.ContentEnd)).Text }
    catch { return '' }
}

function Get-CaretOffsetIn {
    param([System.Windows.Documents.Paragraph]$Para)
    $caret = $script:Editor.CaretPosition
    if ($null -eq $caret -or -not [object]::ReferenceEquals($caret.Paragraph, $Para)) { return -1 }
    try { return (New-Object System.Windows.Documents.TextRange($Para.ContentStart, $caret)).Text.Length }
    catch { return -1 }
}

function Set-CaretOffsetIn {
    param([System.Windows.Documents.Paragraph]$Para, [int]$Offset)
    $avanti  = [System.Windows.Documents.LogicalDirection]::Forward
    $pos     = $Para.ContentStart
    $restano = $Offset
    while ($null -ne $pos -and $restano -gt 0 -and $pos.CompareTo($Para.ContentEnd) -lt 0) {
        if ($pos.GetPointerContext($avanti) -ne [System.Windows.Documents.TextPointerContext]::Text) {
            $pos = $pos.GetNextContextPosition($avanti)
            continue
        }
        $passo = [Math]::Min($pos.GetTextInRun($avanti).Length, $restano)
        $pos = $pos.GetPositionAtOffset($passo)
        $restano -= $passo
    }
    if ($null -eq $pos) { $pos = $Para.ContentEnd }
    $script:Editor.CaretPosition = $pos
}

function Format-Paragraph {
    param([System.Windows.Documents.Paragraph]$Para)
    if ($null -eq $Para -or $script:SkipFormat -or -not $script:Settings.LiveFormatting) { return }
    if ($null -eq $Para.Parent) { return }
    try { $sel = $script:Editor.Selection; if ($sel -and -not $sel.IsEmpty) { return } } catch { return }
    $txt = Get-ParagraphText $Para
    if ($null -ne $Para.Tag -and [string]$Para.Tag -eq $txt) { return }
    $caret = Get-CaretOffsetIn $Para
    $script:IsFormatting = $true
    try {
        $script:Editor.BeginChange()
        try {
            $Para.Inlines.Clear()
            Render-Paragraph $Para $txt
            $Para.Tag = Get-ParagraphText $Para
        } finally { $script:Editor.EndChange() }
        if ($caret -ge 0) { Set-CaretOffsetIn $Para $caret }
    } catch {} finally { $script:IsFormatting = $false }
}

function Get-CaretParagraph {
    try {
        $p = $script:Editor.CaretPosition
        if ($null -ne $p) { return $p.Paragraph }
    } catch {}
    return $null
}

function Flush-DirtyParagraphs {
    if ($script:SkipFormat -or -not $script:Settings.LiveFormatting) { $script:DirtyParas.Clear(); return }
    try { $sel = $script:Editor.Selection; if ($sel -and -not $sel.IsEmpty) { return } } catch { return }
    $list = @($script:DirtyParas)
    $script:DirtyParas.Clear()
    foreach ($p in $list) { Format-Paragraph $p }
}

function Format-AllParagraphs {
    if ($null -eq $script:Editor -or $null -eq $script:Editor.Document) { return }
    $script:IsFormatting = $true
    try {
        foreach ($p in (Get-AllParagraphs)) { 
            $t = Get-ParagraphText $p
            $script:Editor.BeginChange()
            try { $p.Inlines.Clear(); Render-Paragraph $p $t; $p.Tag = Get-ParagraphText $p }
            finally { $script:Editor.EndChange() }
        }
    } catch {} finally { $script:IsFormatting = $false }
}

function Get-AllParagraphs {
    $out = New-Object System.Collections.Generic.List[object]
    if ($null -eq $script:Editor -or $null -eq $script:Editor.Document) { return $out }
    $walk = {
        param($blocks)
        foreach ($b in @($blocks)) {
            if ($b -is [System.Windows.Documents.Paragraph]) { [void]$out.Add($b) }
            elseif ($b -is [System.Windows.Documents.List]) {
                foreach ($li in @($b.ListItems)) { & $walk $li.Blocks }
            }
            elseif ($b -is [System.Windows.Documents.Table]) {
                foreach ($rg in @($b.RowGroups)) {
                    foreach ($row in @($rg.Rows)) {
                        foreach ($cell in @($row.Cells)) { & $walk $cell.Blocks }
                    }
                }
            }
            elseif ($b -is [System.Windows.Documents.Section]) { & $walk $b.Blocks }
        }
    }
    & $walk $script:Editor.Document.Blocks
    return $out
}

function Refresh-EditorDots {
    if ($null -eq $script:Editor -or $null -eq $script:Editor.Document) { return }
    foreach ($p in (Get-AllParagraphs)) {
        foreach ($inl in @($p.Inlines)) {
            $tag = [string]$inl.Tag
            if (-not $tag.StartsWith('host:')) { continue }
            $inl.Foreground = Get-HostBrush $tag.Substring(5)
        }
    }
}

$script:HostsCache = $null

function Get-EditorHosts {
    if ($null -ne $script:HostsCache) { return $script:HostsCache }
    $list = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    foreach ($p in (Get-AllParagraphs)) {
        foreach ($t in (Find-HostTokens (Get-ParagraphText $p))) {
            if ($seen.ContainsKey($t.Value)) { continue }
            if ($script:Ignored.Contains($t.Value)) { continue }
            $seen[$t.Value] = $true
            [void]$list.Add($t.Value)
        }
    }
    $script:HostsCache = $list
    return $list
}

function Wrap-Selection {
    param([string]$Open, [string]$Close = $null)
    if ($null -eq $Close) { $Close = $Open }
    $sel = $script:Editor.Selection
    if ($null -eq $sel) { return }
    if (-not $sel.IsEmpty) {
        try {
            if (-not [object]::ReferenceEquals($sel.Start.Paragraph, $sel.End.Paragraph)) {
                Set-Status 'Seleziona il testo dentro una sola riga.'
                return
            }
        } catch { return }
    }
    $script:SkipFormat = $true
    try {
        if ($sel.IsEmpty) {
            $sel.Text = "$Open$Close"
            $p = $script:Editor.CaretPosition.GetPositionAtOffset(-1 * $Close.Length)
            if ($p) { $script:Editor.CaretPosition = $p }
        } else {
            $t = $sel.Text
            $trim = $t.Trim()
            if ($trim.StartsWith($Open) -and $trim.EndsWith($Close) -and $trim.Length -gt ($Open.Length + $Close.Length)) {
                $sel.Text = $trim.Substring($Open.Length, $trim.Length - $Open.Length - $Close.Length)
            } else {
                $sel.Text = "$Open$t$Close"
            }
        }
    } catch {} finally { $script:SkipFormat = $false }
    $p = Get-CaretParagraph
    if ($p) { $p.Tag = $null; Format-Paragraph $p }
}

function Set-LinePrefix {
    param([string]$Prefix, [switch]$Toggle)
    $p = Get-CaretParagraph
    if ($null -eq $p) { return }
    $txt = (Get-ParagraphText $p) -replace "`r|`n", ''
    $stripped = $txt -replace '^(#{1,3}\s+|>\s?)', ''
    $new = if ($Toggle -and $txt -eq ($Prefix + $stripped)) { $stripped } else { $Prefix + $stripped }
    $script:SkipFormat = $true
    try {
        $r = New-Object System.Windows.Documents.TextRange($p.ContentStart, $p.ContentEnd)
        $r.Text = $new
    } catch {} finally { $script:SkipFormat = $false }
    $p.Tag = $null
    Format-Paragraph $p
}

function Clear-Formatting {
    $sel = $script:Editor.Selection
    if ($null -eq $sel -or $sel.IsEmpty) { Set-Status 'Seleziona il testo da ripulire.'; return }
    try {
        if (-not [object]::ReferenceEquals($sel.Start.Paragraph, $sel.End.Paragraph)) {
            Set-Status 'Seleziona il testo dentro una sola riga.'
            return
        }
    } catch { return }
    $script:SkipFormat = $true
    try {
        $t = $sel.Text
        $t = $t -replace '\*\*([^\*]+)\*\*', '$1'
        $t = $t -replace '__([^_]+)__', '$1'
        $t = $t -replace '~~([^~]+)~~', '$1'
        $t = $t -replace '\*([^\*]+)\*', '$1'
        $t = $t -replace '`([^`]+)`', '$1'
        $t = $t -replace '(?m)^(#{1,3}\s+|>\s?)', ''
        $sel.Text = $t
    } catch {} finally { $script:SkipFormat = $false }
    $p = Get-CaretParagraph
    if ($p) { $p.Tag = $null; Format-Paragraph $p }
}

function New-TableCell {
    param([string]$Text = '', [bool]$Header = $false)
    $c = New-Object System.Windows.Documents.TableCell
    $c.BorderBrush     = $script:BrTblLn
    $c.BorderThickness = New-Object System.Windows.Thickness 0, 0, 1, 1
    $c.Padding         = New-Object System.Windows.Thickness 8, 5, 8, 5
    if ($Header) {
        $c.Background = $script:BrTblHd
        $c.FontWeight = [System.Windows.FontWeights]::SemiBold
    }
    $p = New-Object System.Windows.Documents.Paragraph
    $p.Margin = New-Object System.Windows.Thickness 0
    if ($Text) { [void]$p.Inlines.Add((New-Run $Text)) }
    [void]$c.Blocks.Add($p)
    return $c
}

function Insert-EditorTable {
    param([int]$Rows = 3, [int]$Cols = 3, [bool]$Header = $true, [string[][]]$Data = $null)
    if ($null -eq $script:Editor) { return }
    $script:SkipFormat = $true
    try {
        $t = New-Object System.Windows.Documents.Table
        $t.CellSpacing     = 0
        $t.Margin          = New-Object System.Windows.Thickness 0, 10, 0, 10
        $t.BorderBrush     = $script:BrTblLn
        $t.BorderThickness = New-Object System.Windows.Thickness 1, 1, 0, 0
        $t.FontSize        = 12.5
        for ($c = 0; $c -lt $Cols; $c++) {
            $col = New-Object System.Windows.Documents.TableColumn
            [void]$t.Columns.Add($col)
        }
        $rg = New-Object System.Windows.Documents.TableRowGroup
        for ($r = 0; $r -lt $Rows; $r++) {
            $row = New-Object System.Windows.Documents.TableRow
            for ($c = 0; $c -lt $Cols; $c++) {
                $txt = ''
                if ($Data -and $r -lt $Data.Count -and $c -lt $Data[$r].Count) { $txt = $Data[$r][$c] }
                [void]$row.Cells.Add((New-TableCell $txt ($Header -and $r -eq 0)))
            }
            [void]$rg.Rows.Add($row)
        }
        [void]$t.RowGroups.Add($rg)

        $cur = Get-CaretParagraph
        if ($cur -and $cur.Parent -and $cur.Parent.PSObject.Properties['Blocks']) {
            $blocks = $cur.Parent.Blocks
        } else {
            $blocks = $script:Editor.Document.Blocks
        }
        if ($cur -and $blocks.Contains($cur)) { $blocks.InsertAfter($cur, $t) }
        else { [void]$blocks.Add($t) }

        $after = New-Object System.Windows.Documents.Paragraph
        $after.Margin = New-Object System.Windows.Thickness 0
        $blocks.InsertAfter($t, $after)
        $script:Editor.CaretPosition = $t.RowGroups[0].Rows[0].Cells[0].ContentStart
    } catch {} finally { $script:SkipFormat = $false }
}

function Get-CurrentTableContext {
    $ctx = @{ Table = $null; RowGroup = $null; Row = $null; Cell = $null; RowIdx = -1; ColIdx = -1 }
    try {
        $pos = $script:Editor.CaretPosition
        if ($null -eq $pos) { return $ctx }
        $el = $pos.Parent
        while ($null -ne $el) {
            if ($el -is [System.Windows.Documents.TableCell]) { $ctx.Cell = $el; break }
            if ($el -is [System.Windows.Documents.FlowDocument]) { break }
            $el = $el.Parent
        }
        if ($null -eq $ctx.Cell) { return $ctx }
        $ctx.Row      = $ctx.Cell.Parent
        $ctx.RowGroup = $ctx.Row.Parent
        $ctx.Table    = $ctx.RowGroup.Parent
        $ctx.RowIdx   = $ctx.RowGroup.Rows.IndexOf($ctx.Row)
        $ctx.ColIdx   = $ctx.Row.Cells.IndexOf($ctx.Cell)
    } catch {}
    return $ctx
}

function Add-TableRow {
    $c = Get-CurrentTableContext
    if ($null -eq $c.Table) { Set-Status 'Posiziona il cursore in una tabella.'; return }
    $script:SkipFormat = $true
    try {
        $n = $c.Row.Cells.Count
        $row = New-Object System.Windows.Documents.TableRow
        for ($i = 0; $i -lt $n; $i++) { [void]$row.Cells.Add((New-TableCell '' $false)) }
        $c.RowGroup.Rows.Insert($c.RowIdx + 1, $row)
        $script:Editor.CaretPosition = $row.Cells[0].ContentStart
    } catch {} finally { $script:SkipFormat = $false }
}

function Remove-TableRow {
    $c = Get-CurrentTableContext
    if ($null -eq $c.Table) { Set-Status 'Posiziona il cursore in una tabella.'; return }
    if ($c.RowGroup.Rows.Count -le 1) { Set-Status 'La tabella deve avere almeno una riga.'; return }
    $script:SkipFormat = $true
    try { $c.RowGroup.Rows.Remove($c.Row) } catch {} finally { $script:SkipFormat = $false }
}

function Add-TableColumn {
    $c = Get-CurrentTableContext
    if ($null -eq $c.Table) { Set-Status 'Posiziona il cursore in una tabella.'; return }
    $script:SkipFormat = $true
    try {
        [void]$c.Table.Columns.Add((New-Object System.Windows.Documents.TableColumn))
        $ri = 0
        foreach ($row in @($c.RowGroup.Rows)) {
            $isHead = ($ri -eq 0)
            $idx = [Math]::Min($c.ColIdx + 1, $row.Cells.Count)
            $row.Cells.Insert($idx, (New-TableCell '' $isHead))
            $ri++
        }
    } catch {} finally { $script:SkipFormat = $false }
}

function Remove-TableColumn {
    $c = Get-CurrentTableContext
    if ($null -eq $c.Table) { Set-Status 'Posiziona il cursore in una tabella.'; return }
    if ($c.Row.Cells.Count -le 1) { Set-Status 'La tabella deve avere almeno una colonna.'; return }
    $script:SkipFormat = $true
    try {
        foreach ($row in @($c.RowGroup.Rows)) {
            if ($c.ColIdx -lt $row.Cells.Count) { $row.Cells.RemoveAt($c.ColIdx) }
        }
        if ($c.Table.Columns.Count -gt 1) { $c.Table.Columns.RemoveAt($c.Table.Columns.Count - 1) }
    } catch {} finally { $script:SkipFormat = $false }
}

function New-EditorDocument {
    $doc = New-Object System.Windows.Documents.FlowDocument
    $doc.PagePadding = New-Object System.Windows.Thickness 0
    $doc.FontFamily  = New-Object System.Windows.Media.FontFamily $script:FontUI
    $doc.FontSize    = 13.5
    $doc.LineHeight  = 21
    $doc.PageWidth   = [double]::NaN
    $doc.Foreground  = $script:BrText
    return $doc
}

function Build-DocumentFromText {
    param([string]$Text)
    $doc = New-EditorDocument
    $script:IsFormatting = $true
    try {
        foreach ($line in (($Text -replace "`r`n", "`n" -replace "`r", "`n") -split "`n")) {
            $p = New-Object System.Windows.Documents.Paragraph
            $p.Margin = New-Object System.Windows.Thickness 0
            [void]$doc.Blocks.Add($p)
            Render-Paragraph $p $line
            $p.Tag = Get-ParagraphText $p
        }
        if ($doc.Blocks.Count -eq 0) {
            [void]$doc.Blocks.Add((New-Object System.Windows.Documents.Paragraph))
        }
    } finally { $script:IsFormatting = $false }
    return $doc
}

function Get-NoteBytes {
    $r = New-Object System.Windows.Documents.TextRange(
        $script:Editor.Document.ContentStart, $script:Editor.Document.ContentEnd)
    $ms = New-Object System.IO.MemoryStream
    try {
        $r.Save($ms, [System.Windows.DataFormats]::Xaml)
        return ,$ms.ToArray()
    } finally { $ms.Dispose() }
}

function Backup-NoteFile {
    param([string]$Path)
    $bak = "$Path.bak"
    $scaduta = -not (Test-Path $bak) -or
               ((Get-Date) - (Get-Item $bak).LastWriteTime) -ge [TimeSpan]::FromMinutes(15)
    if ($scaduta -and (Test-Path $Path)) {
        try { Copy-Item -Path $Path -Destination $bak -Force } catch {}
    }
}

function Save-Note {
    param([switch]$Silent, [switch]$Plain)
    if ($script:Dismesso) { return }
    if ($null -eq $script:Editor -or $null -eq $script:Editor.Document) { return }
    if (-not $Plain -and (Test-VaultLocked) -and -not (Test-VaultOpen)) {
        if (-not $Silent) { Set-Status 'Nota non salvata: la cassaforte e'' chiusa.' }
        return
    }
    if ($script:NoteUnreadable) {
        if (-not $Silent) { Set-Status 'Nota non salvata: il file cifrato non e'' stato aperto.' }
        return
    }
    try {
        if (Test-VaultOpen) {
            Rotate-NoteSection
            Set-VaultSection $script:SezioneNota (Get-NoteBytes)
            Save-VaultStore
        } else {
            $tmp = "$($script:NoteFile).tmp"
            $fs = [IO.File]::Create($tmp)
            try {
                $r = New-Object System.Windows.Documents.TextRange(
                    $script:Editor.Document.ContentStart, $script:Editor.Document.ContentEnd)
                $r.Save($fs, [System.Windows.DataFormats]::Xaml)
            } finally { $fs.Close() }
            Backup-NoteFile $script:NoteFile
            Move-Item -Path $tmp -Destination $script:NoteFile -Force
        }
        if (-not $Silent) { Set-Status ('Nota salvata alle {0}.' -f (Get-Date).ToString('HH:mm:ss')) }
    } catch {
        if (-not $Silent) { Set-Status "Salvataggio non riuscito: $_" }
    }
}

function Get-UnreadableNoteText {
    $lines = @(
        '# La nota cifrata non si apre'
        ''
        'La chiave ha aperto la cassaforte, ma il contenuto di `store.bin` non supera'
        'il controllo di integrita'': il file e'' stato troncato, modificato da fuori, o'
        'scritto da una chiave diversa da questa.'
        ''
        '## Cosa fare'
        '- **Non scrivere qui.** L''editor e'' in sola lettura e il salvataggio e'' sospeso,'
        '  cosi'' il file cifrato resta intatto.'
        '- Cerca `store.bin.bak` in `%APPDATA%\DuckNote`: e'' la copia precedente. Chiudi'
        '  DuckNote, rinominala in `store.bin` e riapri.'
        '- Se hai una copia della cartella da prima del guasto, ripristina quella.'
        ''
        'Nessun dato viene toccato finche'' non decidi tu.'
    )
    return ($lines -join "`n")
}

function Get-WelcomeText {
    $tick = [char]0x60
    $fence = "$tick$tick$tick"
    $lines = @(
        '# DuckNote'
        ''
        'Note tecniche e scansione di rete nello stesso posto.'
        ''
        '## Host monitorati'
        '; scrivi gli indirizzi dove ti pare, anche in mezzo a una frase:'
        '; IPv4, IPv6 e nomi a dominio vengono raccolti da soli'
        'Il gateway e 192.168.1.1, il NAS nas.casa e il DNS di casa 2606:4700:4700::1111.'
        '8.8.8.8 DNS Google'
        '1.1.1.1 e 1.0.0.1 sono Cloudflare, sulla stessa riga'
        ''
        '## Da fare'
        '- [ ] controllare 10.0.0.50 prima di venerdi'
        '- [ ] clicca sul marcatore per spuntare la voce'
        '- [x] oppure premi Ctrl+Invio sulla riga'
        ''
        '## Come si scrive'
        ('**grassetto**, *corsivo*, __sottolineato__, ~~barrato~~, ==evidenziato==, ' + $tick + 'codice' + $tick)
        '> le righe con la freccia diventano citazioni'
        '- usa la barra sopra per elenchi, caselle e tabelle'
        ''
        $fence
        'blocchi di codice a larghezza fissa'
        $fence
        ''
        '---'
        ''
        '## Scorciatoie'
        '- Ctrl+F trova, Ctrl+H sostituisce, F3 risultato successivo'
        '- Alt+Su / Alt+Giu spostano la riga, Ctrl+D la duplica'
        '- Ctrl+rotellina o Ctrl+ +/- per lo zoom, Ctrl+0 reimposta'
        '- Invio prosegue automaticamente elenchi e caselle'
        ''
        'Premi F5 per analizzare gli host elencati qui sopra.'
    )
    return ($lines -join "`n")
}

function Get-NoteDocumentFromBytes {
    param([byte[]]$Bytes)
    $doc = New-EditorDocument
    $r  = New-Object System.Windows.Documents.TextRange($doc.ContentStart, $doc.ContentEnd)
    $ms = New-Object System.IO.MemoryStream (,$Bytes)
    try { $r.Load($ms, [System.Windows.DataFormats]::Xaml) } finally { $ms.Dispose() }
    return $doc
}

function Load-Note {
    $doc = $null
    $script:NoteUnreadable = $false
    try {
        if ((Test-VaultOpen) -and ($script:VaultBroken -or $null -ne (Get-VaultSection $script:SezioneNota))) {
            [byte[]]$bytes = if ($script:VaultBroken) { $null } else { Get-VaultSection $script:SezioneNota }
            if ($null -eq $bytes) {
                $script:NoteUnreadable = $true
                $doc = Build-DocumentFromText (Get-UnreadableNoteText)
            } else {
                $doc = Get-NoteDocumentFromBytes $bytes
            }
        }
        elseif (Test-Path $script:NoteFile) {
            $doc = New-EditorDocument
            $r = New-Object System.Windows.Documents.TextRange($doc.ContentStart, $doc.ContentEnd)
            $fs = [IO.File]::OpenRead($script:NoteFile)
            try { $r.Load($fs, [System.Windows.DataFormats]::Xaml) } finally { $fs.Close() }
        }
        elseif (Test-Path $script:LegacyFile) {
            $legacy = Get-Content $script:LegacyFile -Raw -Encoding UTF8
            $pfx = @([string][char]0x25CF, [char]::ConvertFromUtf32(0x1F7E2),
                     [char]::ConvertFromUtf32(0x1F534), [char]::ConvertFromUtf32(0x1F7E1))
            $legacy = ((($legacy -replace "`r`n", "`n") -split "`n") | ForEach-Object {
                $l = $_
                foreach ($p in $pfx) {
                    if ($l.TrimStart().StartsWith($p)) { $l = $l.TrimStart().Substring($p.Length).TrimStart(); break }
                }
                $l
            }) -join "`n"
            $doc = Build-DocumentFromText $legacy.TrimEnd("`r", "`n")
        }
        else {
            $doc = Build-DocumentFromText (Get-WelcomeText)
        }
    } catch {
        if (Test-VaultOpen) {
            $script:NoteUnreadable = $true
            $doc = Build-DocumentFromText (Get-UnreadableNoteText)
        } else {
            $doc = Build-DocumentFromText (Get-WelcomeText)
        }
    }
    if ($null -eq $doc) { $doc = Build-DocumentFromText (Get-WelcomeText) }
    $doc.FontFamily = New-Object System.Windows.Media.FontFamily $script:FontUI
    $doc.FontSize   = 13.5
    $doc.LineHeight = 21
    $doc.PagePadding = New-Object System.Windows.Thickness 0
    $script:LastPara = $null
    $script:HostsCache = $null
    $script:DirtyParas.Clear()
    $script:Editor.Document = $doc
    $script:Editor.IsReadOnly = $script:NoteUnreadable
    Format-AllParagraphs
}

function Append-ToNote {
    param([string[]]$Lines)
    if ($null -eq $script:Editor -or $Lines.Count -eq 0) { return }
    $script:SkipFormat = $true
    try {
        $blocks = $script:Editor.Document.Blocks
        foreach ($l in $Lines) {
            $p = New-Object System.Windows.Documents.Paragraph
            $p.Margin = New-Object System.Windows.Thickness 0
            [void]$blocks.Add($p)
            Render-Paragraph $p $l
            $p.Tag = Get-ParagraphText $p
        }
        $script:HostsCache = $null
    } catch {} finally { $script:SkipFormat = $false }
}

function Get-ParagraphSiblings {
    param($Para)
    if ($null -eq $Para) { return $null }
    $parent = $Para.Parent
    if ($null -eq $parent) { return $null }
    if ($parent -is [System.Windows.Documents.FlowDocument] -or
        $parent -is [System.Windows.Documents.Section] -or
        $parent -is [System.Windows.Documents.ListItem] -or
        $parent -is [System.Windows.Documents.TableCell]) {
        return ,$parent.Blocks
    }
    return $null
}

function Move-EditorLine {
    param([int]$Delta)
    $p = Get-CaretParagraph
    $blocks = Get-ParagraphSiblings $p
    if ($null -eq $blocks) { return }
    $list = @($blocks)
    $i = [Array]::IndexOf($list, $p)
    $j = $i + $Delta
    if ($i -lt 0 -or $j -lt 0 -or $j -ge $list.Count) { return }
    $anchor = $list[$j]
    $script:SkipFormat = $true
    try {
        [void]$blocks.Remove($p)
        if ($Delta -lt 0) { $blocks.InsertBefore($anchor, $p) } else { $blocks.InsertAfter($anchor, $p) }
        $script:Editor.CaretPosition = $p.ContentEnd
    } catch {} finally { $script:SkipFormat = $false }
    $script:HostsCache = $null
}

function Copy-EditorLine {
    $p = Get-CaretParagraph
    $blocks = Get-ParagraphSiblings $p
    if ($null -eq $blocks) { return }
    $txt = Get-ParagraphText $p
    $n = $null
    $script:SkipFormat = $true
    try {
        $n = New-Object System.Windows.Documents.Paragraph
        $n.Margin = $p.Margin
        [void]$n.Inlines.Add((New-Run $txt))
        $blocks.InsertAfter($p, $n)
        $n.Tag = $null
        $script:Editor.CaretPosition = $n.ContentEnd
    } catch { $n = $null } finally { $script:SkipFormat = $false }
    if ($n) { Format-Paragraph $n }
    $script:HostsCache = $null
}

function Invoke-SmartEnter {
    $p = Get-CaretParagraph
    if ($null -eq $p) { return $false }
    $txt = Get-ParagraphText $p
    if ([string]::IsNullOrWhiteSpace($txt)) { return $false }
    $prefix = ''
    if ($txt -match '^(\s*)([-*+])\s+\[[ xX]\]\s') { $prefix = $Matches[1] + $Matches[2] + ' [ ] ' }
    elseif ($txt -match '^(\s*)([-*+])\s+')        { $prefix = $Matches[1] + $Matches[2] + ' ' }
    elseif ($txt -match '^(\s*)(\d+)([.)])\s+')    { $prefix = $Matches[1] + ([int]$Matches[2] + 1).ToString() + $Matches[3] + ' ' }
    elseif ($txt -match '^(\s*)(>+)\s')            { $prefix = $Matches[1] + $Matches[2] + ' ' }
    else { return $false }

    if ($txt.Trim() -eq $prefix.Trim() -or $txt -match '^\s*([-*+]\s+(\[[ xX]\]\s*)?|>+\s*|\d+[.)]\s*)$') {
        $script:SkipFormat = $true
        try {
            $r = New-Object System.Windows.Documents.TextRange $p.ContentStart, $p.ContentEnd
            $r.Text = ''
        } catch {} finally { $script:SkipFormat = $false }
        return $false
    }
    Insert-EditorText $prefix -NewParagraph
    $script:HostsCache = $null
    return $true
}

function Toggle-Todo {
    $p = Get-CaretParagraph
    if ($null -eq $p) { return }
    $txt = Get-ParagraphText $p
    if ($txt -match '^(\s*)([-*+])\s+\[([ xX])\]\s?(.*)$') {
        $mark = if ($Matches[3] -eq ' ') { 'x' } else { ' ' }
        $new = '{0}{1} [{2}] {3}' -f $Matches[1], $Matches[2], $mark, $Matches[4]
    } elseif ($txt -match '^(\s*)([-*+])\s+(.*)$') {
        $new = '{0}{1} [ ] {2}' -f $Matches[1], $Matches[2], $Matches[3]
    } else {
        $new = '- [ ] ' + $txt.TrimStart()
    }
    $script:SkipFormat = $true
    try {
        $r = New-Object System.Windows.Documents.TextRange $p.ContentStart, $p.ContentEnd
        $r.Text = $new
    } catch {} finally { $script:SkipFormat = $false }
    $p.Tag = $null
    Format-Paragraph $p
    $script:HostsCache = $null
}

function Test-TodoAtPoint {
    param([System.Windows.Point]$Pt)
    if ($null -eq $script:Editor) { return $false }
    $pos = $script:Editor.GetPositionFromPoint($Pt, $false)
    if ($null -eq $pos) { return $false }
    $par = $pos.Parent
    return ($par -is [System.Windows.Documents.Run] -and [string]$par.Tag -eq 'todo')
}

function Insert-EditorText {
    param([string]$Text, [switch]$NewParagraph)
    if ($null -eq $script:Editor) { return }
    $script:SkipFormat = $true
    try {
        if ($NewParagraph) {
            $script:Editor.CaretPosition = $script:Editor.CaretPosition.InsertParagraphBreak()
        }
        if ($Text) {
            try {
                $script:Editor.CaretPosition.InsertTextInRun($Text)
                $script:Editor.CaretPosition = $script:Editor.CaretPosition.GetPositionAtOffset($Text.Length)
            } catch {
                $sel = $script:Editor.Selection
                $sel.Text = $Text
                $script:Editor.CaretPosition = $sel.End
            }
        }
    } catch {} finally { $script:SkipFormat = $false }
    $p = Get-CaretParagraph
    if ($p) { $p.Tag = $null; Format-Paragraph $p }
    $script:HostsCache = $null
}

function Paste-PlainText {
    $txt = ''
    try { $txt = [System.Windows.Clipboard]::GetText() } catch { return }
    if (-not $txt) { return }
    $primo = $true
    foreach ($l in ($txt -split "`r?`n")) {
        if ($primo) { Insert-EditorText $l; $primo = $false } else { Insert-EditorText $l -NewParagraph }
    }
}

function Insert-CodeBlock {
    $sel = $script:Editor.Selection
    $body = ''
    if ($sel -and -not $sel.IsEmpty) { $body = $sel.Text.Trim() }
    $f = [string][char]0x60 + [string][char]0x60 + [string][char]0x60
    $script:SkipFormat = $true
    try { if ($sel -and -not $sel.IsEmpty) { $sel.Text = '' } } catch {} finally { $script:SkipFormat = $false }
    Insert-EditorText $f
    foreach ($l in ($body -split "`r?`n")) { Insert-EditorText $l -NewParagraph }
    Insert-EditorText $f -NewParagraph
}

function Insert-Rule { Insert-EditorText '---' -NewParagraph }

function Insert-Link {
    $sel = $script:Editor.Selection
    $t = ''
    if ($sel -and -not $sel.IsEmpty) { $t = $sel.Text.Trim() }
    if ($t -match '^(https?://|www\.)') { Set-Status 'Collegamento gia'' presente.'; return }
    Insert-EditorText 'https://'
    Set-Status 'Completa l''indirizzo: gli URL vengono evidenziati automaticamente.'
}

function Set-EditorZoom {
    param([int]$Percent)
    if ($Percent -lt 60)  { $Percent = 60 }
    if ($Percent -gt 240) { $Percent = 240 }
    $script:Settings.EditorZoom = $Percent
    $s = $Percent / 100.0
    try {
        $st = New-Object System.Windows.Media.ScaleTransform $s, $s
        $script:Editor.LayoutTransform = $st
    } catch {}
    if ($script:UI.ZoomText) { $script:UI.ZoomText.Text = "$Percent%" }
}

function Update-WordCount {
    if ($null -eq $script:UI.WordCount) { return }
    $chars = 0; $words = 0; $lines = 0
    foreach ($p in (Get-AllParagraphs)) {
        $t = Get-ParagraphText $p
        $lines++
        $chars += $t.Length
        if ($t.Trim()) { $words += ([regex]::Matches($t, '\S+')).Count }
    }
    $script:UI.WordCount.Text = '{0} parole  ·  {1} caratteri  ·  {2} righe' -f $words, $chars, $lines
}

function Get-EditorOutline {
    $res = New-Object System.Collections.Generic.List[object]
    foreach ($p in (Get-AllParagraphs)) {
        $t = Get-ParagraphText $p
        if ($t -match '^(#{1,3})\s+(.+)$') {
            [void]$res.Add([pscustomobject]@{
                Level = $Matches[1].Length
                Text  = $Matches[2].Trim()
                Para  = $p
            })
        }
    }
    return $res
}

$script:FindLast = $null

function Find-EditorRange {
    param([System.Windows.Documents.TextPointer]$From, [string]$Needle, [bool]$MatchCase, [bool]$Backward)
    if (-not $Needle) { return $null }
    $cmp = if ($MatchCase) { [StringComparison]::Ordinal } else { [StringComparison]::OrdinalIgnoreCase }
    $dirF = [System.Windows.Documents.LogicalDirection]::Forward
    $dirB = [System.Windows.Documents.LogicalDirection]::Backward
    $ctxText = [System.Windows.Documents.TextPointerContext]::Text
    $ptr = $From
    while ($null -ne $ptr) {
        if ($ptr.GetPointerContext($dirF) -eq $ctxText) {
            $run = $ptr.GetTextInRun($dirF)
            $i = if ($Backward) { $run.LastIndexOf($Needle, $cmp) } else { $run.IndexOf($Needle, $cmp) }
            if ($i -ge 0) {
                $s = $ptr.GetPositionAtOffset($i)
                $e = $s.GetPositionAtOffset($Needle.Length)
                if ($null -ne $s -and $null -ne $e) {
                    return (New-Object System.Windows.Documents.TextRange $s, $e)
                }
            }
        }
        $ptr = $ptr.GetNextContextPosition($(if ($Backward) { $dirB } else { $dirF }))
    }
    return $null
}

function Measure-FindMatches {
    param([string]$Needle, [bool]$MatchCase)
    if (-not $Needle) { return 0 }
    $cmp = if ($MatchCase) { [StringComparison]::Ordinal } else { [StringComparison]::OrdinalIgnoreCase }
    $n = 0
    foreach ($p in (Get-AllParagraphs)) {
        $t = Get-ParagraphText $p
        $i = 0
        while ($true) {
            $i = $t.IndexOf($Needle, $i, $cmp)
            if ($i -lt 0) { break }
            $n++; $i += $Needle.Length
        }
    }
    return $n
}

function Invoke-FindNext {
    param([switch]$Backward)
    $needle = $script:UI.FindBox.Text
    if (-not $needle) { return }
    $case = [bool]$script:UI.FindCase.IsChecked
    $from = if ($Backward) { $script:Editor.Selection.Start } else { $script:Editor.Selection.End }
    $r = Find-EditorRange $from $needle $case $Backward.IsPresent
    if ($null -eq $r) {
        $home = if ($Backward) { $script:Editor.Document.ContentEnd } else { $script:Editor.Document.ContentStart }
        $r = Find-EditorRange $home $needle $case $Backward.IsPresent
    }
    if ($null -eq $r) { Set-Status ('Nessun risultato per "{0}".' -f $needle); return }
    $script:Editor.Selection.Select($r.Start, $r.End)
    try { $script:Editor.Focus() } catch {}
    $script:FindLast = $r
}

function Invoke-ReplaceOne {
    $needle = $script:UI.FindBox.Text
    if (-not $needle) { return }
    $case = [bool]$script:UI.FindCase.IsChecked
    $sel  = $script:Editor.Selection
    $cmp  = if ($case) { [StringComparison]::Ordinal } else { [StringComparison]::OrdinalIgnoreCase }
    if ($sel -and -not $sel.IsEmpty -and $sel.Text.Equals($needle, $cmp)) {
        $script:SkipFormat = $true
        try { $sel.Text = $script:UI.ReplBox.Text } catch {} finally { $script:SkipFormat = $false }
        $p = Get-CaretParagraph
        if ($p) { $p.Tag = $null; Format-Paragraph $p }
        $script:HostsCache = $null
    }
    Invoke-FindNext
    Update-FindStatus
}

function Invoke-ReplaceAll {
    $needle = $script:UI.FindBox.Text
    if (-not $needle) { return }
    $repl = $script:UI.ReplBox.Text
    $case = [bool]$script:UI.FindCase.IsChecked
    $opt  = if ($case) { [Text.RegularExpressions.RegexOptions]::None } else { [Text.RegularExpressions.RegexOptions]::IgnoreCase }
    $safeRepl = $repl -replace '\$', '$$$$'
    $n = 0
    $script:SkipFormat = $true
    try {
        foreach ($p in (Get-AllParagraphs)) {
            $t = Get-ParagraphText $p
            if (-not $t) { continue }
            $new = [regex]::Replace($t, [regex]::Escape($needle), $safeRepl, $opt)
            if ($new -ne $t) {
                $n += ([regex]::Matches($t, [regex]::Escape($needle), $opt)).Count
                $r = New-Object System.Windows.Documents.TextRange $p.ContentStart, $p.ContentEnd
                $r.Text = $new
                $p.Tag = $null
            }
        }
    } catch {} finally { $script:SkipFormat = $false }
    Format-AllParagraphs
    $script:HostsCache = $null
    Set-Status ('{0} sostituzioni.' -f $n)
    Update-FindStatus
}

function Update-FindStatus {
    if ($null -eq $script:UI.FindCount) { return }
    $needle = $script:UI.FindBox.Text
    if (-not $needle) { $script:UI.FindCount.Text = ''; return }
    $n = Measure-FindMatches $needle ([bool]$script:UI.FindCase.IsChecked)
    $script:UI.FindCount.Text = if ($n -eq 1) { '1 risultato' } else { "$n risultati" }
}

function New-Ease {
    param([string]$Kind = 'Cubic', [string]$Mode = 'EaseOut', [double]$Amplitude = 0.4)
    $e = switch ($Kind) {
        'Back'  { $b = New-Object System.Windows.Media.Animation.BackEase; $b.Amplitude = $Amplitude; $b }
        'Quint' { New-Object System.Windows.Media.Animation.QuinticEase }
        default { New-Object System.Windows.Media.Animation.CubicEase }
    }
    $e.EasingMode = [System.Windows.Media.Animation.EasingMode]::$Mode
    return $e
}

function New-Anim {
    param([double]$From, [double]$To, [int]$Ms, $Ease = $null)
    $a = New-Object System.Windows.Media.Animation.DoubleAnimation $From, $To,
         (New-Object System.Windows.Duration ([TimeSpan]::FromMilliseconds($Ms)))
    if ($Ease) { $a.EasingFunction = $Ease }
    return $a
}

function Show-FindBar {
    param([switch]$Hide)
    $fb = $script:UI.FindBar
    if ($null -eq $fb) { return }
    $H  = [System.Windows.FrameworkElement]::MaxHeightProperty
    $O  = [System.Windows.UIElement]::OpacityProperty
    $TY = [System.Windows.Media.TranslateTransform]::YProperty
    $SY = [System.Windows.Media.ScaleTransform]::ScaleYProperty

    if ($Hide) {
        if ($fb.Visibility -ne 'Visible') { return }
        $ah = New-Anim 46 0 170 (New-Ease 'Cubic' 'EaseIn')
        $ah.Add_Completed({ $script:UI.FindBar.Visibility = 'Collapsed' })
        $fb.BeginAnimation($H, $ah)
        $fb.BeginAnimation($O, (New-Anim 1 0 130))
        $script:UI.FindBarT.BeginAnimation($TY, (New-Anim 0 -16 170 (New-Ease 'Cubic' 'EaseIn')))
        $script:UI.FindBarS.BeginAnimation($SY, (New-Anim 1 0.9 170 (New-Ease 'Cubic' 'EaseIn')))
        try { $script:Editor.Focus() } catch {}
        return
    }

    $already = ($fb.Visibility -eq 'Visible')
    $fb.Visibility = 'Visible'
    if (-not $already) {
        $fb.BeginAnimation($H, (New-Anim 0 46 260 (New-Ease 'Cubic' 'EaseOut')))
        $fb.BeginAnimation($O, (New-Anim 0 1 190 (New-Ease 'Cubic' 'EaseOut')))
        $script:UI.FindBarT.BeginAnimation($TY, (New-Anim -16 0 320 (New-Ease 'Back' 'EaseOut' 0.35)))
        $script:UI.FindBarS.BeginAnimation($SY, (New-Anim 0.9 1 300 (New-Ease 'Back' 'EaseOut' 0.3)))
    }
    $sel = $script:Editor.Selection
    if ($sel -and -not $sel.IsEmpty -and $sel.Text.Length -lt 80 -and $sel.Text -notmatch "`n") {
        $script:UI.FindBox.Text = $sel.Text
    }
    [void]$script:UI.FindBox.Dispatcher.BeginInvoke([Action]{
        $script:UI.FindBox.Focus()
        $script:UI.FindBox.SelectAll()
    }, [System.Windows.Threading.DispatcherPriority]::Input)
    Update-FindStatus
}

function Move-Pill {
    param($Slide, $Scale, [double]$X, [switch]$Immediate)
    if ($null -eq $Slide) { return }
    $TX = [System.Windows.Media.TranslateTransform]::XProperty
    if ($Immediate) {
        $Slide.BeginAnimation($TX, $null)
        $Slide.X = $X
        return
    }
    $Slide.BeginAnimation($TX, (New-Anim $Slide.X $X 340 (New-Ease 'Back' 'EaseOut' 0.28)))
    $sx = New-Object System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
    $sx.Duration = New-Object System.Windows.Duration ([TimeSpan]::FromMilliseconds(340))
    foreach ($kf in @(@(0, 1.0), @(140, 0.88), @(340, 1.0))) {
        $k = New-Object System.Windows.Media.Animation.EasingDoubleKeyFrame
        $k.Value   = [double]$kf[1]
        $k.KeyTime = [System.Windows.Media.Animation.KeyTime]::FromTimeSpan([TimeSpan]::FromMilliseconds($kf[0]))
        $k.EasingFunction = New-Ease 'Cubic' 'EaseInOut'
        [void]$sx.KeyFrames.Add($k)
    }
    $Scale.BeginAnimation([System.Windows.Media.ScaleTransform]::ScaleXProperty, $sx)
}

function Move-SegPill {
    param([int]$Index, [switch]$Immediate)
    Move-Pill $script:UI.SegPillT $script:UI.SegPillS ($Index * 92) -Immediate:$Immediate
}

function Move-SideSegPill {
    param([int]$Index, [switch]$Immediate)
    $box = $script:UI.SideSegHost
    if ($null -eq $box -or $box.ActualWidth -le 1) { return }
    $half = $box.ActualWidth / 2
    $script:UI.SideSegPill.Width    = $half
    $script:UI.SideSegPillS.CenterX = $half / 2
    Move-Pill $script:UI.SideSegPillT $script:UI.SideSegPillS ($Index * $half) -Immediate:$Immediate
}

function Switch-View {
    param([string]$Name)
    $notes = $script:UI.ViewNotes
    $net   = $script:UI.ViewNet
    $O  = [System.Windows.UIElement]::OpacityProperty
    $TX = [System.Windows.Media.TranslateTransform]::XProperty
    if ($Name -eq 'note') {
        $net.Visibility   = 'Collapsed'
        $notes.Visibility = 'Visible'
        $notes.BeginAnimation($O, (New-Anim 0 1 220 (New-Ease 'Cubic' 'EaseOut')))
        $script:UI.ViewNotesT.BeginAnimation($TX, (New-Anim -18 0 300 (New-Ease 'Cubic' 'EaseOut')))
        try { $script:Editor.Focus() } catch {}
    } else {
        $notes.Visibility = 'Collapsed'
        $net.Visibility   = 'Visible'
        $net.BeginAnimation($O, (New-Anim 0 1 220 (New-Ease 'Cubic' 'EaseOut')))
        $script:UI.ViewNetT.BeginAnimation($TX, (New-Anim 18 0 300 (New-Ease 'Cubic' 'EaseOut')))
    }
}

function Switch-SideMode {
    param([string]$Mode)
    $outline = ($Mode -eq 'outline')
    $script:Settings.SidebarMode   = $Mode
    $script:UI.SideSearchHint.Text = if ($outline) { 'Filtra intestazioni' } else { 'Filtra host' }
    Move-SideSegPill ([int]$outline)
    Refresh-SideList

    $O  = [System.Windows.UIElement]::OpacityProperty
    $TX = [System.Windows.Media.TranslateTransform]::XProperty
    $from = if ($outline) { 16 } else { -16 }
    $script:UI.SideList.BeginAnimation($O, (New-Anim 0 1 220 (New-Ease 'Cubic' 'EaseOut')))
    $script:UI.SideListT.BeginAnimation($TX, (New-Anim $from 0 300 (New-Ease 'Cubic' 'EaseOut')))
}

$script:FilterOpen  = $false
$script:FilterWidth = 218.0
$script:FilterGap   = 6.0

function Get-FilterRoom {
    $slot = $script:UI.ColFilterSlot
    if ($null -eq $slot) { return $script:FilterWidth }
    $busy = $script:UI.BtnFilterOpen.ActualWidth + $script:UI.CmbStatus.ActualWidth + 28
    [Math]::Max(0, $slot.ActualWidth - $busy)
}

function Show-GridFilter {
    param([switch]$Hide, [switch]$Immediate)
    $wrap = $script:UI.FilterWrap
    if ($null -eq $wrap) { return }
    $W = [System.Windows.FrameworkElement]::WidthProperty

    if ($Hide) {
        if (-not $script:FilterOpen) { return }
        $script:FilterOpen = $false
        if ($Immediate) {
            $wrap.BeginAnimation($W, $null); $wrap.Width = 0
        } else {
            $wrap.BeginAnimation($W, (New-Anim $wrap.ActualWidth 0 220 (New-Ease 'Cubic' 'EaseIn')))
        }
        try { $script:UI.NetGrid.Focus() } catch {}
        return
    }

    if (-not $script:FilterOpen) {
        $script:FilterOpen = $true
        $room  = Get-FilterRoom
        $width = [Math]::Min($script:FilterWidth, $room)
        $script:UI.FilterField.Width = [Math]::Max(0, $width - $script:FilterGap)
        if ($Immediate) {
            $wrap.BeginAnimation($W, $null); $wrap.Width = $width
        } else {
            $ease = if ($room -ge ($width * 1.25)) {
                New-Ease 'Back' 'EaseOut' 0.22
            } else {
                New-Ease 'Cubic' 'EaseOut'
            }
            $wrap.BeginAnimation($W, (New-Anim 0 $width 300 $ease))
        }
    }
    [void]$script:UI.GridFilter.Dispatcher.BeginInvoke([Action]{
        $script:UI.GridFilter.Focus()
        $script:UI.GridFilter.SelectAll()
    }, [System.Windows.Threading.DispatcherPriority]::Input)
}

function Update-FilterGlyph {
    if ($null -eq $script:UI.GlyphFilter) { return }
    $active = [bool]$script:UI.GridFilter.Text
    if ($active) {
        $script:UI.GlyphFilter.Stroke = $script:UI.BtnFilterOpen.FindResource('Accent')
        $script:UI.GlyphFilter.StrokeThickness = 1.9
    } else {
        $script:UI.GlyphFilter.SetResourceReference(
            [System.Windows.Shapes.Shape]::StrokeProperty, 'LabelSecondary')
        $script:UI.GlyphFilter.StrokeThickness = 1.5
    }
}

$script:Window     = $null
$script:UI         = @{}
$script:SideItems  = New-Object 'System.Collections.ObjectModel.ObservableCollection[object]'
$script:PumpTimer  = $null
$script:SaveTimer  = $null
$script:FormatTimer= $null
$script:MonitorTimer = $null
$script:HostsTimer    = $null
$script:LastHostsSeen = ''
$script:SyncingSide  = $false

function Set-Status {
    param([string]$Text)
    if ($script:UI.StatusText) { $script:UI.StatusText.Text = $Text }
}

function New-Brush {
    param([string]$Hex)
    $b = New-Object System.Windows.Media.SolidColorBrush (ConvertFrom-Hex $Hex)
    $b.Freeze()
    return $b
}

function Apply-Theme {
    param([string]$Name)
    if (-not $script:Tokens.ContainsKey($Name)) { $Name = 'light' }
    $script:Settings.Theme = $Name
    $t = $script:Tokens[$Name]
    if ($script:Window) {
        $rd = $script:Window.Resources
        foreach ($k in @($t.Keys)) {
            if (-not $rd.Contains($k)) { continue }
            $col = ConvertFrom-Hex $t[$k]
            $cur = $rd[$k]
            if (($cur -is [System.Windows.Media.SolidColorBrush]) -and (-not $cur.IsFrozen)) {
                $cur.Color = $col
            } else {
                [System.Windows.Media.SolidColorBrush]$nb = New-Object System.Windows.Media.SolidColorBrush
                $nb.Color = $col
                [void]$rd.Remove($k)
                [void]$rd.Add($k, $nb)
            }
        }
    }
    Sync-EditorBrushes
    if ($script:UI.ThemeGlyph) {
        $d = if ($Name -eq 'dark') {
            'M6,10 A4,4 0 1 1 14,10 A4,4 0 1 1 6,10 Z M10,0.8 V3 M10,17 V19.2 M0.8,10 H3 M17,10 H19.2 M3.5,3.5 L5,5 M15,15 L16.5,16.5 M16.5,3.5 L15,5 M5,15 L3.5,16.5'
        } else {
            'M 10,2 A 8,8 0 1 0 18,10 A 6,6 0 1 1 10,2 Z'
        }
        try { $script:UI.ThemeGlyph.Data = [System.Windows.Media.Geometry]::Parse($d) } catch {}
        $gb = New-Brush $t.LabelSecondary
        if ($Name -eq 'dark') {
            $script:UI.ThemeGlyph.Fill = $null
            $script:UI.ThemeGlyph.Stroke = $gb
            $script:UI.ThemeGlyph.StrokeThickness = 1.3
        } else {
            $script:UI.ThemeGlyph.Stroke = $null
            $script:UI.ThemeGlyph.Fill = $gb
        }
    }
    Refresh-TableChrome
    Refresh-RowColors
    Refresh-SideList
    Refresh-EditorDots
    Update-DuckBrush
}

function Refresh-TableChrome {
    if ($null -eq $script:Editor -or $null -eq $script:Editor.Document) { return }
    $walk = {
        param($blocks)
        foreach ($b in @($blocks)) {
            if ($b -is [System.Windows.Documents.Table]) {
                $b.BorderBrush = $script:BrTblLn
                foreach ($rg in @($b.RowGroups)) {
                    foreach ($row in @($rg.Rows)) {
                        foreach ($cell in @($row.Cells)) {
                            $cell.BorderBrush = $script:BrTblLn
                            if ($null -ne $cell.Background) { $cell.Background = $script:BrTblHd }
                            & $walk $cell.Blocks
                        }
                    }
                }
            }
            elseif ($b -is [System.Windows.Documents.List]) {
                foreach ($li in @($b.ListItems)) { & $walk $li.Blocks }
            }
            elseif ($b -is [System.Windows.Documents.Section]) { & $walk $b.Blocks }
        }
    }
    & $walk $script:Editor.Document.Blocks
}

function Refresh-RowColors {
    foreach ($r in $script:Rows) { $r.DotColor = Get-DotHex $r.StatusRank }
}

function Get-DotHex {
    param([int]$Rank)
    $t = $script:Tokens[$script:Settings.Theme]
    switch ($Rank) {
        0 { return $t.Green }
        1 { return $t.Teal }
        2 { return $t.Red }
        3 { return $t.Orange }
        4 { return $t.LabelQuaternary }
        default { return $t.LabelQuaternary }
    }
}

function New-SideItem {
    param([string]$Key, [string]$Title, [string]$Dot, [string]$Subtitle = '', [string]$Ip = '', $Para = $null)
    $it = New-Object DuckNote.SideItem
    $it.Key      = $Key
    $it.Title    = $Title
    $it.Subtitle = $Subtitle
    $it.SubVis   = $(if ($Subtitle) { 'Visible' } else { 'Collapsed' })
    $it.Dot      = $Dot
    $it.IP       = $Ip
    $it.Para     = $Para
    return $it
}

function Sync-SideItems {
    param($Voluti)
    $vivi   = $script:SideItems
    $chiavi = @{}
    foreach ($v in $Voluti) { $chiavi[$v.Key] = $true }

    for ($i = $vivi.Count - 1; $i -ge 0; $i--) {
        if (-not $chiavi.ContainsKey($vivi[$i].Key)) { $vivi.RemoveAt($i) }
    }
    for ($i = 0; $i -lt $Voluti.Count; $i++) {
        $v = $Voluti[$i]
        if ($i -ge $vivi.Count -or $vivi[$i].Key -ne $v.Key) { $vivi.Insert($i, $v); continue }
        $vecchio = $vivi[$i]
        $vecchio.Title    = $v.Title
        $vecchio.Subtitle = $v.Subtitle
        $vecchio.SubVis   = $v.SubVis
        $vecchio.Dot      = $v.Dot
        $vecchio.IP       = $v.IP
        $vecchio.Para     = $v.Para
    }
    while ($vivi.Count -gt $Voluti.Count) { $vivi.RemoveAt($vivi.Count - 1) }
}

function Refresh-SideList {
    if ($null -eq $script:UI.SideList) { return }
    if ($script:Settings.SidebarMode -eq 'outline') { Refresh-OutlineList; return }
    $filter = ''
    if ($script:UI.SideSearch) { $filter = $script:UI.SideSearch.Text.Trim() }
    $items = New-Object System.Collections.Generic.List[object]
    $seen  = @{}

    foreach ($r in $script:Rows) {
        if ($r.StatusRank -gt 2) { continue }
        if ($seen.ContainsKey($r.IP)) { continue }
        $seen[$r.IP] = $true
        foreach ($n in @($r.Hostname, $r.NetBiosName, $r.MdnsName)) {
            if ($n) { $seen[$n.ToLower()] = $true }
        }
        $sub = if ($r.Hostname) { $r.Hostname } elseif ($r.NetBiosName) { $r.NetBiosName } else { '' }
        if ($filter -and ("$($r.IP) $sub" -notmatch [regex]::Escape($filter))) { continue }
        [void]$items.Add((New-SideItem -Key "h:$($r.IP)" -Title $r.IP -Subtitle $sub `
                                       -Dot (Get-DotHex $r.StatusRank) -Ip $r.IP))
    }
    foreach ($h in (Get-EditorHosts)) {
        if ($seen.ContainsKey($h) -or $seen.ContainsKey($h.ToLower())) { continue }
        $seen[$h] = $true
        $seen[$h.ToLower()] = $true
        if ($filter -and ($h -notmatch [regex]::Escape($filter))) { continue }
        $rank = 4
        if ($script:HostStates.ContainsKey($h)) { $rank = if ($script:HostStates[$h]) { 0 } else { 2 } }
        [void]$items.Add((New-SideItem -Key "h:$h" -Title $h -Dot (Get-DotHex $rank) -Ip $h))
    }

    Sync-SideItems $items
    if ($script:UI.SideFoot) {
        $up = @($script:Rows | Where-Object { $_.StatusRank -le 1 }).Count
        if ($items.Count -eq 0) { $script:UI.SideFoot.Text = 'Nessun host' }
        else { $script:UI.SideFoot.Text = ('{0} host, {1} attivi' -f $items.Count, $up) }
    }
    Update-HeaderStats
}

function Select-SideHost {
    param([string]$Ip)
    if ($script:SyncingSide -or $null -eq $script:UI.SideList) { return }
    if ($script:Settings.SidebarMode -ne 'host' -or -not $Ip) { return }
    $item = $script:SideItems | Where-Object { $_.IP -eq $Ip } | Select-Object -First 1
    if ($null -eq $item -or [object]::ReferenceEquals($script:UI.SideList.SelectedItem, $item)) { return }
    $script:SyncingSide = $true
    try {
        $script:UI.SideList.SelectedItem = $item
        $script:UI.SideList.ScrollIntoView($item)
    } catch {} finally { $script:SyncingSide = $false }
}

$script:FirstChecked  = New-Object 'System.Collections.Generic.HashSet[string]'
$script:FirstQueue    = New-Object 'System.Collections.Generic.HashSet[string]'
$script:FirstTimer    = $null

function Queue-FirstCheck {
    param([string[]]$Hosts)
    foreach ($h in $Hosts) {
        if ([string]::IsNullOrWhiteSpace($h)) { continue }
        if (-not $script:FirstChecked.Add($h)) { continue }
        [void]$script:FirstQueue.Add($h)
    }
    if ($script:FirstQueue.Count -eq 0 -or $null -eq $script:FirstTimer) { return }
    $script:FirstTimer.Stop(); $script:FirstTimer.Start()
}

function Remove-StaleNoteRows {
    if ($script:ScanActive) { return }
    $vivi = @{}
    foreach ($h in (Get-EditorHosts)) { $vivi[$h] = $true }

    for ($i = $script:Rows.Count - 1; $i -ge 0; $i--) {
        $r = $script:Rows[$i]
        if (-not $r.NoteKey -or $vivi.ContainsKey($r.NoteKey)) { continue }
        [void]$script:RowIndex.Remove($r.NoteKey)
        [void]$script:RowIndex.Remove($r.IP)
        [void]$script:FirstChecked.Remove($r.NoteKey)
        $script:HostStates.Remove($r.NoteKey)
        $script:Rows.RemoveAt($i)
    }
}

function Start-FirstCheck {
    if ($script:FirstQueue.Count -eq 0) { return }
    if ($script:ScanActive) { $script:FirstTimer.Start(); return }
    $targets = @($script:FirstQueue)
    $script:FirstQueue.Clear()
    Start-Scan -Targets $targets -KeepExisting
    Set-Status ('Primo controllo di {0} host appena aggiunti...' -f $targets.Count)
}

$script:MenuHost = ''

function Remove-HostRows {
    param([string]$Name)
    for ($i = $script:Rows.Count - 1; $i -ge 0; $i--) {
        $r = $script:Rows[$i]
        if ($r.IP -ne $Name -and $r.NoteKey -ne $Name) { continue }
        foreach ($k in @($r.IP, $r.NoteKey)) {
            if (-not $k) { continue }
            [void]$script:RowIndex.Remove($k)
            $script:HostStates.Remove($k)
        }
        $script:Rows.RemoveAt($i)
    }
}

function Refresh-IgnoredView {
    $script:HostsCache    = $null
    $script:LastHostsSeen = (@(Get-EditorHosts) -join ',')
    Refresh-SideList
    Refresh-EditorDots
}

function Ignore-Host {
    param([string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return }
    if (-not $script:Ignored.Add($Name)) { return }
    Remove-HostRows $Name
    [void]$script:FirstChecked.Remove($Name)
    [void]$script:FirstQueue.Remove($Name)
    $script:HostStates.Remove($Name)
    Save-IgnoredHosts
    Refresh-IgnoredView
    Set-Status ('{0}: escluso dal ping. Tasto destro sul nome per riprenderlo.' -f $Name)
}

function Restore-Host {
    param([string]$Name)
    if (-not $script:Ignored.Remove($Name)) { return }
    Save-IgnoredHosts
    Refresh-IgnoredView
    if ((Get-EditorHosts) -contains $Name) { Queue-FirstCheck @($Name) }
    Set-Status ('{0}: torna sotto analisi.' -f $Name)
}

function Sync-NoteHosts {
    $script:HostsTimer.Stop()
    $hosts = @(Get-EditorHosts)
    $ora   = ($hosts -join ',')
    if ($ora -eq $script:LastHostsSeen) { return }
    $script:LastHostsSeen = $ora
    Remove-StaleNoteRows
    Refresh-SideList
    Refresh-EditorDots
    Queue-FirstCheck $hosts
}

$script:CommitPending = $false

function Request-EditorCommit {
    if ($script:CommitPending) { return }
    $script:CommitPending = $true
    [void]$script:Editor.Dispatcher.BeginInvoke([Action]{
        $script:CommitPending = $false
        $script:FormatTimer.Stop()
        Flush-DirtyParagraphs
        Sync-NoteHosts
    }, [System.Windows.Threading.DispatcherPriority]::Background)
}

function Get-SideItemAt {
    param($Source)
    $n = $Source
    while ($null -ne $n) {
        if ($n -is [System.Windows.Controls.ListBoxItem]) { return $n.DataContext }
        try { $n = [System.Windows.Media.VisualTreeHelper]::GetParent($n) } catch { break }
    }
    return $null
}

function Prepare-HostMenu {
    param($Source)
    $it = Get-SideItemAt $Source
    $script:MenuHost = if ($null -ne $it) { [string]$it.IP } else { '' }

    $voce = $script:UI.HostMenuIgnore
    $voce.Header    = if ($script:MenuHost) { "Non analizzare $($script:MenuHost)" } else { 'Non analizzare' }
    $voce.IsEnabled = [bool]$script:MenuHost

    $esclusi = @($script:Ignored | Sort-Object)
    $sotto   = $script:UI.HostMenuIgnored
    $sotto.Items.Clear()
    $sotto.Header    = if ($esclusi.Count) { 'Ignorati ({0})' -f $esclusi.Count } else { 'Nessun host ignorato' }
    $sotto.IsEnabled = [bool]$esclusi.Count
    foreach ($h in $esclusi) {
        $ri = New-Object System.Windows.Controls.MenuItem
        $ri.Header = $h
        $ri.Tag    = $h
        $ri.Add_Click({ param($s, $e) Restore-Host ([string]$s.Tag) })
        [void]$sotto.Items.Add($ri)
    }
}

function Get-HostAtPosition {
    param($Pos)
    if ($null -eq $Pos) { return '' }
    $par = $Pos.Paragraph
    if ($null -eq $par) { return '' }
    try { $off = (New-Object System.Windows.Documents.TextRange($par.ContentStart, $Pos)).Text.Length }
    catch { return '' }
    foreach ($t in (Find-HostTokens (Get-ParagraphText $par))) {
        if ($off -ge $t.Start -and $off -le ($t.Start + $t.Length)) { return $t.Value }
    }
    return ''
}

function Prepare-EditorMenu {
    param($Pos)
    $script:MenuHost = Get-HostAtPosition $Pos
    $sopra = [bool]$script:MenuHost
    foreach ($v in @($script:UI.EdMenuWatch, $script:UI.EdMenuPing,
                     $script:UI.EdMenuCopyHost, $script:UI.EdMenuSep)) {
        $v.Visibility = if ($sopra) { 'Visible' } else { 'Collapsed' }
    }
    if ($sopra) {
        $pingato = -not $script:Ignored.Contains($script:MenuHost)
        $script:UI.EdMenuWatch.Header = if ($pingato) {
            'Smetti di pingare {0}' -f $script:MenuHost
        } else {
            'Pinga {0}' -f $script:MenuHost
        }
        $script:UI.EdMenuPing.Header      = 'Analizza {0} adesso' -f $script:MenuHost
        $script:UI.EdMenuPing.IsEnabled   = $pingato
        $script:UI.EdMenuCopyHost.Header  = 'Copia {0}' -f $script:MenuHost
    }

    $sel = $script:Editor.Selection
    $haSel = ($null -ne $sel) -and (-not $sel.IsEmpty)
    $script:UI.EdMenuCut.IsEnabled  = $haSel
    $script:UI.EdMenuCopy.IsEnabled = $haSel

    $incollabile = $false
    try { $incollabile = [System.Windows.Clipboard]::ContainsText() } catch {}
    $script:UI.EdMenuPaste.IsEnabled    = $incollabile
    $script:UI.EdMenuPasteRaw.IsEnabled = $incollabile
}

function Refresh-OutlineList {
    $filter = ''
    if ($script:UI.SideSearch) { $filter = $script:UI.SideSearch.Text.Trim() }
    $t = $script:Tokens[$script:Settings.Theme]
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($h in (Get-EditorOutline)) {
        if ($filter -and ($h.Text -notmatch [regex]::Escape($filter))) { continue }
        $pad = '   ' * ($h.Level - 1)
        $dot = if ($h.Level -eq 1) { $t.Accent } elseif ($h.Level -eq 2) { $t.LabelSecondary } else { $t.LabelQuaternary }
        [void]$items.Add((New-SideItem -Key "o:$($items.Count)" -Title ($pad + $h.Text) -Dot $dot -Para $h.Para))
    }

    Sync-SideItems $items
    if ($script:UI.SideFoot) {
        $n = $items.Count
        $script:UI.SideFoot.Text = if ($n -eq 0) { 'Nessuna intestazione (usa # Titolo)' } else { "$n intestazioni" }
    }
    Update-HeaderStats
}

function Update-HeaderStats {
    if ($null -eq $script:UI.StatHosts) { return }
    $up = 0; $down = 0
    foreach ($r in $script:Rows) {
        if ($r.StatusRank -le 1) { $up++ } elseif ($r.StatusRank -eq 2 -or $r.StatusRank -eq 3) { $down++ }
    }
    $script:UI.StatHosts.Text = "$($script:Rows.Count)"
    $script:UI.StatUp.Text    = "$up"
    $script:UI.StatDown.Text  = "$down"
}

$script:DetailXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="Dettagli host" Width="340" Height="620" MinWidth="290" MinHeight="320"
        WindowStyle="None" AllowsTransparency="True" ShowInTaskbar="False" ShowActivated="False"
        ResizeMode="CanResize" WindowStartupLocation="Manual" UseLayoutRounding="True"
        FontFamily="SF Pro Text, Segoe UI Variable Text, Segoe UI" FontSize="13"
        Background="Transparent">

  <shell:WindowChrome.WindowChrome>
    <shell:WindowChrome CaptionHeight="0" CornerRadius="0" GlassFrameThickness="0"
                        ResizeBorderThickness="6" UseAeroCaptionButtons="False"/>
  </shell:WindowChrome.WindowChrome>

  <Border x:Name="DShell" CornerRadius="14" Background="{DynamicResource BgSidebarGlass}"
          BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
  <Border.RenderTransform>
    <TranslateTransform x:Name="DSlide" X="0"/>
  </Border.RenderTransform>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="40"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <Grid x:Name="DTitleBar" Grid.Row="0" Background="Transparent">
      <Button x:Name="DClose" Style="{DynamicResource TrafficBtn}" Background="{DynamicResource TLClose}"
              HorizontalAlignment="Left" VerticalAlignment="Center" Margin="14,0,0,0" ToolTip="Chiudi (Esc)"
              shell:WindowChrome.IsHitTestVisibleInChrome="True">
        <Path Data="M 0,0 L 6,6 M 6,0 L 0,6" Stroke="#5A1416" StrokeThickness="1.2" Stretch="Uniform"
              StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
      </Button>
      <TextBlock Text="Dettagli host" FontSize="12" FontWeight="SemiBold" HorizontalAlignment="Center"
                 VerticalAlignment="Center" Foreground="{DynamicResource LabelSecondary}"/>
    </Grid>

    <Grid Grid.Row="1" Margin="10,0,10,10">
      <Border x:Name="DSheet" CornerRadius="14" Background="{DynamicResource SheetScrim}"
              BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1" ClipToBounds="True">
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
          </Grid.RowDefinitions>

          <Border Grid.Row="0" Padding="18,15,18,13">
            <StackPanel>
              <StackPanel Orientation="Horizontal">
                <TextBlock x:Name="DTitle" Text="Nessuna selezione" FontSize="17" FontWeight="SemiBold"
                           Foreground="{DynamicResource Label}" TextTrimming="CharacterEllipsis"/>
                <Border x:Name="DBadgeBox" CornerRadius="9" Margin="9,2,0,0" Padding="9,2" VerticalAlignment="Center"
                        Background="{DynamicResource AccentSoft}" BorderBrush="{DynamicResource AccentBorder}"
                        BorderThickness="1" Visibility="Collapsed">
                  <TextBlock x:Name="DBadge" Text="" FontSize="10" FontWeight="SemiBold"
                             Foreground="{DynamicResource Accent}"/>
                </Border>
              </StackPanel>
              <TextBlock x:Name="DSub" Text="Seleziona un host nella tabella." Margin="0,4,0,0"
                         FontSize="11.5" Foreground="{DynamicResource LabelSecondary}" TextWrapping="Wrap"/>
            </StackPanel>
          </Border>

          <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" Padding="18,4,14,18">
            <StackPanel x:Name="DBody"/>
          </ScrollViewer>
        </Grid>
      </Border>
      <Border CornerRadius="14" BorderThickness="1" BorderBrush="{DynamicResource EdgeSheen}"
              IsHitTestVisible="False"/>
    </Grid>
  </Grid>
  </Border>
</Window>
'@

$script:DetailWin  = $null
$script:Det        = @{}
$script:DetailOpen = $false
$script:DetailRow  = $null
$script:DetailGap  = 10.0

function Get-DetailWindow {
    if ($script:DetailWin) { return $script:DetailWin }
    $rd  = [System.Xml.XmlReader]::Create([System.IO.StringReader]$script:DetailXaml)
    $win = [System.Windows.Markup.XamlReader]::Load($rd)
    try { [void]$win.Resources.MergedDictionaries.Add($script:Window.Resources) } catch {}
    $win.Owner = $script:Window
    $script:Det = @{}
    foreach ($n in @('DShell','DSlide','DTitleBar','DClose','DTitle','DSub','DBody','DSheet','DBadge','DBadgeBox')) {
        $script:Det[$n] = $win.FindName($n)
    }
    $script:Det.DTitleBar.Add_MouseLeftButtonDown({ try { $script:DetailWin.DragMove() } catch {} })
    $script:Det.DClose.Add_Click({ Hide-HostDetails })
    $win.Add_KeyDown({ param($s, $e) if ($e.Key -eq 'Escape') { Hide-HostDetails } })
    $win.Add_SizeChanged({ if ($script:DetailOpen) { $script:Settings.InspectorWidth = [int]$script:DetailWin.Width } })
    $win.Add_Closed({ $script:DetailWin = $null; $script:Det = @{}; $script:DetailOpen = $false; Update-InspectGlyph })
    $script:DetailWin = $win
    return $win
}

function Get-MainBounds {
    $m = $script:Window
    if ($m.WindowState -eq 'Maximized') {
        $a = [System.Windows.SystemParameters]::WorkArea
        return @{ Left = $a.Left; Top = $a.Top; Width = $a.Width; Height = $a.Height }
    }
    @{ Left = $m.Left; Top = $m.Top; Width = $m.ActualWidth; Height = $m.ActualHeight }
}

$script:Zoomed      = $false
$script:ZoomRestore = $null

function Get-DetailReserve {
    if (-not $script:DetailOpen) { return 0.0 }
    [double]$script:Settings.InspectorWidth + $script:DetailGap + 4
}

function Sync-DetailSoon {
    [void]$script:Window.Dispatcher.BeginInvoke([Action]{ Sync-DetailBounds },
        [System.Windows.Threading.DispatcherPriority]::Loaded)
}

function Update-ZoomBounds {
    if (-not $script:Zoomed) { return }
    $a = [System.Windows.SystemParameters]::WorkArea
    $w = $script:Window
    $w.Left   = $a.Left
    $w.Top    = $a.Top
    $w.Height = $a.Height
    $w.Width  = [Math]::Max($w.MinWidth, $a.Width - (Get-DetailReserve))
    Sync-DetailSoon
}

function Set-WindowZoom {
    param([switch]$Off)
    $w = $script:Window
    if ($Off) {
        if (-not $script:Zoomed) { return }
        $script:Zoomed = $false
        $r = $script:ZoomRestore
        if ($r) {
            $w.Left = $r.Left; $w.Top = $r.Top
            $w.Width = $r.Width; $w.Height = $r.Height
        }
        Sync-DetailSoon
        return
    }
    if (-not $script:Zoomed) {
        $script:ZoomRestore = @{
            Left = $w.Left; Top = $w.Top; Width = $w.ActualWidth; Height = $w.ActualHeight
        }
        $script:Zoomed = $true
    }
    Update-ZoomBounds
}

function Get-DetailSlot {
    $b    = Get-MainBounds
    $area = [System.Windows.SystemParameters]::WorkArea
    $w    = [double]$script:Settings.InspectorWidth
    $right = $b.Left + $b.Width + $script:DetailGap
    if (($right + $w) -le ($area.Right - 4)) { return @{ X = $right; Side = 1 } }
    $left = $b.Left - $script:DetailGap - $w
    if ($left -ge ($area.Left + 4)) { return @{ X = $left; Side = -1 } }
    @{ X = [Math]::Max($area.Left + 4, $area.Right - $w - 8); Side = 1 }
}

$script:DetailShift      = 44.0
$script:DetailSide       = 1
$script:DetailMoving     = $false
$script:DetailAfterLeave = 'chiudi'

function Start-DetailEnter {
    $X = [System.Windows.Media.TranslateTransform]::XProperty
    $O = [System.Windows.UIElement]::OpacityProperty
    $script:Det.DSlide.BeginAnimation($X, $null)
    $script:Det.DShell.BeginAnimation($O, $null)
    $script:Det.DSlide.X = -$script:DetailShift * $script:DetailSide
    $script:Det.DShell.Opacity = 0
    $script:Det.DSlide.BeginAnimation($X, (New-Anim $script:Det.DSlide.X 0 340 (New-Ease 'Quint' 'EaseOut')))
    $script:Det.DShell.BeginAnimation($O, (New-Anim 0 1 260 (New-Ease 'Cubic' 'EaseOut')))
}

function Start-DetailLeave {
    $X = [System.Windows.Media.TranslateTransform]::XProperty
    $O = [System.Windows.UIElement]::OpacityProperty
    $fade = New-Anim $script:Det.DShell.Opacity 0 200 (New-Ease 'Cubic' 'EaseIn')
    $fade.Add_Completed({
        try {
            $w = $script:DetailWin
            if ($null -eq $w) { return }
            $script:Det.DSlide.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $null)
            $script:Det.DShell.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $null)
            $script:Det.DShell.Opacity = 0
            if ($script:DetailAfterLeave -eq 'cambia-lato') {
                $script:DetailMoving = $false
                $slot = Get-DetailSlot
                $w.Left = $slot.X
                $script:DetailSide = $slot.Side
                Sync-DetailBounds
                Start-DetailEnter
            } else {
                $w.Hide()
                $script:Det.DSlide.X = 0
            }
        } catch {}
        $script:DetailMoving = $false
    })
    $script:Det.DSlide.BeginAnimation($X,
        (New-Anim $script:Det.DSlide.X (-$script:DetailShift * $script:DetailSide) 220 (New-Ease 'Cubic' 'EaseIn')))
    $script:Det.DShell.BeginAnimation($O, $fade)
}

function Show-HostDetails {
    $win = Get-DetailWindow
    $script:DetailOpen = $true
    Update-ZoomBounds
    $b    = Get-MainBounds
    $slot = Get-DetailSlot
    $script:DetailSide = $slot.Side

    $win.Width  = [double]$script:Settings.InspectorWidth
    $win.Height = [Math]::Max(320, $b.Height - 8)
    $win.Top    = $b.Top + 4
    $win.Left   = $slot.X
    Fill-Details $script:DetailRow
    $script:Det.DShell.Opacity = 0
    $win.Show()
    $script:DetailOpen = $true
    Update-InspectGlyph
    Start-DetailEnter
}

function Hide-HostDetails {
    if (-not $script:DetailOpen -or $null -eq $script:DetailWin) { return }
    $script:DetailOpen = $false
    Update-InspectGlyph
    $script:DetailAfterLeave = 'chiudi'
    Start-DetailLeave
    Update-ZoomBounds
}

$script:DragTimer = $null

function Request-WindowSettle {
    Suspend-Ducks
    if ($null -eq $script:DragTimer) {
        $script:DragTimer = New-Object System.Windows.Threading.DispatcherTimer
        $script:DragTimer.Interval = [TimeSpan]::FromMilliseconds(90)
        $script:DragTimer.Add_Tick({
            $script:DragTimer.Stop()
            Sync-DetailBounds
            Resume-Ducks
        })
    }
    $script:DragTimer.Stop()
    $script:DragTimer.Start()
}

function Sync-DetailBounds {
    if (-not $script:DetailOpen -or $null -eq $script:DetailWin) { return }
    if ($script:DetailMoving) { return }
    $b    = Get-MainBounds
    $slot = Get-DetailSlot
    $win  = $script:DetailWin
    $win.Top    = $b.Top + 4
    $win.Height = [Math]::Max(320, $b.Height - 8)
    if ($slot.Side -ne $script:DetailSide) {
        $script:DetailMoving     = $true
        $script:DetailAfterLeave = 'cambia-lato'
        Start-DetailLeave
        return
    }
    $win.Left = $slot.X
}

$script:SbTarget = 0.0
$script:SbAnim   = $null

function Animate-Sidebar {
    param([double]$To, [int]$Ms = 190)
    $col = $script:UI.ColSidebar
    if ($null -eq $col) { return }
    $W = [System.Windows.Controls.ColumnDefinition]::WidthProperty
    $col.MinWidth   = 0
    $script:SbTarget = $To
    $script:SbAnim   = $true
    Suspend-Ducks

    $a = New-Object DuckNative.GridLengthAnimation
    $a.From = [double]$col.ActualWidth
    $a.To   = $To
    $a.Duration = New-Object System.Windows.Duration ([TimeSpan]::FromMilliseconds($Ms))
    $a.EasingFunction = New-Ease 'Cubic' 'EaseOut'
    $a.Add_Completed({
        $c = $script:UI.ColSidebar
        $c.BeginAnimation([System.Windows.Controls.ColumnDefinition]::WidthProperty, $null)
        $c.Width = New-Object System.Windows.GridLength $script:SbTarget
        if ($script:SbTarget -le 0) { $script:UI.SidebarPanel.Visibility = 'Collapsed' }
        else { $c.MinWidth = 170 }
        $script:SbAnim = $null
        Resume-Ducks
    })
    $col.BeginAnimation($W, $a)
}

function Update-InspectGlyph {
    $g = $script:UI.GlyphInspect
    if ($null -eq $g) { return }
    if ($script:DetailOpen) {
        $g.Stroke = $script:UI.BtnInspect.FindResource('Accent')
        $g.StrokeThickness = 1.8
    } else {
        $g.SetResourceReference([System.Windows.Shapes.Shape]::StrokeProperty, 'LabelSecondary')
        $g.StrokeThickness = 1.4
    }
}

function Add-DetSection {
    param([string]$Title)
    $tb = New-Object System.Windows.Controls.TextBlock
    $tb.Text       = $Title
    $tb.FontSize   = 11
    $tb.FontWeight = [System.Windows.FontWeights]::SemiBold
    $tb.Margin     = New-Object System.Windows.Thickness 0, 14, 0, 6
    $tb.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty, 'LabelSecondary')
    [void]$script:Det.DBody.Children.Add($tb)
}

function Add-DetRow {
    param([string]$Label, [string]$Value, [string]$Accent = $null)
    if ([string]::IsNullOrWhiteSpace($Value)) { return }
    $g = New-Object System.Windows.Controls.Grid
    $g.Margin = New-Object System.Windows.Thickness 0, 0, 0, 5
    $c1 = New-Object System.Windows.Controls.ColumnDefinition
    $c1.Width = New-Object System.Windows.GridLength 104
    $c2 = New-Object System.Windows.Controls.ColumnDefinition
    $c2.Width = New-Object System.Windows.GridLength (1, [System.Windows.GridUnitType]::Star)
    [void]$g.ColumnDefinitions.Add($c1); [void]$g.ColumnDefinitions.Add($c2)

    $l = New-Object System.Windows.Controls.TextBlock
    $l.Text = $Label; $l.FontSize = 11
    $l.TextWrapping = 'Wrap'
    $l.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty, 'LabelTertiary')
    [System.Windows.Controls.Grid]::SetColumn($l, 0)

    $v = New-Object System.Windows.Controls.TextBlock
    $v.Text = $Value; $v.FontSize = 11.5
    $v.TextWrapping = 'Wrap'
    $v.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty, ($(if ($Accent) { $Accent } else { 'Label' })))
    [System.Windows.Controls.Grid]::SetColumn($v, 1)

    [void]$g.Children.Add($l); [void]$g.Children.Add($v)
    [void]$script:Det.DBody.Children.Add($g)
}

function Show-Inspector {
    param($Row)
    $script:DetailRow = $Row
    if ($script:DetailOpen) { Fill-Details $Row }
}

function Fill-Details {
    param($Row)
    if ($null -eq $script:Det.DBody) { return }
    $script:Det.DBody.Children.Clear()
    if ($null -eq $Row) {
        $script:Det.DTitle.Text = 'Nessuna selezione'
        $script:Det.DSub.Text   = 'Seleziona un host nella tabella.'
        $script:Det.DBadgeBox.Visibility = 'Collapsed'
        return
    }
    $title = if ($Row.Hostname) { $Row.Hostname } elseif ($Row.NetBiosName) { $Row.NetBiosName } else { $Row.IP }
    $script:Det.DTitle.Text = $title
    $script:Det.DSub.Text   = (@($Row.IP, $Row.DeviceType) | Where-Object { $_ }) -join '  ·  '

    $script:Det.DBadge.Text = ($Row.Status).ToUpper()
    $script:Det.DBadgeBox.Visibility = if ($Row.Status) { 'Visible' } else { 'Collapsed' }
    $tone = if ($Row.StatusRank -le 1) { 'Green' } elseif ($Row.StatusRank -le 3) { 'Red' } else { 'LabelTertiary' }
    $script:Det.DBadge.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty, $tone)
    $script:Det.DBadgeBox.SetResourceReference([System.Windows.Controls.Border]::BorderBrushProperty, $tone)

    Add-DetSection 'Identita'
    Add-DetRow 'Indirizzo IP'   $Row.IP
    Add-DetRow 'Nome DNS'       $Row.Hostname
    Add-DetRow 'Nome NetBIOS'   $Row.NetBiosName
    Add-DetRow 'Nome mDNS'      $Row.MdnsName
    Add-DetRow 'Gruppo/Dominio' (@($Row.Workgroup, $Row.Domain) | Where-Object { $_ } | Select-Object -First 1)
    Add-DetRow 'Indirizzo MAC'  $Row.Mac
    Add-DetRow 'Produttore'     $Row.Vendor
    Add-DetRow 'Utente'         $Row.LoggedUser

    Add-DetSection 'Raggiungibilita'
    Add-DetRow 'Stato'          $Row.Status
    Add-DetRow 'Latenza'        $Row.RttMs
    Add-DetRow 'Perdita'        $Row.Loss
    Add-DetRow 'TTL'            $Row.Ttl
    Add-DetRow 'Ultima verifica' $Row.LastSeen
    if ($Row.ScanMs -gt 0) { Add-DetRow 'Durata analisi' ("$($Row.ScanMs) ms") }

    Add-DetSection 'Sistema'
    Add-DetRow 'Stima OS'       $Row.OsGuess
    Add-DetRow 'Tipo'           $Row.DeviceType
    Add-DetRow 'Sistema (WMI)'  $Row.WmiOs
    Add-DetRow 'Modello'        $Row.WmiModel
    Add-DetRow 'Seriale'        $Row.WmiSerial
    Add-DetRow 'CPU'            $Row.WmiCpu
    Add-DetRow 'Memoria'        $Row.WmiRam
    Add-DetRow 'Dischi'         $Row.WmiDisks
    Add-DetRow 'Uptime'         $Row.WmiUptime

    Add-DetSection 'Servizi'
    Add-DetRow 'Porte aperte'   $Row.OpenPorts
    Add-DetRow 'Servizi'        $Row.Services
    Add-DetRow 'Condivisioni'   $Row.Shares
    Add-DetRow 'RDP'            $Row.RdpInfo

    Add-DetSection 'Applicazioni'
    Add-DetRow 'Titolo web'     $Row.HttpTitle
    Add-DetRow 'Server HTTP'    $Row.HttpServer
    Add-DetRow 'SSH'            $Row.SshBanner
    Add-DetRow 'FTP'            $Row.FtpBanner
    Add-DetRow 'SMTP'           $Row.SmtpBanner
    Add-DetRow 'UPnP'           ((@($Row.UpnpDevice, $Row.UpnpServer) | Where-Object { $_ }) -join ' / ')

    Add-DetSection 'Certificato TLS'
    Add-DetRow 'Soggetto'       $Row.TlsSubject
    Add-DetRow 'Emittente'      $Row.TlsIssuer
    Add-DetRow 'Scadenza'       $Row.TlsExpiry

    Add-DetSection 'SNMP'
    Add-DetRow 'Nome'           $Row.SnmpName
    Add-DetRow 'Descrizione'    $Row.SnmpDescr
    Add-DetRow 'Posizione'      $Row.SnmpLocation
    Add-DetRow 'Contatto'       $Row.SnmpContact
    Add-DetRow 'Uptime'         $Row.SnmpUptime

    if ($Row.Notes) {
        Add-DetSection 'Rilievi'
        Add-DetRow 'Note' $Row.Notes 'Orange'
    }
}

function Update-ScanUi {
    $tot  = $script:ScanTotal
    $done = $script:ScanDone
    if ($script:ScanActive) {
        $script:UI.ScanProgress.Visibility = 'Visible'
        $script:UI.ScanProgress.Value = if ($tot -gt 0) { [Math]::Min(100, ($done / [double]$tot) * 100) } else { 0 }
        $script:UI.BtnStop.IsEnabled     = $true
        $script:UI.BtnScanRange.IsEnabled = $false
        $script:UI.BtnScanNote.IsEnabled  = $false
        $eng = if ($script:ScanMode -eq 'parallel') { 'parallelo' } else { 'pool' }
        Set-Status ('Analisi in corso ({0}, {1} thread): {2} di {3} host...' -f $eng, $script:Settings.MaxThreads, $done, $tot)
    } else {
        $script:UI.ScanProgress.Visibility = 'Collapsed'
        $script:UI.BtnStop.IsEnabled      = $false
        $script:UI.BtnScanRange.IsEnabled = $true
        $script:UI.BtnScanNote.IsEnabled  = $true
    }
    Update-CountText
    Refresh-SideList
}

function Update-MonitorUi {
    if ($null -eq $script:UI.MonGlyph) { return }
    $t = $script:Tokens[$script:Settings.Theme]
    if ($script:Settings.MonitorEnabled) {
        $script:UI.MonGlyph.Stroke = New-Brush $t.Green
        $script:UI.MonLbl.Text = ('{0}s' -f $script:Settings.MonitorIntervalSec)
    } else {
        $script:UI.MonGlyph.Stroke = New-Brush $t.Gray
        $script:UI.MonLbl.Text = 'fermo'
    }
}

$script:FilterFields = @{
    ip = 'IP'; nome = 'Hostname'; host = 'Hostname'; netbios = 'NetBiosName'; mac = 'Mac'
    produttore = 'Vendor'; vendor = 'Vendor'; os = 'OsGuess'; sistema = 'OsGuess'
    tipo = 'DeviceType'; porta = 'OpenPorts'; porte = 'OpenPorts'; web = 'HttpTitle'
    gruppo = 'Workgroup'; nota = 'Notes'; note = 'Notes'; stato = 'Status'
}

function Test-RowMatch {
    param($Row, [string[]]$Terms)
    foreach ($term in $Terms) {
        $field = $null
        $value = $term
        if ($term -match '^([A-Za-z]+):(.*)$') {
            $k = $Matches[1].ToLower()
            if ($script:FilterFields.ContainsKey($k)) { $field = $script:FilterFields[$k]; $value = $Matches[2] }
        }
        if (-not $value) { continue }
        $hay = if ($field) {
            "$($Row.$field)"
        } else {
            "$($Row.IP) $($Row.Hostname) $($Row.NetBiosName) $($Row.Mac) $($Row.Vendor) $($Row.OsGuess) $($Row.DeviceType) $($Row.OpenPorts) $($Row.HttpTitle) $($Row.Workgroup) $($Row.Notes) $($Row.SnmpDescr)"
        }
        if ($hay.IndexOf($value, [StringComparison]::OrdinalIgnoreCase) -lt 0) { return $false }
    }
    return $true
}

function Get-StatusFilter {
    $sel = $null
    if ($script:UI.CmbStatus) { $sel = $script:UI.CmbStatus.SelectedItem }
    if ($null -eq $sel) { return 'tutti' }
    return [string]$sel.Tag
}

function Apply-GridFilter {
    $view = [System.Windows.Data.CollectionViewSource]::GetDefaultView($script:UI.NetGrid.ItemsSource)
    if ($null -eq $view) { return }
    $needle = ''
    if ($script:UI.GridFilter) { $needle = $script:UI.GridFilter.Text.Trim() }
    $terms  = @($needle -split '\s+' | Where-Object { $_ })
    $status = Get-StatusFilter

    if ($terms.Count -eq 0 -and $status -eq 'tutti') {
        $view.Filter = $null
        Update-CountText
        return
    }
    $match = ${function:Test-RowMatch}
    $view.Filter = [Predicate[object]]({
        param($o)
        switch ($status) {
            'attivi'  { if ($o.StatusRank -gt 1) { return $false } }
            'spenti'  { if ($o.StatusRank -le 1) { return $false } }
            'porte'   { if ([int]$o.PortCount -le 0) { return $false } }
            'rilievi' { if (-not $o.Notes) { return $false } }
        }
        if ($terms.Count -eq 0) { return $true }
        return (& $match $o $terms)
    }.GetNewClosure())
    Update-CountText
}

function Update-CountText {
    if ($null -eq $script:UI.CountText) { return }
    $up  = @($script:Rows | Where-Object { $_.StatusRank -le 1 }).Count
    $txt = '{0} host · {1} attivi' -f $script:Rows.Count, $up
    $view = [System.Windows.Data.CollectionViewSource]::GetDefaultView($script:UI.NetGrid.ItemsSource)
    if ($view -and $view.Filter) { $txt += ' · {0} mostrati' -f @($view).Count }
    $script:UI.CountText.Text = $txt
}

function Export-ScanCsv {
    if ($script:Rows.Count -eq 0) { Set-Status 'Nessun risultato da esportare.'; return }
    $dlg = New-Object Microsoft.Win32.SaveFileDialog
    $dlg.Filter   = 'CSV (*.csv)|*.csv|Tutti i file (*.*)|*.*'
    $dlg.FileName = ('ducknote-scan-{0}.csv' -f (Get-Date).ToString('yyyyMMdd-HHmm'))
    if ($dlg.ShowDialog() -ne $true) { return }
    try {
        $script:Rows | Select-Object IP, Status, Hostname, NetBiosName, Workgroup, Domain, Mac, Vendor,
            RttMs, Loss, Ttl, OsGuess, DeviceType, OpenPorts, Services, HttpTitle, HttpServer,
            TlsSubject, TlsIssuer, TlsExpiry, SshBanner, FtpBanner, SmtpBanner,
            SnmpName, SnmpDescr, SnmpLocation, SnmpContact, SnmpUptime,
            MdnsName, UpnpDevice, UpnpServer, Shares, LoggedUser,
            WmiOs, WmiModel, WmiSerial, WmiCpu, WmiRam, WmiDisks, WmiUptime, Notes, LastSeen |
            Export-Csv -Path $dlg.FileName -NoTypeInformation -Encoding UTF8 -Delimiter ';'
        Set-Status ('Esportati {0} host in {1}.' -f $script:Rows.Count, $dlg.FileName)
    } catch { Set-Status "Esportazione non riuscita: $_" }
}

function Send-ScanToNote {
    $sel = @($script:UI.NetGrid.SelectedItems)
    if ($sel.Count -eq 0) { $sel = @($script:Rows | Where-Object { $_.StatusRank -le 1 }) }
    if ($sel.Count -eq 0) { Set-Status 'Nessun host da inviare.'; return }
    $data = New-Object System.Collections.Generic.List[object]
    [void]$data.Add(@('IP', 'Nome', 'MAC', 'Produttore', 'Sistema', 'Porte'))
    foreach ($r in $sel) {
        $nm = if ($r.Hostname) { $r.Hostname } elseif ($r.NetBiosName) { $r.NetBiosName } else { '' }
        [void]$data.Add(@($r.IP, $nm, $r.Mac, $r.Vendor, $r.OsGuess, $r.OpenPorts))
    }
    $arr = @()
    foreach ($d in $data) { $arr += ,([string[]]$d) }
    Append-ToNote @('', ('## Scansione ' + (Get-Date).ToString('dd/MM/yyyy HH:mm')))
    $last = $script:Editor.Document.Blocks.LastBlock
    if ($last) { $script:Editor.CaretPosition = $last.ContentEnd }
    Insert-EditorTable -Rows $arr.Count -Cols 6 -Header $true -Data $arr
    $script:UI.TabNote.IsChecked = $true
    Set-Status ('{0} host aggiunti alla nota.' -f $sel.Count)
}

$script:Ducks     = New-Object System.Collections.Generic.List[object]
$script:DuckTimer = $null
$script:DuckBrush = New-Object System.Windows.Media.SolidColorBrush
$script:DuckGeo   = $null
$script:DuckMask  = $null
$script:DuckRnd   = New-Object System.Random

function New-DuckBitmapFromStream {
    param([System.IO.Stream]$Stream)
    $bmp = New-Object System.Windows.Media.Imaging.BitmapImage
    $bmp.BeginInit()
    $bmp.CacheOption    = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $bmp.CreateOptions  = [System.Windows.Media.Imaging.BitmapCreateOptions]::IgnoreColorProfile
    $bmp.StreamSource   = $Stream
    $bmp.EndInit()
    $bmp.Freeze()
    return $bmp
}

function Initialize-DuckImage {
    $script:DuckPngFile = $null
    $script:DuckBitmap  = $null

    foreach ($f in $script:DuckPngCandidates) {
        if (-not $f -or -not (Test-Path -LiteralPath $f)) { continue }
        try {
            $fs = [IO.File]::OpenRead($f)
            try { $script:DuckBitmap = New-DuckBitmapFromStream $fs } finally { $fs.Dispose() }
            $script:DuckPngFile = $f
            break
        } catch { $script:DuckBitmap = $null }
    }

    if ($null -eq $script:DuckBitmap) {
        try {
            $bytes = [Convert]::FromBase64String(($script:DuckPngBase64 -replace '\s', ''))
            $ms = New-Object System.IO.MemoryStream (,$bytes)
            try { $script:DuckBitmap = New-DuckBitmapFromStream $ms } finally { $ms.Dispose() }
            $script:DuckPngFile = '(incorporata)'
        } catch { $script:DuckBitmap = $null }
    }

    if ($null -eq $script:DuckBitmap) { return $false }
    if ($script:DuckBitmap.PixelWidth -gt 0 -and $script:DuckBitmap.PixelHeight -gt 0) {
        $script:DuckW = [double]$script:DuckBitmap.PixelWidth
        $script:DuckH = [double]$script:DuckBitmap.PixelHeight
    }
    return $true
}

$script:IconeNative = @()

function New-DuckHicon {
    param([int]$Lato)
    if ($null -eq $script:DuckBitmap) { return [IntPtr]::Zero }
    try {
        $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        [void]$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($script:DuckBitmap))
        $ms = New-Object System.IO.MemoryStream
        try {
            $enc.Save($ms)
            $ms.Position = 0
            $originale = New-Object System.Drawing.Bitmap $ms
            try {
                $tela = New-Object System.Drawing.Bitmap $Lato, $Lato
                $g = [System.Drawing.Graphics]::FromImage($tela)
                try {
                    $g.InterpolationMode = 'HighQualityBicubic'
                    $g.SmoothingMode     = 'HighQuality'
                    $g.PixelOffsetMode   = 'HighQuality'
                    $g.Clear([System.Drawing.Color]::Transparent)
                    $g.DrawImage($originale, 0, 0, $Lato, $Lato)
                } finally { $g.Dispose() }
                $h = $tela.GetHicon()
                $tela.Dispose()
                return $h
            } finally { $originale.Dispose() }
        } finally { $ms.Dispose() }
    } catch { return [IntPtr]::Zero }
}

# La finestra porta la papera anche dove WPF non arriva: barra delle
# applicazioni e Alt+Tab leggono le icone native dell'HWND.
function Set-DuckIcon {
    param($Finestra)
    if ($null -eq $Finestra) { return }
    try { $Finestra.Icon = $script:DuckBitmap } catch {}
    $hwnd = (New-Object System.Windows.Interop.WindowInteropHelper $Finestra).Handle
    if ($hwnd -eq [IntPtr]::Zero) { return }
    $piccola = New-DuckHicon 16
    $grande  = New-DuckHicon 32
    [DuckNative.Shell]::SetWindowIcon($hwnd, $piccola, $grande)
    $script:IconeNative += @($piccola, $grande)
}

function Clear-DuckIcons {
    foreach ($h in $script:IconeNative) { [DuckNative.Shell]::ReleaseIcon($h) }
    $script:IconeNative = @()
}

function Apply-DuckLogo {
    if ($script:UI.VeilImg) {
        if ($null -ne $script:DuckBitmap) {
            $script:UI.VeilImg.Source     = $script:DuckBitmap
            $script:UI.VeilImg.Visibility = 'Visible'
            $script:UI.VeilVec.Visibility = 'Collapsed'
        } else {
            $script:UI.VeilDuck.Data = [System.Windows.Media.Geometry]::Parse($script:DuckGeometry)
        }
    }
    if ($null -eq $script:UI.LogoImage) { return }
    if ($null -ne $script:DuckBitmap) {
        $script:UI.LogoImage.Source     = $script:DuckBitmap
        $script:UI.LogoImage.Visibility = 'Visible'
        $script:UI.LogoVector.Visibility = 'Collapsed'
        Set-DuckIcon $script:Window
    } else {
        $script:UI.LogoImage.Visibility  = 'Collapsed'
        $script:UI.LogoVector.Visibility = 'Visible'
    }
}

function Update-DuckBrush {
    $t = $script:Tokens[$script:Settings.Theme]
    try { $script:DuckBrush.Color = ConvertFrom-Hex $t.DuckOrange } catch {}
    $op = [double]$script:Settings.DuckOpacity / 100.0
    foreach ($d in $script:Ducks) { $d.Path.Opacity = $op }
}

function Build-Ducks {
    $cv = $script:UI.DuckLayer
    if ($null -eq $cv) { return }
    $cv.Children.Clear()
    $script:Ducks.Clear()
    if (-not $script:Settings.DuckBackground) { return }

    $useImg = ($null -ne $script:DuckBitmap)
    if ($useImg) {
        if ($null -eq $script:DuckMask) {
            $script:DuckMask = New-Object System.Windows.Media.ImageBrush $script:DuckBitmap
            $script:DuckMask.Stretch = [System.Windows.Media.Stretch]::Fill
            $script:DuckMask.Freeze()
        }
    } elseif ($null -eq $script:DuckGeo) {
        $script:DuckGeo = [System.Windows.Media.Geometry]::Parse($script:DuckGeometry)
        $script:DuckGeo.Freeze()
    }
    $w = [Math]::Max(400.0, $cv.ActualWidth)
    $h = [Math]::Max(300.0, $cv.ActualHeight)
    $op = [double]$script:Settings.DuckOpacity / 100.0
    $cx = $script:DuckW / 2.0
    $cy = $script:DuckH / 2.0

    for ($i = 0; $i -lt [int]$script:Settings.DuckCount; $i++) {
        $size = $script:DuckRnd.NextDouble() * 180 + 180
        $x = $script:DuckRnd.NextDouble() * $w
        $y = $script:DuckRnd.NextDouble() * $h
        for ($try = 0; $try -lt 50; $try++) {
            $ok = $true
            foreach ($d in $script:Ducks) {
                if ([Math]::Sqrt([Math]::Pow($x - $d.X, 2) + [Math]::Pow($y - $d.Y, 2)) -lt (($size + $d.Size) / 2)) { $ok = $false; break }
            }
            if ($ok) { break }
            $x = $script:DuckRnd.NextDouble() * $w
            $y = $script:DuckRnd.NextDouble() * $h
        }
        $ang   = $script:DuckRnd.NextDouble() * [Math]::PI * 2
        $speed = 0.4 + $script:DuckRnd.NextDouble() * 0.7

        $sc = New-Object System.Windows.Media.ScaleTransform
        $sc.CenterX = $cx; $sc.CenterY = $cy
        $flipInit = $(if ($script:DuckRnd.NextDouble() -gt 0.5) { 1.0 } else { -1.0 })
        $ro = New-Object System.Windows.Media.RotateTransform
        $ro.CenterX = $cx; $ro.CenterY = $cy
        $tr = New-Object System.Windows.Media.TranslateTransform
        $tg = New-Object System.Windows.Media.TransformGroup
        [void]$tg.Children.Add($sc)
        [void]$tg.Children.Add($ro)
        [void]$tg.Children.Add($tr)

        if ($useImg) {
            $path = New-Object System.Windows.Shapes.Rectangle
            $path.Width  = $script:DuckW
            $path.Height = $script:DuckH
            $path.OpacityMask = $script:DuckMask
        } else {
            $path = New-Object System.Windows.Shapes.Path
            $path.Data = $script:DuckGeo
        }
        $path.Fill            = $script:DuckBrush
        $path.Opacity         = $op
        $path.RenderTransform = $tg
        $path.IsHitTestVisible = $false
        [void]$cv.Children.Add($path)

        [void]$script:Ducks.Add([pscustomobject]@{
            X = $x; Y = $y; Size = $size
            VX = [Math]::Cos($ang) * $speed
            VY = [Math]::Sin($ang) * $speed
            Wobble      = $script:DuckRnd.NextDouble() * [Math]::PI * 2
            WobbleSpeed = 0.008 + $script:DuckRnd.NextDouble() * 0.008
            Flip        = $flipInit
            Path = $path; S = $sc; R = $ro; T = $tr
        })
    }
    Update-DuckBrush
}

function Step-Ducks {
    $cv = $script:UI.DuckLayer
    if ($null -eq $cv -or $script:Ducks.Count -eq 0) { return }
    $w = $cv.ActualWidth
    $h = $cv.ActualHeight
    if ($w -le 0 -or $h -le 0) { return }
    $cx = $script:DuckW / 2.0
    $cy = $script:DuckH / 2.0

    for ($i = 0; $i -lt $script:Ducks.Count; $i++) {
        for ($j = $i + 1; $j -lt $script:Ducks.Count; $j++) {
            $a = $script:Ducks[$i]; $b = $script:Ducks[$j]
            $dx = $b.X - $a.X; $dy = $b.Y - $a.Y
            $dist = [Math]::Sqrt($dx * $dx + $dy * $dy)
            $min  = ($a.Size + $b.Size) / 2.2
            if ($dist -lt $min -and $dist -gt 0) {
                $nx = $dx / $dist; $ny = $dy / $dist
                $ov = ($min - $dist) / 2
                $a.X -= $nx * $ov; $a.Y -= $ny * $ov
                $b.X += $nx * $ov; $b.Y += $ny * $ov
                $dvn = ($a.VX - $b.VX) * $nx + ($a.VY - $b.VY) * $ny
                if ($dvn -gt 0) {
                    $a.VX -= $dvn * $nx * 0.8; $a.VY -= $dvn * $ny * 0.8
                    $b.VX += $dvn * $nx * 0.8; $b.VY += $dvn * $ny * 0.8
                }
            }
        }
    }

    foreach ($d in $script:Ducks) {
        $d.X += $d.VX
        $d.Y += $d.VY
        $d.Wobble += $d.WobbleSpeed
        if ($d.X -lt -$d.Size)      { $d.X = $w + $d.Size }
        if ($d.X -gt $w + $d.Size)  { $d.X = -$d.Size }
        if ($d.Y -lt -$d.Size)      { $d.Y = $h + $d.Size }
        if ($d.Y -gt $h + $d.Size)  { $d.Y = -$d.Size }
        $k = $d.Size / $script:DuckW
        $d.S.ScaleX = $k * $d.Flip
        $d.S.ScaleY = $k
        $d.R.Angle  = [Math]::Sin($d.Wobble) * 1.72
        $d.T.X = $d.X - $cx
        $d.T.Y = $d.Y - $cy
    }
}

function Start-DuckBackground {
    if ($null -eq $script:DuckTimer) {
        $script:DuckTimer = New-Object System.Windows.Threading.DispatcherTimer
        $script:DuckTimer.Interval = [TimeSpan]::FromMilliseconds(40)
        $script:DuckTimer.Add_Tick({ Step-Ducks })
    }
    Build-Ducks
    if ($script:Settings.DuckBackground -and $script:Ducks.Count -gt 0) {
        $script:DuckTimer.Start()
    } else {
        $script:DuckTimer.Stop()
    }
}

function Stop-DuckBackground {
    if ($script:DuckTimer) { $script:DuckTimer.Stop() }
    if ($script:UI.DuckLayer) { $script:UI.DuckLayer.Children.Clear() }
    $script:Ducks.Clear()
}

$script:DucksHeld = $false

function Suspend-Ducks {
    if ($script:DucksHeld -or $null -eq $script:DuckTimer) { return }
    if (-not $script:DuckTimer.IsEnabled) { return }
    $script:DuckTimer.Stop()
    $script:DucksHeld = $true
}

function Resume-Ducks {
    if (-not $script:DucksHeld) { return }
    $script:DucksHeld = $false
    if ($script:Settings.DuckBackground -and $script:Ducks.Count -gt 0) { $script:DuckTimer.Start() }
}

$script:PrefsXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Impostazioni" Width="540" Height="640"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False"
        WindowStyle="None" AllowsTransparency="True" UseLayoutRounding="True"
        FontFamily="SF Pro Text, Segoe UI Variable Text, Segoe UI" FontSize="13"
        Background="Transparent">
  <Border x:Name="PRoot" CornerRadius="14" Background="{DynamicResource BgSidebarGlass}"
          BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1"
          RenderTransformOrigin="0.5,0.45" Opacity="0">
  <Border.RenderTransform>
    <TransformGroup>
      <ScaleTransform x:Name="PScale" ScaleX="0.96" ScaleY="0.96"/>
      <TranslateTransform x:Name="PMove" Y="12"/>
    </TransformGroup>
  </Border.RenderTransform>
  <Grid Margin="0">
    <Grid.RowDefinitions>
      <RowDefinition Height="44"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <Border x:Name="PTitleBar" Grid.Row="0" Background="{DynamicResource BgToolbarGlass}" CornerRadius="13,13,0,0">
      <Grid>
        <Button x:Name="PClose" Style="{DynamicResource TrafficBtn}" Background="{DynamicResource TLClose}"
                HorizontalAlignment="Left" VerticalAlignment="Center" Margin="14,0,0,0" ToolTip="Chiudi">
          <Path Data="M 0,0 L 6,6 M 6,0 L 0,6" Stroke="#5A1416" StrokeThickness="1.2" Stretch="Uniform"
                StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
        </Button>
        <TextBlock Text="Impostazioni" FontSize="13" FontWeight="SemiBold" HorizontalAlignment="Center"
                   VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
      </Grid>
    </Border>

    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" Padding="22,18,18,10">
      <StackPanel>

        <TextBlock Text="Aspetto" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource LabelSecondary}" Margin="0,0,0,8"/>
        <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Padding="15,13" Margin="0,0,0,18"
                BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
          <StackPanel>
            <CheckBox x:Name="PSysTheme" Content="Segui l'aspetto di sistema" Margin="0,0,0,10"/>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
              <TextBlock Text="Tema" Width="150" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <ComboBox x:Name="PTheme" Width="150" Height="26">
                <ComboBoxItem Content="Chiaro" Tag="light"/>
                <ComboBoxItem Content="Scuro"  Tag="dark"/>
              </ComboBox>
            </StackPanel>
            <CheckBox x:Name="PLiveFmt" Content="Formattazione Markdown dal vivo" Margin="0,0,0,10"/>
            <CheckBox x:Name="PDucks" Content="Papere animate sullo sfondo" Margin="0,0,0,10"/>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
              <TextBlock Text="Numero di papere" Width="150" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <TextBox x:Name="PDuckN" Style="{DynamicResource Field}" Width="90" TextAlignment="Right"/>
            </StackPanel>
            <StackPanel Orientation="Horizontal">
              <TextBlock Text="Intensita' (%)" Width="150" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <TextBox x:Name="PDuckOp" Style="{DynamicResource Field}" Width="90" TextAlignment="Right"/>
            </StackPanel>
          </StackPanel>
        </Border>

        <TextBlock Text="Nota" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource LabelSecondary}" Margin="0,0,0,8"/>
        <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Padding="15,13" Margin="0,0,0,18"
                BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
          <StackPanel>
            <CheckBox x:Name="PAutosave" Content="Salvataggio automatico" Margin="0,0,0,10"/>
            <StackPanel Orientation="Horizontal">
              <TextBlock Text="Attesa prima di salvare (ms)" Width="220" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <TextBox x:Name="PAutoMs" Style="{DynamicResource Field}" Width="90" TextAlignment="Right"/>
            </StackPanel>
          </StackPanel>
        </Border>

        <TextBlock Text="Monitoraggio" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource LabelSecondary}" Margin="0,0,0,8"/>
        <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Padding="15,13" Margin="0,0,0,18"
                BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
          <StackPanel>
            <CheckBox x:Name="PMonitor" Content="Controlla periodicamente gli host della nota" Margin="0,0,0,10"/>
            <StackPanel Orientation="Horizontal">
              <TextBlock Text="Intervallo (secondi)" Width="220" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <TextBox x:Name="PMonSec" Style="{DynamicResource Field}" Width="90" TextAlignment="Right"/>
            </StackPanel>
          </StackPanel>
        </Border>

        <TextBlock Text="Scansione" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource LabelSecondary}" Margin="0,0,0,8"/>
        <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Padding="15,13" Margin="0,0,0,18"
                BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
          <StackPanel>
            <CheckBox x:Name="PParallel" Content="Motore parallelo di PowerShell 7" Margin="0,0,0,4"/>
            <TextBlock x:Name="PParallelInfo" FontSize="11" TextWrapping="Wrap" Margin="22,0,0,12"
                       Foreground="{DynamicResource LabelTertiary}"/>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,9">
              <TextBlock Text="Analisi simultanee" Width="220" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <TextBox x:Name="PThreads" Style="{DynamicResource Field}" Width="90" TextAlignment="Right"/>
            </StackPanel>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,9">
              <TextBlock Text="Ping per host" Width="220" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <TextBox x:Name="PPingN" Style="{DynamicResource Field}" Width="90" TextAlignment="Right"/>
            </StackPanel>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,9">
              <TextBlock Text="Timeout ping (ms)" Width="220" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <TextBox x:Name="PPingMs" Style="{DynamicResource Field}" Width="90" TextAlignment="Right"/>
            </StackPanel>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,9">
              <TextBlock Text="Timeout porta (ms)" Width="220" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
              <TextBox x:Name="PPortMs" Style="{DynamicResource Field}" Width="90" TextAlignment="Right"/>
            </StackPanel>
            <TextBlock Text="Porte da verificare" Foreground="{DynamicResource Label}" Margin="0,4,0,5"/>
            <TextBox x:Name="PPorts" Style="{DynamicResource Field}" Height="52" TextWrapping="Wrap"
                     AcceptsReturn="True" VerticalContentAlignment="Top" Padding="8,5"/>
            <CheckBox x:Name="PDead" Content="Analizza anche host che non rispondono al ping" Margin="0,10,0,0"/>
          </StackPanel>
        </Border>

        <TextBlock Text="Raccolta informazioni" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource LabelSecondary}" Margin="0,0,0,8"/>
        <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Padding="15,13" Margin="0,0,0,18"
                BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
          <StackPanel>
            <CheckBox x:Name="PDns"     Content="Nome host via DNS inverso" Margin="0,0,0,8"/>
            <StackPanel Orientation="Horizontal" Margin="22,0,0,10">
              <TextBlock Text="Server DNS" Width="100" VerticalAlignment="Center" Foreground="{DynamicResource LabelSecondary}" FontSize="12"/>
              <TextBox x:Name="PDnsServer" Style="{DynamicResource Field}" Width="140"/>
              <TextBlock Text="vuoto = quello di sistema" VerticalAlignment="Center" Margin="10,0,0,0"
                         FontSize="11" Foreground="{DynamicResource LabelTertiary}"/>
            </StackPanel>
            <CheckBox x:Name="PNetBios" Content="Nome, gruppo e MAC via NetBIOS" Margin="0,0,0,8"/>
            <CheckBox x:Name="PMdns"    Content="Nome via mDNS (Bonjour)" Margin="0,0,0,8"/>
            <CheckBox x:Name="PSsdp"    Content="Descrizione dispositivo via SSDP/UPnP" Margin="0,0,0,8"/>
            <CheckBox x:Name="PBanner"  Content="Banner dei servizi, titolo web e certificato TLS" Margin="0,0,0,8"/>
            <CheckBox x:Name="PSnmp"    Content="Interrogazione SNMP v2c" Margin="0,0,0,8"/>
            <StackPanel Orientation="Horizontal" Margin="22,0,0,10">
              <TextBlock Text="Community" Width="100" VerticalAlignment="Center" Foreground="{DynamicResource LabelSecondary}" FontSize="12"/>
              <TextBox x:Name="PCommunity" Style="{DynamicResource Field}" Width="140"/>
            </StackPanel>
            <CheckBox x:Name="PShares"  Content="Elenco condivisioni SMB (piu' lento)" Margin="0,0,0,8"/>
            <StackPanel Orientation="Horizontal" Margin="0,10,0,10">
              <Button x:Name="POui" Style="{DynamicResource QuietBtn}" Content="Aggiorna database produttori (IEEE)"/>
              <TextBlock x:Name="POuiInfo" VerticalAlignment="Center" Margin="10,0,0,0" FontSize="11"
                         Foreground="{DynamicResource LabelSecondary}"/>
            </StackPanel>
            <CheckBox x:Name="PWmi"     Content="Inventario via WMI/WinRM (richiede permessi)"/>
            <TextBlock Text="WMI usa le credenziali dell'utente corrente. Su host non di dominio serve un account locale abilitato."
                       FontSize="11" TextWrapping="Wrap" Margin="22,6,0,0" Foreground="{DynamicResource LabelTertiary}"/>
          </StackPanel>
        </Border>

        <TextBlock Text="Sicurezza" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource LabelSecondary}" Margin="0,0,0,8"/>
        <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Padding="15,13" Margin="0,0,0,18"
                BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
          <StackPanel>
            <StackPanel Orientation="Horizontal">
              <Ellipse x:Name="PSecDot" Width="9" Height="9" VerticalAlignment="Center" Margin="0,0,8,0"/>
              <TextBlock x:Name="PSecState" FontWeight="SemiBold" VerticalAlignment="Center"
                         Foreground="{DynamicResource Label}"/>
            </StackPanel>
            <TextBlock x:Name="PSecInfo" FontSize="11" TextWrapping="Wrap" Margin="17,6,0,0"
                       Foreground="{DynamicResource LabelTertiary}"/>
            <StackPanel Orientation="Horizontal" Margin="17,12,0,0">
              <TextBlock Text="Blocca dopo (minuti)" Width="203" VerticalAlignment="Center"
                         Foreground="{DynamicResource Label}"/>
              <TextBox x:Name="PLockMin" Style="{DynamicResource Field}" Width="90" TextAlignment="Right"/>
            </StackPanel>
            <TextBlock x:Name="PLockInfo" FontSize="11" TextWrapping="Wrap" Margin="17,5,0,0"
                       Foreground="{DynamicResource LabelTertiary}"
                       Text="Senza tocchi a tastiera e mouse per questo tempo, la chiave lascia la memoria e serve di nuovo la password. 0 non blocca mai."/>
            <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
              <Button x:Name="PSecOn"     Style="{DynamicResource PrimaryBtn}" Content="Attiva cifratura" Margin="0,0,8,0"/>
              <Button x:Name="PSecPwd"    Style="{DynamicResource QuietBtn}"   Content="Cambia password" Margin="0,0,8,0"/>
              <Button x:Name="PSecOff"    Style="{DynamicResource QuietBtn}"   Content="Disattiva"/>
            </StackPanel>
          </StackPanel>
        </Border>

        <TextBlock Text="Impronta del codice" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource LabelSecondary}" Margin="0,0,0,8"/>
        <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Padding="15,13" Margin="0,0,0,18"
                BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
          <StackPanel>
            <TextBlock Text="SHA-256 dello script in esecuzione, da confrontare con le somme pubblicate con la release."
                       FontSize="11" TextWrapping="Wrap" Foreground="{DynamicResource LabelTertiary}"/>
            <TextBox x:Name="PFinger" Style="{DynamicResource Field}" Margin="0,8,0,0" Height="46"
                     IsReadOnly="True" TextWrapping="Wrap" VerticalContentAlignment="Top" Padding="8,5"
                     FontFamily="SF Mono, Cascadia Mono, Consolas" FontSize="11.5"/>
            <TextBlock x:Name="PFingerPath" FontSize="10.5" TextWrapping="Wrap" Margin="2,6,0,0"
                       FontFamily="SF Mono, Cascadia Mono, Consolas" Foreground="{DynamicResource LabelTertiary}"/>
          </StackPanel>
        </Border>

        <TextBlock Text="Disinstallazione" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource LabelSecondary}" Margin="0,0,0,8"/>
        <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Padding="15,13" Margin="0,0,0,18"
                BorderBrush="{DynamicResource AccentBorder}" BorderThickness="1">
          <StackPanel>
            <TextBlock Text="Cancella nota, backup, impostazioni, scansioni e chiave: tutto il contenuto di %APPDATA%\DuckNote."
                       FontSize="12" TextWrapping="Wrap" Foreground="{DynamicResource Label}"/>
            <TextBlock Text="I byte dei file vengono sovrascritti prima della cancellazione. L'operazione non si annulla."
                       FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0" Foreground="{DynamicResource LabelTertiary}"/>
            <Button x:Name="PWipe" Style="{DynamicResource QuietBtn}" Content="Cancella tutti i dati..."
                    HorizontalAlignment="Left" Margin="0,12,0,0" Foreground="{DynamicResource Red}"/>
          </StackPanel>
        </Border>

        <TextBlock x:Name="PError" Foreground="{DynamicResource Red}" FontSize="12" TextWrapping="Wrap" Margin="0,0,0,4"/>
      </StackPanel>
    </ScrollViewer>

    <Border Grid.Row="2" Background="{DynamicResource BgToolbarGlass}" Padding="18,12" CornerRadius="0,0,13,13">
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
        <Button x:Name="PCancel" Style="{DynamicResource QuietBtn}" Content="Annulla" Margin="0,0,8,0"/>
        <Button x:Name="POk"     Style="{DynamicResource PrimaryBtn}" Content="Salva"/>
      </StackPanel>
    </Border>
  </Grid>
  </Border>
</Window>
'@

function Open-DialogAnimated {
    param($Root)
    if ($null -eq $Root) { return }
    $O  = [System.Windows.UIElement]::OpacityProperty
    $SX = [System.Windows.Media.ScaleTransform]::ScaleXProperty
    $SY = [System.Windows.Media.ScaleTransform]::ScaleYProperty
    $TY = [System.Windows.Media.TranslateTransform]::YProperty
    $sc = $Root.RenderTransform.Children[0]
    $mv = $Root.RenderTransform.Children[1]
    $ease = New-Ease 'Cubic' 'EaseOut'
    $Root.BeginAnimation($O,  (New-Anim 0 1 200 $ease))
    $sc.BeginAnimation($SX,   (New-Anim 0.96 1 260 $ease))
    $sc.BeginAnimation($SY,   (New-Anim 0.96 1 260 $ease))
    $mv.BeginAnimation($TY,   (New-Anim 12 0 260 $ease))
}

function Close-DialogAnimated {
    param($Dialog, $Root, [bool]$Result)
    if ($null -eq $Root) { $Dialog.DialogResult = $Result; $Dialog.Close(); return }
    if ($Dialog.Tag -eq 'chiudendo') { return }
    $Dialog.Tag = 'chiudendo'
    $O  = [System.Windows.UIElement]::OpacityProperty
    $SX = [System.Windows.Media.ScaleTransform]::ScaleXProperty
    $SY = [System.Windows.Media.ScaleTransform]::ScaleYProperty
    $sc = $Root.RenderTransform.Children[0]
    $ease = New-Ease 'Cubic' 'EaseIn'
    $fade = New-Anim $Root.Opacity 0 150 $ease
    $fade.Add_Completed({
        try { $Dialog.DialogResult = $Result } catch {}
        try { $Dialog.Close() } catch {}
    }.GetNewClosure())
    $sc.BeginAnimation($SX, (New-Anim 1 0.97 150 $ease))
    $sc.BeginAnimation($SY, (New-Anim 1 0.97 150 $ease))
    $Root.BeginAnimation($O, $fade)
}

function Test-DnsServer {
    param([string]$Server)
    if (-not $Server) { return }
    if (DN-DnsQuery -Server $Server -Name '1.1.1.1.in-addr.arpa' -Type 12 -TimeoutMs 1500) {
        Set-Status ('Server DNS {0}: risponde.' -f $Server)
        return
    }
    Set-Status ('Server DNS {0}: nessuna risposta. Rifiuta le query in chiaro o non e'' raggiungibile, i nomi resteranno vuoti.' -f $Server)
}

$script:Gate = @{}

$script:GateXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="DuckNote" Width="440" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen" ShowInTaskbar="True"
        WindowStyle="None" AllowsTransparency="True" UseLayoutRounding="True"
        SnapsToDevicePixels="True" TextOptions.TextFormattingMode="Ideal"
        FontFamily="SF Pro Text, Segoe UI Variable Text, Segoe UI" FontSize="13"
        Background="Transparent">
  <Window.Resources>
    <Style x:Key="GatePwd" TargetType="PasswordBox">
      <Setter Property="FontSize" Value="14"/>
      <Setter Property="Height" Value="36"/>
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Background" Value="{DynamicResource BgField}"/>
      <Setter Property="BorderBrush" Value="{DynamicResource BorderControl}"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Padding" Value="11,0"/>
      <Setter Property="CaretBrush" Value="{DynamicResource Label}"/>
      <Setter Property="PasswordChar" Value="&#x25CF;"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="PasswordBox">
            <Border x:Name="bd" CornerRadius="9" Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
              <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center"
                            Margin="{TemplateBinding Padding}"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsKeyboardFocused" Value="True">
                <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource Accent}"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter TargetName="bd" Property="Opacity" Value="0.45"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="GateLabel" TargetType="TextBlock">
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Foreground" Value="{DynamicResource LabelSecondary}"/>
      <Setter Property="Margin" Value="2,0,0,5"/>
    </Style>

    <Style x:Key="GateGo" TargetType="Button">
      <Setter Property="Foreground" Value="{DynamicResource AccentText}"/>
      <Setter Property="Background" Value="{DynamicResource Accent}"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Height" Value="34"/>
      <Setter Property="Padding" Value="20,0"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="b" CornerRadius="9" Background="{TemplateBinding Background}"
                    Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="b" Property="Opacity" Value="0.88"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="b" Property="Opacity" Value="0.72"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource BgPressed}"/>
                <Setter Property="Foreground" Value="{DynamicResource LabelTertiary}"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="GateQuiet" TargetType="Button">
      <Setter Property="Foreground" Value="{DynamicResource Label}"/>
      <Setter Property="Background" Value="{DynamicResource BgFieldAlt}"/>
      <Setter Property="Height" Value="34"/>
      <Setter Property="Padding" Value="16,0"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="b" CornerRadius="9" Background="{TemplateBinding Background}"
                    BorderBrush="{DynamicResource BorderControl}" BorderThickness="1"
                    Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource BgHover}"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="b" Property="Background" Value="{DynamicResource BgPressed}"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.4"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>

  <Border x:Name="GRoot" CornerRadius="16" Background="{DynamicResource Panel}"
          BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1"
          RenderTransformOrigin="0.5,0.45" Opacity="0">
    <Border.RenderTransform>
      <TransformGroup>
        <ScaleTransform x:Name="GScale" ScaleX="0.96" ScaleY="0.96"/>
        <TranslateTransform x:Name="GMove" Y="12"/>
      </TransformGroup>
    </Border.RenderTransform>

    <Grid>
      <Grid.RenderTransform>
        <TranslateTransform x:Name="GShake"/>
      </Grid.RenderTransform>

      <!-- velo di vetro: un filo di luce lungo il bordo superiore -->
      <Border CornerRadius="15,15,0,0" VerticalAlignment="Top" Height="1"
              Background="{DynamicResource EdgeHighlight}" Panel.ZIndex="2"/>

      <StackPanel>
        <Border x:Name="GTitleBar" Background="{DynamicResource BgToolbarGlass}" Height="40" CornerRadius="15,15,0,0">
          <Grid>
            <Button x:Name="GClose" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="14,0,0,0"
                    Width="12" Height="12" Cursor="Hand" ToolTip="Chiudi">
              <Button.Template>
                <ControlTemplate TargetType="Button">
                  <Grid>
                    <Ellipse x:Name="e" Fill="{DynamicResource TLClose}"/>
                    <Path x:Name="x" Data="M 0,0 L 6,6 M 6,0 L 0,6" Stroke="#5A1416" StrokeThickness="1.2"
                          Stretch="Uniform" Margin="3" Opacity="0"
                          StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="x" Property="Opacity" Value="1"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Button.Template>
            </Button>
            <TextBlock x:Name="GBarTitle" Text="DuckNote" FontSize="12" FontWeight="SemiBold"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Foreground="{DynamicResource Label}"/>
          </Grid>
        </Border>

        <StackPanel x:Name="GLoader">
          <StackPanel.RenderTransform>
            <TranslateTransform x:Name="GLoaderT"/>
          </StackPanel.RenderTransform>

        <Grid x:Name="GStage" Height="132" Margin="0,16,0,0">
          <Path x:Name="GRing1" Width="116" Height="116" HorizontalAlignment="Center" VerticalAlignment="Center"
                Data="M 58,6 A 52,52 0 1 1 6,58" Stroke="{DynamicResource Accent}" StrokeThickness="3"
                StrokeStartLineCap="Round" StrokeEndLineCap="Round" RenderTransformOrigin="0.5,0.5" Opacity="0.5">
            <Path.RenderTransform><RotateTransform x:Name="GRot1"/></Path.RenderTransform>
          </Path>
          <Path x:Name="GRing2" Width="92" Height="92" HorizontalAlignment="Center" VerticalAlignment="Center"
                Data="M 46,6 A 40,40 0 0 1 86,46" Stroke="{DynamicResource Blue}" StrokeThickness="3"
                StrokeStartLineCap="Round" StrokeEndLineCap="Round" RenderTransformOrigin="0.5,0.5" Opacity="0.45">
            <Path.RenderTransform><RotateTransform x:Name="GRot2"/></Path.RenderTransform>
          </Path>
          <Ellipse x:Name="GSeal" Width="116" Height="116" HorizontalAlignment="Center" VerticalAlignment="Center"
                   Stroke="{DynamicResource Green}" StrokeThickness="3" Opacity="0" RenderTransformOrigin="0.5,0.5">
            <Ellipse.RenderTransform><ScaleTransform x:Name="GSealS" ScaleX="0.6" ScaleY="0.6"/></Ellipse.RenderTransform>
          </Ellipse>

          <Grid x:Name="GDuckWrap" Width="58" Height="58" HorizontalAlignment="Center" VerticalAlignment="Center"
                RenderTransformOrigin="0.5,0.5">
            <Grid.RenderTransform>
              <TransformGroup>
                <ScaleTransform x:Name="GDuckS" ScaleX="1" ScaleY="1"/>
                <TranslateTransform x:Name="GDuckT"/>
              </TransformGroup>
            </Grid.RenderTransform>
            <Image x:Name="GDuckImg" Stretch="Uniform" Visibility="Collapsed"
                   RenderOptions.BitmapScalingMode="HighQuality"/>
            <Viewbox x:Name="GDuckVec" Stretch="Uniform">
              <Canvas Width="99" Height="100">
                <Path x:Name="GDuckBody" Fill="{DynamicResource Accent}"/>
                <Path x:Name="GDuckEye"  Fill="#1F2328"/>
              </Canvas>
            </Viewbox>
          </Grid>
        </Grid>

        <TextBlock x:Name="GWork" FontSize="12" TextWrapping="Wrap" Margin="34,14,34,0" LineHeight="17"
                   TextAlignment="Center" Foreground="{DynamicResource LabelSecondary}" Visibility="Collapsed"/>
        </StackPanel>

        <StackPanel x:Name="GBody">
          <StackPanel.RenderTransform>
            <TranslateTransform x:Name="GBodyT"/>
          </StackPanel.RenderTransform>

        <TextBlock x:Name="GTitle" FontSize="19" FontWeight="SemiBold" HorizontalAlignment="Center"
                   Margin="26,8,26,0" TextAlignment="Center" Foreground="{DynamicResource Label}"/>
        <TextBlock x:Name="GSub" FontSize="12.5" HorizontalAlignment="Center" TextWrapping="Wrap"
                   Margin="32,8,32,0" TextAlignment="Center" LineHeight="18"
                   Foreground="{DynamicResource Label}" Opacity="0.78"/>

        <Border x:Name="GCard" CornerRadius="12" Background="{DynamicResource SheetScrim}" Margin="26,16,26,0"
                Padding="15,13" BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
          <StackPanel x:Name="GForm">
            <TextBlock x:Name="GLbl1" Style="{DynamicResource GateLabel}" Text="Password"/>
            <PasswordBox x:Name="GPwd" Style="{DynamicResource GatePwd}"/>
            <TextBlock x:Name="GLbl2" Style="{DynamicResource GateLabel}" Text="Ripeti la password" Margin="2,11,0,5"/>
            <PasswordBox x:Name="GPwd2" Style="{DynamicResource GatePwd}"/>
            <Grid x:Name="GStrength" Margin="0,13,0,0">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
              </Grid.ColumnDefinitions>
              <Border Grid.Column="0" Height="5" CornerRadius="3" VerticalAlignment="Center"
                      Background="{DynamicResource BgFieldAlt}">
                <Border x:Name="GBar" Height="5" CornerRadius="3" Width="0" HorizontalAlignment="Left"
                        Background="{DynamicResource Red}"/>
              </Border>
              <TextBlock x:Name="GBarTxt" Grid.Column="1" FontSize="11" Margin="10,0,0,0" VerticalAlignment="Center"
                         FontWeight="SemiBold" Foreground="{DynamicResource LabelSecondary}"/>
            </Grid>
          </StackPanel>
        </Border>

        <TextBlock x:Name="GMsg" FontSize="12.5" FontWeight="SemiBold" TextWrapping="Wrap" Margin="30,13,30,0"
                   TextAlignment="Center" Foreground="{DynamicResource Red}" Visibility="Collapsed"/>

        </StackPanel>

        <Border x:Name="GFooter" Background="{DynamicResource BgToolbarGlass}" Padding="20,14" Margin="0,18,0,0"
                CornerRadius="0,0,15,15">
          <Grid>
            <Button x:Name="GLater" Style="{DynamicResource GateQuiet}" Content="Piu' tardi" HorizontalAlignment="Left"/>
            <Button x:Name="GGo"    Style="{DynamicResource GateGo}"    Content="Crea la chiave" HorizontalAlignment="Right"/>
          </Grid>
        </Border>
      </StackPanel>
    </Grid>
  </Border>
</Window>
'@

function New-Spin {
    param([int]$Ms, [double]$From = 0, [double]$To = 360)
    $a = New-Anim $From $To $Ms
    $a.RepeatBehavior = [System.Windows.Media.Animation.RepeatBehavior]::Forever
    return $a
}

function New-Pulse {
    param([double]$From, [double]$To, [int]$Ms)
    $a = New-Anim $From $To $Ms (New-Ease 'Cubic' 'EaseInOut')
    $a.AutoReverse    = $true
    $a.RepeatBehavior = [System.Windows.Media.Animation.RepeatBehavior]::Forever
    return $a
}

function New-Shake {
    param([int]$Ms = 460, [double]$Ampiezza = 12)
    $a = New-Object System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
    $a.Duration = New-Object System.Windows.Duration ([TimeSpan]::FromMilliseconds($Ms))
    $passi = @(@(0.0, 0.0), @(0.13, -$Ampiezza), @(0.30, $Ampiezza * 0.8),
               @(0.47, -$Ampiezza * 0.5), @(0.66, $Ampiezza * 0.3),
               @(0.84, -$Ampiezza * 0.12), @(1.0, 0.0))
    foreach ($p in $passi) {
        $quando = [System.Windows.Media.Animation.KeyTime]::FromTimeSpan(
                      [TimeSpan]::FromMilliseconds($Ms * [double]$p[0]))
        [void]$a.KeyFrames.Add(
            (New-Object System.Windows.Media.Animation.SplineDoubleKeyFrame ([double]$p[1]), $quando))
    }
    return $a
}

function Set-GateStroke {
    param($Elemento, [string]$Token)
    [System.Windows.Media.SolidColorBrush]$pennello = New-Brush $script:Tokens[$script:Settings.Theme][$Token]
    $Elemento.Stroke = $pennello
}

function Measure-PasswordStrength {
    param([Security.SecureString]$Password)
    if ($null -eq $Password -or $Password.Length -eq 0) { return 0 }
    $b = [DuckNative.Secret]::FromSecureString($Password)
    try {
        $minuscole = $false; $maiuscole = $false; $cifre = $false; $altro = $false
        foreach ($c in $b) {
            if     ($c -ge 0x61 -and $c -le 0x7A) { $minuscole = $true }
            elseif ($c -ge 0x41 -and $c -le 0x5A) { $maiuscole = $true }
            elseif ($c -ge 0x30 -and $c -le 0x39) { $cifre     = $true }
            else                                  { $altro     = $true }
        }
        $alfabeto = 0
        if ($minuscole) { $alfabeto += 26 }
        if ($maiuscole) { $alfabeto += 26 }
        if ($cifre)     { $alfabeto += 10 }
        if ($altro)     { $alfabeto += 33 }
        if ($alfabeto -lt 2) { $alfabeto = 2 }
        return [int]($Password.Length * [Math]::Log($alfabeto, 2))
    } finally { [Array]::Clear($b, 0, $b.Length) }
}

function Test-SamePassword {
    param([Security.SecureString]$Prima, [Security.SecureString]$Seconda)
    $a = [DuckNative.Secret]::FromSecureString($Prima)
    $b = [DuckNative.Secret]::FromSecureString($Seconda)
    try { return [DuckNative.Secret]::Equal($a, $b) }
    finally { [Array]::Clear($a, 0, $a.Length); [Array]::Clear($b, 0, $b.Length) }
}

function Update-GateStrength {
    $g = $script:Gate
    if ($g.GStrength.Visibility -ne 'Visible') { return }
    $bit  = Measure-PasswordStrength $g.GPwd.SecurePassword
    $tema = $script:Tokens[$script:Settings.Theme]
    $colore, $parola = if     ($bit -eq 0)   { $tema.BorderControl, '' }
                       elseif ($bit -lt 40)  { $tema.Red,    'fragile' }
                       elseif ($bit -lt 60)  { $tema.Yellow, 'discreta' }
                       elseif ($bit -lt 85)  { $tema.Green,  'solida' }
                       else                  { $tema.Green,  'da manuale' }
    [System.Windows.Media.SolidColorBrush]$pennello = New-Brush $colore
    $g.GBar.Background = $pennello
    $piena = [Math]::Max(60.0, $g.GBar.Parent.ActualWidth)
    $g.GBar.BeginAnimation([System.Windows.FrameworkElement]::WidthProperty,
        (New-Anim $g.GBar.ActualWidth ($piena * [Math]::Min(1.0, $bit / 110.0)) 260 (New-Ease 'Cubic' 'EaseOut')))
    $g.GBarTxt.Text = if ($bit -eq 0) { '' } else { ('{0} bit, {1}' -f $bit, $parola) }
}

$script:GateMargine = 30
$script:GateAriaStage = 16

function Measure-GateCompact {
    $g = $script:Gate
    try { $g.Dialog.UpdateLayout() } catch {}
    $discesa = $script:GateMargine - $script:GateAriaStage
    $compatta = $g.GTitleBar.ActualHeight + $g.GLoader.ActualHeight + $discesa + $script:GateMargine
    return @{ Discesa = $discesa; Altezza = [Math]::Round($compatta) }
}

function Resize-GateWindow {
    param([double]$Altezza, [int]$Ms)
    $g = $script:Gate
    $da = [Math]::Round($g.Dialog.ActualHeight)
    if ([Math]::Abs($da - $Altezza) -lt 2) { return }
    $morbido = New-Ease 'Cubic' 'EaseInOut'
    try {
        $g.Dialog.SizeToContent = 'Manual'
        $g.Dialog.Height = $da
        $g.Dialog.BeginAnimation([System.Windows.Window]::HeightProperty,
            (New-Anim $da $Altezza $Ms $morbido))
        if (-not [double]::IsNaN($g.Dialog.Top)) {
            $g.Dialog.BeginAnimation([System.Windows.Window]::TopProperty,
                (New-Anim $g.Dialog.Top ($g.Dialog.Top + ($da - $Altezza) / 2) $Ms $morbido))
        }
    } catch {}
}

function Hide-GateForm {
    $g = $script:Gate
    if ($g.FormaNascosta) { return }
    $g.FormaNascosta = $true

    $misura = Measure-GateCompact
    $g.Discesa        = $misura.Discesa
    $g.AltezzaPiena   = [Math]::Round($g.Dialog.ActualHeight)
    $g.AltezzaStretta = $misura.Altezza
    if (-not [double]::IsNaN($g.Dialog.Top)) { $g.TopPieno = $g.Dialog.Top }

    $O = [System.Windows.UIElement]::OpacityProperty
    $Y = [System.Windows.Media.TranslateTransform]::YProperty

    $g.GBody.IsHitTestVisible   = $false
    $g.GFooter.IsHitTestVisible = $false
    $g.GBody.BeginAnimation($O,   (New-Anim 1 0 180 (New-Ease 'Cubic' 'EaseIn')))
    $g.GFooter.BeginAnimation($O, (New-Anim 1 0 140 (New-Ease 'Cubic' 'EaseIn')))
    $g.GBodyT.BeginAnimation($Y,  (New-Anim 0 18 240 (New-Ease 'Cubic' 'EaseIn')))
    $g.GLoaderT.BeginAnimation($Y, (New-Anim 0 $g.Discesa 560 (New-Ease 'Cubic' 'EaseInOut')))
    Resize-GateWindow -Altezza $g.AltezzaStretta -Ms 560
}

function Show-GateForm {
    $g = $script:Gate
    if (-not $g.FormaNascosta) { return }
    $g.FormaNascosta = $false
    $g.GWork.Visibility = 'Collapsed'

    $O = [System.Windows.UIElement]::OpacityProperty
    $Y = [System.Windows.Media.TranslateTransform]::YProperty
    $ease = New-Ease 'Cubic' 'EaseOut'

    $g.GBody.IsHitTestVisible   = $true
    $g.GFooter.IsHitTestVisible = $true
    $g.GBody.BeginAnimation($O,   (New-Anim 0 1 300 $ease))
    $g.GFooter.BeginAnimation($O, (New-Anim 0 1 300 $ease))
    $g.GBodyT.BeginAnimation($Y,  (New-Anim 18 0 320 $ease))
    $g.GLoaderT.BeginAnimation($Y, (New-Anim $g.Discesa 0 500 (New-Ease 'Cubic' 'EaseInOut')))

    if (-not $g.AltezzaPiena) { return }
    Resize-GateWindow -Altezza $g.AltezzaPiena -Ms 500

    $g.Riassetto = New-Object System.Windows.Threading.DispatcherTimer
    $g.Riassetto.Interval = [TimeSpan]::FromMilliseconds(540)
    $g.Riassetto.Add_Tick({
        $gg = $script:Gate
        $gg.Riassetto.Stop()
        if ($gg.FormaNascosta) { return }
        try {
            $gg.Dialog.BeginAnimation([System.Windows.Window]::HeightProperty, $null)
            $gg.Dialog.BeginAnimation([System.Windows.Window]::TopProperty, $null)
            $gg.Dialog.Height = $gg.AltezzaPiena
            $gg.Dialog.SizeToContent = 'Height'
        } catch {}
    })
    $g.Riassetto.Start()
}

function Set-GateStage {
    param([string]$Testo)
    $g = $script:Gate
    $g.GWork.Text = $Testo
    if (-not $g.FormaNascosta) { return }
    $misura = Measure-GateCompact
    $g.AltezzaStretta = $misura.Altezza
    Resize-GateWindow -Altezza $misura.Altezza -Ms 260
}

function Start-GateWork {
    param([string]$Testo)
    $g = $script:Gate
    $g.GForm.IsEnabled  = $false
    $g.GGo.IsEnabled    = $false
    $g.GLater.IsEnabled = $false
    $g.GMsg.Visibility  = 'Collapsed'
    $g.GWork.Visibility = 'Visible'
    $g.GWork.Text       = $Testo
    Hide-GateForm

    Set-GateStroke $g.GRing1 'Accent'
    Set-GateStroke $g.GRing2 'Blue'
    $g.GSeal.Opacity  = 0
    $g.GRing1.Opacity = 0.95
    $g.GRing2.Opacity = 0.85
    $angolo = [System.Windows.Media.RotateTransform]::AngleProperty
    $g.GRot1.BeginAnimation($angolo, (New-Spin 1900))
    $g.GRot2.BeginAnimation($angolo, (New-Spin 2700 360 0))
    $sx = [System.Windows.Media.ScaleTransform]::ScaleXProperty
    $sy = [System.Windows.Media.ScaleTransform]::ScaleYProperty
    $g.GDuckS.BeginAnimation($sx, (New-Pulse 1 1.07 780))
    $g.GDuckS.BeginAnimation($sy, (New-Pulse 1 1.07 780))
}

function Stop-GateWork {
    $g = $script:Gate
    $angolo = [System.Windows.Media.RotateTransform]::AngleProperty
    $g.GRot1.BeginAnimation($angolo, $null)
    $g.GRot2.BeginAnimation($angolo, $null)
    $g.GDuckS.BeginAnimation([System.Windows.Media.ScaleTransform]::ScaleXProperty, $null)
    $g.GDuckS.BeginAnimation([System.Windows.Media.ScaleTransform]::ScaleYProperty, $null)
    $g.GForm.IsEnabled  = $true
    $g.GGo.IsEnabled    = $true
    $g.GLater.IsEnabled = ($g.Modo -eq 'crea')
    $g.GRing1.Opacity   = 0.5
    $g.GRing2.Opacity   = 0.45
}

function Show-GateError {
    param([string]$Messaggio)
    $g = $script:Gate
    Stop-GateWork
    Show-GateForm
    Set-GateStroke $g.GRing1 'Red'
    Set-GateStroke $g.GRing2 'Red'
    $g.GMsg.Text = $Messaggio
    $g.GMsg.Visibility = 'Visible'
    $g.GShake.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, (New-Shake))
    $g.GPwd.Clear()
    if ($g.GPwd2.Visibility -eq 'Visible') { $g.GPwd2.Clear() }
    Update-GateStrength
    [void]$g.GPwd.Focus()
}

function Show-GateSuccess {
    param([string]$Esito)
    $g = $script:Gate
    Stop-GateWork
    $g.Esito = $Esito
    $g.GRing1.Opacity = 0
    $g.GRing2.Opacity = 0
    $g.GSeal.Opacity  = 1
    $sx = [System.Windows.Media.ScaleTransform]::ScaleXProperty
    $sy = [System.Windows.Media.ScaleTransform]::ScaleYProperty
    $molla = New-Ease 'Back' 'EaseOut' 0.5
    $g.GSealS.BeginAnimation($sx, (New-Anim 0.6 1 420 $molla))
    $g.GSealS.BeginAnimation($sy, (New-Anim 0.6 1 420 $molla))
    $salto = New-Anim 0 -9 190 (New-Ease 'Cubic' 'EaseOut')
    $salto.AutoReverse = $true
    $g.GDuckT.BeginAnimation([System.Windows.Media.TranslateTransform]::YProperty, $salto)

    Set-GateStage $(if ($Esito -eq 'cambiato') { 'Involucro riscritto.' } else { 'Apertura della nota...' })

    if ($g.Trattieni) {
        $script:GateResult = $Esito
        if ($g.Frame) { $g.Frame.Continue = $false }
        return
    }

    $g.Congedo = New-Object System.Windows.Threading.DispatcherTimer
    $g.Congedo.Interval = [TimeSpan]::FromMilliseconds(620)
    $g.Congedo.Add_Tick({
        $script:Gate.Congedo.Stop()
        Close-Gate $script:Gate.Esito
    })
    $g.Congedo.Start()
}

function Close-GateOverlay {
    $g = $script:Gate
    if (-not $g -or -not $g.Dialog -or $g.Chiusa) { return }
    $g.Chiusa = $true
    try { $g.Dialog.Topmost = $false } catch {}
    Close-DialogAnimated $g.Dialog $g.GRoot $true
}

function Close-Gate {
    param([string]$Esito)
    $script:GateResult = $Esito
    $script:Gate.Chiusa = $true
    Close-DialogAnimated $script:Gate.Dialog $script:Gate.GRoot $true
    if ($script:Gate.Frame) { $script:Gate.Frame.Continue = $false }
}

function Start-GateDerivation {
    $g = $script:Gate
    if (-not $g.GGo.IsEnabled) { return }

    if ($g.GPwd.SecurePassword.Length -eq 0) {
        Show-GateError 'Serve una password.'
        return
    }
    if ($g.Modo -ne 'sblocca') {
        if ($g.GPwd.SecurePassword.Length -lt 8) {
            Show-GateError 'Almeno otto caratteri: sotto questa soglia la chiave non protegge nulla.'
            return
        }
        if (-not (Test-SamePassword $g.GPwd.SecurePassword $g.GPwd2.SecurePassword)) {
            Show-GateError 'Le due password non combaciano.'
            return
        }
    }

    if ($g.Modo -eq 'sblocca') {
        $g.Store  = Read-VaultHeader $script:StoreFile
        $g.Legacy = $false
        if ($null -eq $g.Store) {
            $g.Store  = Read-LegacyKeystore
            $g.Legacy = $true
        }
        if ($null -eq $g.Store) {
            Show-GateError 'Il contenitore e'' illeggibile: store.bin non e'' una cassaforte di DuckNote.'
            return
        }
        $g.Salt = $g.Store.Salt
        $g.Kdf  = $g.Store.Kdf
    } else {
        $g.Salt = New-Entropy 32
        $g.Kdf  = $script:VaultKdf
    }

    Start-GateWork ('Forgiatura della chiave. ' + $g.Parametri)
    $g.Orologio = [Diagnostics.Stopwatch]::StartNew()
    $g.Worker   = Start-KeyDerivation -Password $g.GPwd.SecurePassword -Salt $g.Salt -Kdf $g.Kdf
    $g.GPwd.Clear()
    if ($g.GPwd2.Visibility -eq 'Visible') { $g.GPwd2.Clear() }

    $g.Attesa = New-Object System.Windows.Threading.DispatcherTimer
    $g.Attesa.Interval = [TimeSpan]::FromMilliseconds(90)
    $g.Attesa.Add_Tick({ Step-GateDerivation })
    $g.Attesa.Start()
}

function Step-GateDerivation {
    $g = $script:Gate
    $g.GWork.Text = ('Forgiatura della chiave, {0:N1} s. {1}' -f $g.Orologio.Elapsed.TotalSeconds, $g.Parametri)
    if (-not $g.Worker.Done) { return }
    $g.Attesa.Stop()
    $g.Orologio.Stop()

    if ($g.Worker.Error) {
        Show-GateError ('Derivazione non riuscita: ' + $g.Worker.Error)
        return
    }
    $kek = $g.Worker.Key

    if ($g.Modo -eq 'sblocca') {
        $aperta = if ($g.Legacy) { Import-LegacyVault -Header $g.Store -Kek $kek }
                  else            { Open-VaultWithKek  -Header $g.Store -Kek $kek }
        [Array]::Clear($kek, 0, $kek.Length)
        if (-not $aperta) {
            Show-GateError 'Password errata.'
            return
        }
        Show-GateSuccess 'aperto'
        return
    }

    try {
        if ($g.Modo -eq 'cambia') { Update-VaultWrapper -Kek $kek -Kdf $g.Kdf -Salt $g.Salt }
        else                      { New-VaultStore     -Kek $kek -Kdf $g.Kdf -Salt $g.Salt }
    } catch {
        [Array]::Clear($kek, 0, $kek.Length)
        Show-GateError "Chiave non salvata: $_"
        return
    }
    [Array]::Clear($kek, 0, $kek.Length)
    Show-GateSuccess $(if ($g.Modo -eq 'cambia') { 'cambiato' } else { 'creato' })
}

function Set-GateMode {
    param([string]$Modo)
    $g = $script:Gate
    $g.Modo = $Modo
    switch ($Modo) {
        'crea' {
            $g.GBarTitle.Text = 'Cifratura della nota'
            $g.GTitle.Text    = 'Chiudi la nota a chiave'
            $g.GSub.Text      = 'Da questa password nasce la chiave che cifra la nota, la scansione e gli ' +
                                'host esclusi. Senza di essa il contenuto non torna piu'' leggibile, nemmeno a te.'
            $g.GGo.Content    = 'Crea la chiave'
        }
        'cambia' {
            $g.GBarTitle.Text = 'Nuova password'
            $g.GTitle.Text    = 'Cambia la password'
            $g.GSub.Text      = 'La chiave che cifra i dati resta la stessa: cambia solo l''involucro che la ' +
                                'custodisce, e nessun file viene riscritto.'
            $g.GGo.Content    = 'Cambia'
            $g.GLater.Visibility = 'Collapsed'
        }
        'sblocca' {
            $g.GBarTitle.Text = 'DuckNote'
            $g.GTitle.Text    = 'La nota e'' chiusa'
            $g.GSub.Text      = 'Inserisci la password per aprirla.'
            $g.GGo.Content    = 'Sblocca'
            $g.GLbl1.Text     = 'Password'
            $g.GLbl2.Visibility  = 'Collapsed'
            $g.GPwd2.Visibility  = 'Collapsed'
            $g.GStrength.Visibility = 'Collapsed'
            $g.GLater.Visibility = 'Collapsed'
        }
    }
}

function Show-VaultGate {
    param([ValidateSet('crea','sblocca','cambia')][string]$Modo, [switch]$Trattieni)

    $script:GateResult = 'annullato'
    if ($Modo -eq 'cambia' -and -not (Test-VaultOpen)) { return 'errore' }
    try {
        $rd  = [System.Xml.XmlReader]::Create([System.IO.StringReader]$script:GateXaml)
        $dlg = [System.Windows.Markup.XamlReader]::Load($rd)
    } catch {
        [System.Windows.MessageBox]::Show("Schermata della chiave non disponibile: $_", 'DuckNote',
            [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Error) | Out-Null
        return 'errore'
    }

    $tema = $script:Tokens[$script:Settings.Theme]
    foreach ($k in @($tema.Keys)) {
        [System.Windows.Media.SolidColorBrush]$pennello = New-Brush $tema[$k]
        $dlg.Resources[$k] = $pennello
    }

    $script:Gate = @{ Dialog = $dlg; Trattieni = [bool]$Trattieni }
    foreach ($n in @('GRoot','GShake','GTitleBar','GClose','GBarTitle',
                     'GLoader','GLoaderT','GStage','GBody','GBodyT','GFooter',
                     'GRing1','GRing2','GRot1','GRot2','GSeal','GSealS','GDuckS','GDuckT',
                     'GDuckImg','GDuckVec','GDuckBody','GDuckEye','GTitle','GSub','GCard','GForm',
                     'GLbl1','GLbl2','GPwd','GPwd2','GStrength','GBar','GBarTxt','GMsg','GWork',
                     'GLater','GGo')) {
        $script:Gate[$n] = $dlg.FindName($n)
    }

    if ($null -eq $script:DuckBitmap) { [void](Initialize-DuckImage) }
    if ($null -ne $script:DuckBitmap) {
        $script:Gate.GDuckImg.Source     = $script:DuckBitmap
        $script:Gate.GDuckImg.Visibility = 'Visible'
        $script:Gate.GDuckVec.Visibility = 'Collapsed'
        $dlg.Add_SourceInitialized({ Set-DuckIcon $script:Gate.Dialog })
    } else {
        $script:Gate.GDuckBody.Data = [System.Windows.Media.Geometry]::Parse($script:DuckGeometry)
        $script:Gate.GDuckEye.Data  = [System.Windows.Media.Geometry]::Parse($script:DuckEye)
    }

    $script:Gate.Parametri = ('Argon2id {0} MiB, {1} passate su {2} corsie, poi PBKDF2-SHA512 con {3:N0} giri.' -f
                              [int]($script:VaultKdf.Memory / 1024), $script:VaultKdf.Passes,
                              $script:VaultKdf.Lanes, $script:VaultKdf.Iterations)
    Set-GateMode $Modo
    if ($script:Window -and $script:Window.IsVisible) { $dlg.Owner = $script:Window }

    $script:Gate.GTitleBar.Add_MouseLeftButtonDown({ try { $script:Gate.Dialog.DragMove() } catch {} })
    $script:Gate.GClose.Add_Click({ Close-Gate 'annullato' })
    $script:Gate.GLater.Add_Click({ Close-Gate 'dopo' })
    $script:Gate.GGo.Add_Click({ Start-GateDerivation })
    $script:Gate.GPwd.Add_PasswordChanged({ Update-GateStrength })
    $dlg.Add_ContentRendered({
        Open-DialogAnimated $script:Gate.GRoot
        [void]$script:Gate.GPwd.Focus()
    })
    $dlg.Add_KeyDown({
        param($s, $e)
        if ($e.Key -eq 'Escape') {
            Close-Gate $(if ($script:Gate.Modo -eq 'sblocca') { 'annullato' } else { 'dopo' })
            $e.Handled = $true
            return
        }
        if ($e.Key -eq 'Return') { Start-GateDerivation; $e.Handled = $true }
    })

    if (-not $Trattieni) {
        [void]$dlg.ShowDialog()
        return $script:GateResult
    }

    $script:Gate.Frame = New-Object System.Windows.Threading.DispatcherFrame
    $dlg.Topmost = $true
    $dlg.Add_Closed({ if ($script:Gate.Frame) { $script:Gate.Frame.Continue = $false } })
    $dlg.Show()
    [System.Windows.Threading.Dispatcher]::PushFrame($script:Gate.Frame)
    return $script:GateResult
}

function Convert-DataToVault {
    Save-Note -Silent
    Save-PrivateData
    Save-Settings
    if (Test-Path $script:ScanFile) {
        try { Set-VaultSection $script:SezioneScansione ([IO.File]::ReadAllBytes($script:ScanFile)) } catch {}
    }
    Save-VaultStore
    foreach ($f in @($script:NoteFile, "$($script:NoteFile).bak", $script:ScanFile)) {
        Remove-FileSecurely $f
    }
    Remove-PlaintextLeftovers
}

function Enable-NoteEncryption {
    if (Test-VaultLocked) { Set-Status 'La nota e'' gia'' cifrata.'; return $false }
    if ((Show-VaultGate -Modo 'crea' -Trattieni) -ne 'creato') { Close-GateOverlay; return $false }
    Convert-DataToVault
    Close-GateOverlay
    Update-LockButton
    Set-Status 'Cifratura attiva: la nota vive nel contenitore e i file in chiaro sono stati coperti e rimossi.'
    return $true
}

function Disable-NoteEncryption {
    if (-not (Test-VaultOpen)) { return $false }
    Clear-VaultKey

    Save-Note -Silent -Plain
    if (-not (Test-Path $script:NoteFile)) {
        Set-Status 'Cifratura ancora attiva: la nota non si e'' potuta scrivere in chiaro, il contenitore resta.'
        return $false
    }
    Save-Settings
    foreach ($f in @($script:StoreFile, "$($script:StoreFile).bak")) {
        Remove-FileSecurely $f
    }
    Update-LockButton
    Set-Status 'Cifratura disattivata: la nota e'' tornata in chiaro in note.xaml.'
    return $true
}

$script:Bloccata = $false

function Lock-Vault {
    param([string]$Motivo = 'a richiesta')
    if (-not (Test-VaultOpen) -or $script:Bloccata) { return }

    Save-Note -Silent
    Save-PrivateData
    Save-ScanSnapshot
    Suspend-NoteTimers

    $script:Editor.Document = New-EditorDocument
    $script:LastPara   = $null
    $script:HostsCache = $null
    $script:DirtyParas.Clear()
    Clear-VaultKey

    Show-LockScreen
    Update-LockButton
    Set-Status ('Bloccata {0}. Serve la password per riaprirla.' -f $Motivo)
}

function Show-LockScreen {
    $ui = $script:UI
    $script:Bloccata = $true
    $script:PannelloAlLavoro = $false

    $ui.VeilPwd.Clear()
    $ui.VeilMsg.Visibility  = 'Collapsed'
    $ui.VeilWork.Visibility = 'Collapsed'
    $ui.VeilForm.IsEnabled  = $true
    $ui.VeilCard.Opacity    = 1
    $script:VeloSpoglio     = $false
    $ui.VeilBody.IsHitTestVisible = $true
    $ui.VeilBody.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $null)
    $ui.VeilBodyT.BeginAnimation([System.Windows.Media.TranslateTransform]::YProperty, $null)
    $ui.VeilLoaderT.BeginAnimation([System.Windows.Media.TranslateTransform]::YProperty, $null)
    $ui.VeilBody.Opacity    = 1
    $ui.VeilBodyT.Y         = 0
    $ui.VeilLoaderT.Y       = 0
    $ui.VeilSeal.Opacity    = 0
    $ui.VeilRing1.Opacity   = 0.5
    $ui.VeilRing2.Opacity   = 0.45
    Set-GateStroke $ui.VeilRing1 'Accent'
    Set-GateStroke $ui.VeilRing2 'Blue'

    $ui.LockVeil.Visibility = 'Visible'
    $ui.LockVeil.BeginAnimation([System.Windows.UIElement]::OpacityProperty,
        (New-Anim 0 1 220 (New-Ease 'Cubic' 'EaseOut')))
    [void]$ui.VeilPwd.Focus()
}

function Show-PanelError {
    param([string]$Messaggio)
    $ui = $script:UI
    Stop-PanelWork
    Show-VeilForm
    Set-GateStroke $ui.VeilRing1 'Red'
    Set-GateStroke $ui.VeilRing2 'Red'
    $ui.VeilMsg.Text = $Messaggio
    $ui.VeilMsg.Visibility = 'Visible'
    $ui.VeilShake.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, (New-Shake))
    $ui.VeilPwd.Clear()
    [void]$ui.VeilPwd.Focus()
}

function Hide-VeilForm {
    $ui = $script:UI
    if ($script:VeloSpoglio) { return }
    $script:VeloSpoglio = $true

    $ui.LockVeil.UpdateLayout()
    $centroVelo = $ui.LockVeil.ActualHeight / 2
    $inizio = $ui.VeilLoader.TranslatePoint((New-Object System.Windows.Point 0, 0), $ui.LockVeil).Y
    $script:VeloDiscesa = [Math]::Round($centroVelo - ($inizio + $ui.VeilLoader.ActualHeight / 2))

    $O = [System.Windows.UIElement]::OpacityProperty
    $Y = [System.Windows.Media.TranslateTransform]::YProperty
    $ui.VeilBody.IsHitTestVisible = $false
    $ui.VeilBody.BeginAnimation($O, (New-Anim 1 0 200 (New-Ease 'Cubic' 'EaseIn')))
    $ui.VeilBodyT.BeginAnimation($Y, (New-Anim 0 18 240 (New-Ease 'Cubic' 'EaseIn')))
    $ui.VeilLoaderT.BeginAnimation($Y, (New-Anim 0 $script:VeloDiscesa 520 (New-Ease 'Cubic' 'EaseInOut')))
}

function Show-VeilForm {
    $ui = $script:UI
    if (-not $script:VeloSpoglio) { return }
    $script:VeloSpoglio = $false

    $O = [System.Windows.UIElement]::OpacityProperty
    $Y = [System.Windows.Media.TranslateTransform]::YProperty
    $ease = New-Ease 'Cubic' 'EaseOut'
    $ui.VeilBody.IsHitTestVisible = $true
    $ui.VeilBody.BeginAnimation($O, (New-Anim 0 1 300 $ease))
    $ui.VeilBodyT.BeginAnimation($Y, (New-Anim 18 0 320 $ease))
    $ui.VeilLoaderT.BeginAnimation($Y, (New-Anim $script:VeloDiscesa 0 460 (New-Ease 'Cubic' 'EaseInOut')))
    $ui.VeilWork.Visibility = 'Collapsed'
}

function Start-PanelWork {
    param([string]$Testo)
    $ui = $script:UI
    $ui.VeilForm.IsEnabled  = $false
    $ui.VeilMsg.Visibility  = 'Collapsed'
    $ui.VeilWork.Visibility = 'Visible'
    $ui.VeilWork.Text       = $Testo
    Hide-VeilForm
    $ui.VeilRing1.Opacity   = 0.95
    $ui.VeilRing2.Opacity   = 0.85
    Set-GateStroke $ui.VeilRing1 'Accent'
    Set-GateStroke $ui.VeilRing2 'Blue'
    $angolo = [System.Windows.Media.RotateTransform]::AngleProperty
    $ui.VeilRot1.BeginAnimation($angolo, (New-Spin 1900))
    $ui.VeilRot2.BeginAnimation($angolo, (New-Spin 2700 360 0))
    $sx = [System.Windows.Media.ScaleTransform]::ScaleXProperty
    $sy = [System.Windows.Media.ScaleTransform]::ScaleYProperty
    $ui.VeilDuckS.BeginAnimation($sx, (New-Pulse 1 1.07 780))
    $ui.VeilDuckS.BeginAnimation($sy, (New-Pulse 1 1.07 780))
}

function Stop-PanelWork {
    $ui = $script:UI
    $script:PannelloAlLavoro = $false
    if ($script:PannelloTimer) { try { $script:PannelloTimer.Stop() } catch {} }
    $angolo = [System.Windows.Media.RotateTransform]::AngleProperty
    $ui.VeilRot1.BeginAnimation($angolo, $null)
    $ui.VeilRot2.BeginAnimation($angolo, $null)
    $ui.VeilDuckS.BeginAnimation([System.Windows.Media.ScaleTransform]::ScaleXProperty, $null)
    $ui.VeilDuckS.BeginAnimation([System.Windows.Media.ScaleTransform]::ScaleYProperty, $null)
    $ui.VeilForm.IsEnabled  = $true
    $ui.VeilRing1.Opacity   = 0.5
    $ui.VeilRing2.Opacity   = 0.45
}

function Start-PanelUnlock {
    if ($script:PannelloAlLavoro -or -not $script:Bloccata) { return }
    $ui = $script:UI

    if ($ui.VeilPwd.SecurePassword.Length -eq 0) {
        Show-PanelError 'Serve la password.'
        return
    }
    $store = Read-VaultHeader $script:StoreFile
    if ($null -eq $store) {
        $store = Read-LegacyKeystore
        $script:PannelloLegacy = $true
    } else { $script:PannelloLegacy = $false }
    if ($null -eq $store) {
        Show-PanelError 'Il contenitore non si legge: store.bin non e'' una cassaforte di DuckNote.'
        return
    }

    $script:PannelloAlLavoro = $true
    $script:PannelloStore    = $store
    $parametri = ('Argon2id {0} MiB, {1} passate, poi PBKDF2-SHA512 con {2:N0} giri.' -f
                  [int]($store.Kdf.Memory / 1024), $store.Kdf.Passes, $store.Kdf.Iterations)
    Start-PanelWork ('Apertura della cassaforte. ' + $parametri)
    $script:PannelloParametri = $parametri
    $script:PannelloOrologio  = [Diagnostics.Stopwatch]::StartNew()
    $script:PannelloWorker    = Start-KeyDerivation -Password $ui.VeilPwd.SecurePassword -Salt $store.Salt -Kdf $store.Kdf
    $ui.VeilPwd.Clear()

    $script:PannelloTimer = New-Object System.Windows.Threading.DispatcherTimer
    $script:PannelloTimer.Interval = [TimeSpan]::FromMilliseconds(90)
    $script:PannelloTimer.Add_Tick({ Step-PanelUnlock })
    $script:PannelloTimer.Start()
}

function Step-PanelUnlock {
    $ui = $script:UI
    $ui.VeilWork.Text = ('Apertura della cassaforte, {0:N1} s. {1}' -f
                         $script:PannelloOrologio.Elapsed.TotalSeconds, $script:PannelloParametri)
    if (-not $script:PannelloWorker.Done) { return }
    $script:PannelloTimer.Stop()
    $script:PannelloOrologio.Stop()

    if ($script:PannelloWorker.Error) {
        Show-PanelError ('Derivazione non riuscita: ' + $script:PannelloWorker.Error)
        return
    }
    $kek = $script:PannelloWorker.Key
    $aperta = if ($script:PannelloLegacy) { Import-LegacyVault -Header $script:PannelloStore -Kek $kek }
              else { Open-VaultWithKek -Header $script:PannelloStore -Kek $kek }
    [Array]::Clear($kek, 0, $kek.Length)

    if (-not $aperta) {
        Show-PanelError 'Password errata.'
        return
    }
    Show-PanelSuccess
}

function Show-PanelSuccess {
    $ui = $script:UI
    Stop-PanelWork
    $ui.VeilWork.Text       = 'Apertura della nota...'
    $ui.VeilWork.Visibility = 'Visible'
    $ui.VeilForm.IsEnabled  = $false
    $ui.VeilRing1.Opacity   = 0
    $ui.VeilRing2.Opacity   = 0
    $ui.VeilSeal.Opacity    = 1
    $sx = [System.Windows.Media.ScaleTransform]::ScaleXProperty
    $sy = [System.Windows.Media.ScaleTransform]::ScaleYProperty
    $molla = New-Ease 'Back' 'EaseOut' 0.5
    $ui.VeilSealS.BeginAnimation($sx, (New-Anim 0.6 1 420 $molla))
    $ui.VeilSealS.BeginAnimation($sy, (New-Anim 0.6 1 420 $molla))
    $salto = New-Anim 0 -9 190 (New-Ease 'Cubic' 'EaseOut')
    $salto.AutoReverse = $true
    $ui.VeilDuckT.BeginAnimation([System.Windows.Media.TranslateTransform]::YProperty, $salto)

    $script:PannelloCongedo = New-Object System.Windows.Threading.DispatcherTimer
    $script:PannelloCongedo.Interval = [TimeSpan]::FromMilliseconds(520)
    $script:PannelloCongedo.Add_Tick({
        $script:PannelloCongedo.Stop()
        Complete-PanelUnlock
    })
    $script:PannelloCongedo.Start()
}

function Complete-PanelUnlock {
    $ui = $script:UI
    Read-PrivateData
    Read-IgnoredHosts
    Load-Note
    [void](Restore-ScanSnapshot)
    Remove-PlaintextLeftovers
    Refresh-EditorDots
    Refresh-SideList
    Update-ScanUi
    Update-HeaderStats

    $svanisce = New-Anim 1 0 260 (New-Ease 'Cubic' 'EaseIn')
    $svanisce.Add_Completed({
        $script:UI.LockVeil.Visibility = 'Collapsed'
        $script:UI.LockVeil.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $null)
    })
    $ui.LockVeil.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $svanisce)

    $script:Bloccata = $false
    Register-Activity
    Apply-Timers
    Update-LockButton
    Set-Status ('Riaperta alle {0}.' -f (Get-Date).ToString('HH:mm'))
    try { $script:Editor.Focus() } catch {}
}

function Update-LockButton {
    if ($null -eq $script:UI.BtnLock) { return }
    $script:UI.BtnLock.Visibility = if ((Test-VaultOpen) -and -not $script:Bloccata) { 'Visible' } else { 'Collapsed' }
}

function Suspend-NoteTimers {
    foreach ($t in @($script:SaveTimer, $script:FormatTimer, $script:HostsTimer,
                     $script:TypingTimer, $script:MonitorTimer, $script:LockTimer)) {
        try { if ($t) { $t.Stop() } } catch {}
    }
}

$script:UltimoTocco = [DateTime]::UtcNow
$script:LockPassoMs = 30000

function Register-Activity {
    $script:UltimoTocco = [DateTime]::UtcNow
}

function Get-IdleMinutes {
    $sistema = 0
    try { $sistema = [double]([DuckNative.Idle]::Milliseconds()) } catch { $sistema = 0 }
    if ($sistema -lt $script:LockPassoMs) { Register-Activity }
    $proprio = ([DateTime]::UtcNow - $script:UltimoTocco).TotalMinutes
    $altro   = $sistema / 60000.0
    return [Math]::Min($proprio, $altro)
}

function Test-IdleLock {
    if (-not (Test-VaultOpen)) { return }
    if ($script:Bloccata) { return }
    $minuti = [int]$script:Settings.AutoLockMinutes
    if ($minuti -le 0) { return }
    $fermo = Get-IdleMinutes
    if ($fermo -ge $minuti) { Lock-Vault -Motivo ('dopo {0:N0} minuti di inattivita' -f $fermo) }
}

function Get-AppDataInventory {
    if (-not (Test-Path $script:AppDir)) { return @() }
    return @(Get-ChildItem -LiteralPath $script:AppDir -Recurse -Force -File -ErrorAction SilentlyContinue)
}

function Remove-AppData {
    $script:Dismesso = $true
    foreach ($t in @($script:SaveTimer, $script:FormatTimer, $script:MonitorTimer,
                     $script:HostsTimer, $script:TypingTimer, $script:FirstTimer, $script:PumpTimer)) {
        try { if ($t) { $t.Stop() } } catch {}
    }
    Clear-VaultKey

    $falliti = @()
    foreach ($f in (Get-AppDataInventory)) {
        Remove-FileSecurely $f.FullName
        if (Test-Path -LiteralPath $f.FullName) { $falliti += $f.FullName }
    }
    try { Remove-Item -LiteralPath $script:AppDir -Recurse -Force -ErrorAction Stop } catch { $falliti += $script:AppDir }
    return $falliti
}

$script:UninstallXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Disinstalla DuckNote" Width="470" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        WindowStyle="None" AllowsTransparency="True" UseLayoutRounding="True"
        FontFamily="SF Pro Text, Segoe UI Variable Text, Segoe UI" FontSize="13"
        Background="Transparent">
  <Border x:Name="URoot" CornerRadius="14" Background="{DynamicResource BgSidebarGlass}"
          BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1"
          RenderTransformOrigin="0.5,0.45" Opacity="0">
    <Border.RenderTransform>
      <TransformGroup>
        <ScaleTransform x:Name="UScale" ScaleX="0.96" ScaleY="0.96"/>
        <TranslateTransform x:Name="UMove" Y="12"/>
      </TransformGroup>
    </Border.RenderTransform>
    <StackPanel>
      <Border x:Name="UTitleBar" Background="{DynamicResource BgToolbarGlass}" Height="44" CornerRadius="13,13,0,0">
        <Grid>
          <Button x:Name="UClose" Style="{DynamicResource TrafficBtn}" Background="{DynamicResource TLClose}"
                  HorizontalAlignment="Left" VerticalAlignment="Center" Margin="14,0,0,0" ToolTip="Chiudi">
            <Path Data="M 0,0 L 6,6 M 6,0 L 0,6" Stroke="#5A1416" StrokeThickness="1.2" Stretch="Uniform"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
          </Button>
          <TextBlock Text="Disinstalla DuckNote" FontSize="13" FontWeight="SemiBold" HorizontalAlignment="Center"
                     VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
        </Grid>
      </Border>

      <StackPanel Margin="22,18,22,0">
        <TextBlock Text="Viene cancellato tutto il contenuto di questa cartella:" TextWrapping="Wrap"
                   Foreground="{DynamicResource Label}"/>
        <TextBlock x:Name="UPath" FontFamily="SF Mono, Cascadia Mono, Consolas" FontSize="12" Margin="0,7,0,0"
                   TextWrapping="Wrap" Foreground="{DynamicResource LabelSecondary}"/>

        <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Padding="14,11" Margin="0,13,0,0"
                BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
          <ScrollViewer x:Name="UListScroll" MaxHeight="150" VerticalScrollBarVisibility="Auto">
            <TextBlock x:Name="UList" FontFamily="SF Mono, Cascadia Mono, Consolas" FontSize="11.5"
                       Foreground="{DynamicResource LabelSecondary}"/>
          </ScrollViewer>
        </Border>

        <TextBlock x:Name="UWarn" TextWrapping="Wrap" Margin="0,13,0,0" FontSize="12"
                   Foreground="{DynamicResource Red}"/>

        <TextBlock Text="Scrivi DISINSTALLA per confermare" Margin="0,14,0,6" FontSize="12"
                   Foreground="{DynamicResource LabelSecondary}"/>
        <TextBox x:Name="UConfirm" Style="{DynamicResource Field}" Height="32"/>
      </StackPanel>

      <Border Background="{DynamicResource BgToolbarGlass}" Padding="18,12" Margin="0,18,0,0" CornerRadius="0,0,13,13">
        <Grid>
          <TextBlock x:Name="UNote" FontSize="11" VerticalAlignment="Center" HorizontalAlignment="Left"
                     Foreground="{DynamicResource LabelTertiary}" Text="DuckNote si chiude subito dopo."/>
          <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="UCancel" Style="{DynamicResource QuietBtn}" Content="Annulla" Margin="0,0,8,0"/>
            <Button x:Name="UGo" Content="Cancella tutto" IsEnabled="False" Height="30" Padding="16,0"
                    Cursor="Hand" FontWeight="SemiBold" Foreground="White">
              <Button.Template>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="b" CornerRadius="7" Background="{DynamicResource Red}"
                          Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="b" Property="Opacity" Value="0.88"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter TargetName="b" Property="Background" Value="{DynamicResource BgPressed}"/>
                      <Setter Property="Foreground" Value="{DynamicResource LabelTertiary}"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Button.Template>
            </Button>
          </StackPanel>
        </Grid>
      </Border>
    </StackPanel>
  </Border>
</Window>
'@

function Show-UninstallDialog {
    try {
        $rd  = [System.Xml.XmlReader]::Create([System.IO.StringReader]$script:UninstallXaml)
        $dlg = [System.Windows.Markup.XamlReader]::Load($rd)
        try { [void]$dlg.Resources.MergedDictionaries.Add($script:Window.Resources) } catch {}
        $dlg.Owner = $script:Window
        $uroot = $dlg.FindName('URoot')
        $dlg.Add_ContentRendered({ Open-DialogAnimated $uroot })

        $file = Get-AppDataInventory
        $peso = ($file | Measure-Object -Property Length -Sum).Sum
        $dlg.FindName('UPath').Text = $script:AppDir
        $dlg.FindName('UList').Text = if ($file.Count -eq 0) { 'La cartella e'' gia'' vuota.' }
            else {
                (@($file | ForEach-Object {
                    '{0}  ({1:N0} KB)' -f $_.FullName.Substring($script:AppDir.Length).TrimStart('\'),
                                          [Math]::Max(1, $_.Length / 1KB)
                }) -join "`n")
            }

        $avviso = @('{0} file, {1:N1} MB in tutto. I byte vengono sovrascritti prima della cancellazione.' -f
                    $file.Count, ($peso / 1MB))
        if (Test-VaultLocked) {
            $avviso += 'Fra questi c''e'' vault.key: senza quel file la nota cifrata non torna leggibile con nessuna password.'
        }
        $avviso += 'Lo script e l''eseguibile restano dove sono: al prossimo avvio DuckNote riparte come la prima volta.'
        $dlg.FindName('UWarn').Text = ($avviso -join ' ')

        $conferma = $dlg.FindName('UConfirm')
        $vai      = $dlg.FindName('UGo')
        $conferma.Add_TextChanged({ $vai.IsEnabled = ($conferma.Text.Trim() -ceq 'DISINSTALLA') }.GetNewClosure())

        $dlg.FindName('UTitleBar').Add_MouseLeftButtonDown({ try { $dlg.DragMove() } catch {} })
        $dlg.FindName('UClose').Add_Click({ Close-DialogAnimated $dlg $uroot $false }.GetNewClosure())
        $dlg.FindName('UCancel').Add_Click({ Close-DialogAnimated $dlg $uroot $false }.GetNewClosure())
        $dlg.Add_KeyDown({
            param($s, $e)
            if ($e.Key -eq 'Escape') { Close-DialogAnimated $dlg $uroot $false }
        }.GetNewClosure())

        $vai.Add_Click({
            $falliti = Remove-AppData
            $dlg.DialogResult = $true
            $dlg.Close()
            if ($falliti.Count -gt 0) {
                [System.Windows.MessageBox]::Show(
                    ("Questi elementi non sono stati rimossi:`n`n" + ($falliti -join "`n")),
                    'DuckNote', [System.Windows.MessageBoxButton]::OK,
                    [System.Windows.MessageBoxImage]::Warning) | Out-Null
            }
            $script:Window.Close()
        }.GetNewClosure())

        [void]$dlg.ShowDialog()
    } catch {
        Set-Status "Disinstallazione non avviata: $_"
    }
}

function Show-Preferences {
    try {
        $rd  = [System.Xml.XmlReader]::Create([System.IO.StringReader]$script:PrefsXaml)
        $dlg = [System.Windows.Markup.XamlReader]::Load($rd)
        try { [void]$dlg.Resources.MergedDictionaries.Add($script:Window.Resources) } catch {}
        $dlg.Owner = $script:Window
        $proot = $dlg.FindName('PRoot')
        $dlg.Add_ContentRendered({ Open-DialogAnimated $proot })

        $c = @{}
        foreach ($n in @('PSysTheme','PTheme','PLiveFmt','PAutosave','PAutoMs','PMonitor','PMonSec',
                         'PThreads','PPingN','PPingMs','PPortMs','PPorts','PDead','PDns','PNetBios',
                         'PMdns','PSsdp','PBanner','PSnmp','PCommunity','PShares','PWmi','PError','PDnsServer',
                         'POk','PCancel','PDucks','PDuckN','PDuckOp','POui','POuiInfo',
                         'PClose','PTitleBar','PParallel','PParallelInfo',
                         'PSecDot','PSecState','PSecInfo','PSecOn','PSecPwd','PSecOff','PWipe',
                         'PLockMin','PLockInfo','PFinger','PFingerPath')) {
            $c[$n] = $dlg.FindName($n)
        }

        $tema = $script:Tokens[$script:Settings.Theme]
        $cifrata = Test-VaultOpen
        $c.PSecDot.Fill      = New-Brush $(if ($cifrata) { $tema.Green } else { $tema.LabelQuaternary })
        $c.PSecState.Text    = if ($cifrata) { 'Nota cifrata, cassaforte aperta' } else { 'Nota in chiaro' }
        $c.PSecInfo.Text     = if ($cifrata) {
            ('Chiave da Argon2id {0} MiB piu'' PBKDF2-SHA512, contenuti in AES-256 con HMAC-SHA256. ' +
             'Nota, backup, scansione e host esclusi sono illeggibili senza password.') -f
            [int]($script:VaultKdf.Memory / 1024)
        } else {
            'Chiunque apra %APPDATA%\DuckNote\note.xaml legge quello che scrivi. Attivando la cifratura ' +
            'la nota passa in note.dat e il file in chiaro viene coperto e rimosso.'
        }
        $c.PSecOn.IsEnabled  = -not $cifrata
        $c.PSecPwd.IsEnabled = $cifrata
        $c.PSecOff.IsEnabled = $cifrata
        $c.PLockMin.Text      = "$($script:Settings.AutoLockMinutes)"
        $c.PLockMin.IsEnabled = $cifrata
        $impronta = Get-ScriptFingerprint
        $c.PFinger.Text     = $impronta.Hash
        $c.PFingerPath.Text = $impronta.Path

        $c.PSecOn.Add_Click({
            Close-DialogAnimated $dlg $proot $false
            [void](Enable-NoteEncryption)
        }.GetNewClosure())
        $c.PSecPwd.Add_Click({
            Close-DialogAnimated $dlg $proot $false
            if ((Show-VaultGate -Modo 'cambia') -eq 'cambiato') {
                Set-Status 'Password cambiata: la chiave dei dati e'' la stessa, l''involucro e'' nuovo.'
            }
        }.GetNewClosure())
        $c.PSecOff.Add_Click({
            $r = [System.Windows.MessageBox]::Show(
                "Riportare la nota in chiaro?`n`nnote.dat, il suo backup, la scansione e la chiave vengono " +
                "rimossi e il testo torna leggibile in note.xaml.",
                'DuckNote', [System.Windows.MessageBoxButton]::YesNo, [System.Windows.MessageBoxImage]::Warning)
            if ($r -ne 'Yes') { return }
            Close-DialogAnimated $dlg $proot $false
            [void](Disable-NoteEncryption)
        }.GetNewClosure())
        $c.PWipe.Add_Click({
            Close-DialogAnimated $dlg $proot $false
            Show-UninstallDialog
        }.GetNewClosure())

        $c.PSysTheme.IsChecked = $script:Settings.FollowSystemTheme
        foreach ($it in $c.PTheme.Items) { if ([string]$it.Tag -eq $script:Settings.Theme) { $c.PTheme.SelectedItem = $it } }
        $c.PLiveFmt.IsChecked  = $script:Settings.LiveFormatting
        $c.PAutosave.IsChecked = $script:Settings.AutosaveEnabled
        $c.PAutoMs.Text        = "$($script:Settings.AutosaveDebounceMs)"
        $c.PMonitor.IsChecked  = $script:Settings.MonitorEnabled
        $c.PMonSec.Text        = "$($script:Settings.MonitorIntervalSec)"
        $c.PThreads.Text       = "$($script:Settings.MaxThreads)"
        $c.PParallel.IsChecked = ($script:Settings.UseParallel -and $script:IsPS7)
        $c.PParallel.IsEnabled = $script:IsPS7
        $c.PParallelInfo.Text  = if ($script:IsPS7) {
            'ForEach-Object -Parallel dentro un runspace di background: piu'' veloce e con meno memoria del pool di runspace.'
        } else {
            "Richiede PowerShell 7. In esecuzione su $($PSVersionTable.PSVersion): viene usato il RunspacePool."
        }
        $c.PPingN.Text         = "$($script:Settings.PingCount)"
        $c.PPingMs.Text        = "$($script:Settings.PingTimeoutMs)"
        $c.PPortMs.Text        = "$($script:Settings.PortTimeoutMs)"
        $c.PPorts.Text         = "$($script:Settings.Ports)"
        $c.PDead.IsChecked     = $script:Settings.ScanDeadHosts
        $c.PDns.IsChecked      = $script:Settings.ResolveDns
        $c.PDnsServer.Text     = "$($script:Settings.DnsServer)"
        $c.PNetBios.IsChecked  = $script:Settings.ProbeNetBios
        $c.PMdns.IsChecked     = $script:Settings.ProbeMdns
        $c.PSsdp.IsChecked     = $script:Settings.ProbeSsdp
        $c.PBanner.IsChecked   = $script:Settings.ProbeBanners
        $c.PSnmp.IsChecked     = $script:Settings.ProbeSnmp
        $c.PCommunity.Text     = "$($script:Settings.SnmpCommunity)"
        $c.PShares.IsChecked   = $script:Settings.ProbeShares
        $c.PWmi.IsChecked      = $script:Settings.ProbeWmi
        $c.PDucks.IsChecked    = $script:Settings.DuckBackground
        $c.PDuckN.Text         = "$($script:Settings.DuckCount)"
        $c.PDuckOp.Text        = "$($script:Settings.DuckOpacity)"
        $c.POuiInfo.Text       = ('{0} prefissi in memoria' -f $script:OuiMap.Count)
        $c.POui.Add_Click({
            $c.POuiInfo.Text = 'Scaricamento in corso...'
            $dlg.Cursor = [System.Windows.Input.Cursors]::Wait
            [void]$dlg.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::Render)
            $n = Update-OuiDatabase
            $dlg.Cursor = $null
            $c.POuiInfo.Text = if ($n -lt 0) { 'Download non riuscito (rete o proxy).' }
                               else { ('{0} prefissi caricati.' -f $n) }
        })

        $c.PTitleBar.Add_MouseLeftButtonDown({ try { $dlg.DragMove() } catch {} })
        $c.PClose.Add_Click({ Close-DialogAnimated $dlg $proot $false })
        $dlg.Add_KeyDown({
            param($s, $e)
            if ($e.Key -eq 'Escape') { Close-DialogAnimated $dlg $proot $false }
        })
        $c.PCancel.Add_Click({ Close-DialogAnimated $dlg $proot $false })
        $c.POk.Add_Click({
            $err = @()
            $iv = @{}
            foreach ($pair in @(
                @('PThreads','MaxThreads',1,512,'Analisi simultanee'),
                @('PPingN','PingCount',1,8,'Ping per host'),
                @('PPingMs','PingTimeoutMs',100,10000,'Timeout ping'),
                @('PPortMs','PortTimeoutMs',80,10000,'Timeout porta'),
                @('PAutoMs','AutosaveDebounceMs',200,20000,'Attesa salvataggio'),
                @('PMonSec','MonitorIntervalSec',5,86400,'Intervallo monitoraggio'),
                @('PLockMin','AutoLockMinutes',0,1440,'Blocca dopo'),
                @('PDuckN','DuckCount',0,60,'Numero di papere'),
                @('PDuckOp','DuckOpacity',0,30,'Intensita'''))) {
                $n = 0
                if (-not [int]::TryParse($c[$pair[0]].Text, [ref]$n) -or $n -lt $pair[2] -or $n -gt $pair[3]) {
                    $err += ('{0}: valore ammesso tra {1} e {2}.' -f $pair[4], $pair[2], $pair[3])
                } else { $iv[$pair[1]] = $n }
            }
            $dns = $c.PDnsServer.Text.Trim()
            $ipDns = $null
            if ($dns -and -not [Net.IPAddress]::TryParse($dns, [ref]$ipDns)) {
                $err += 'Server DNS: indica un indirizzo IP, oppure lascia vuoto per quello di sistema.'
            }
            if ($err.Count -gt 0) { $c.PError.Text = ($err -join '  '); return }

            $oldLive = $script:Settings.LiveFormatting
            foreach ($k in $iv.Keys) { $script:Settings[$k] = $iv[$k] }
            $script:Settings.FollowSystemTheme = [bool]$c.PSysTheme.IsChecked
            $script:Settings.LiveFormatting    = [bool]$c.PLiveFmt.IsChecked
            $script:Settings.AutosaveEnabled   = [bool]$c.PAutosave.IsChecked
            $script:Settings.MonitorEnabled    = [bool]$c.PMonitor.IsChecked
            $script:Settings.ScanDeadHosts     = [bool]$c.PDead.IsChecked
            $script:Settings.ResolveDns        = [bool]$c.PDns.IsChecked
            $script:Settings.DnsServer         = $dns
            $script:Settings.ProbeNetBios      = [bool]$c.PNetBios.IsChecked
            $script:Settings.ProbeMdns         = [bool]$c.PMdns.IsChecked
            $script:Settings.ProbeSsdp         = [bool]$c.PSsdp.IsChecked
            $script:Settings.ProbeBanners      = [bool]$c.PBanner.IsChecked
            $script:Settings.ProbeSnmp         = [bool]$c.PSnmp.IsChecked
            $script:Settings.ProbeShares       = [bool]$c.PShares.IsChecked
            $script:Settings.ProbeWmi          = [bool]$c.PWmi.IsChecked
            $script:Settings.DuckBackground    = [bool]$c.PDucks.IsChecked
            $script:Settings.UseParallel       = [bool]$c.PParallel.IsChecked
            $script:Settings.SnmpCommunity     = $c.PCommunity.Text.Trim()
            if ($c.PPorts.Text.Trim()) { $script:Settings.Ports = ($c.PPorts.Text -replace '\s+', ' ').Trim() }

            $newTheme = $script:Settings.Theme
            if ($c.PSysTheme.IsChecked) { $newTheme = Get-SystemTheme }
            elseif ($c.PTheme.SelectedItem) { $newTheme = [string]$c.PTheme.SelectedItem.Tag }

            Save-Settings
            Apply-Timers
            Apply-Theme $newTheme
            Start-DuckBackground
            Update-MonitorUi
            if ($oldLive -ne $script:Settings.LiveFormatting) { Format-AllParagraphs }
            Close-DialogAnimated $dlg $proot $true
            if ($script:Settings.ResolveDns) { Test-DnsServer $dns }
        })

        [void]$dlg.ShowDialog()
    } catch {
        [System.Windows.MessageBox]::Show("Impostazioni non disponibili: $_", 'DuckNote',
            [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning) | Out-Null
    }
}

function Show-TableDialog {
    $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Nuova tabella" SizeToContent="Height" Width="340" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        WindowStyle="None" AllowsTransparency="True" UseLayoutRounding="True"
        FontFamily="SF Pro Text, Segoe UI Variable Text, Segoe UI" FontSize="13"
        Background="Transparent">
  <Border CornerRadius="14" Background="{DynamicResource BgSidebarGlass}"
          BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
    <StackPanel>
      <Border x:Name="TTitleBar" Background="{DynamicResource BgToolbarGlass}" Height="44" CornerRadius="13,13,0,0">
        <Grid>
          <Button x:Name="TClose" Style="{DynamicResource TrafficBtn}" Background="{DynamicResource TLClose}"
                  HorizontalAlignment="Left" VerticalAlignment="Center" Margin="14,0,0,0" ToolTip="Chiudi">
            <Path Data="M 0,0 L 6,6 M 6,0 L 0,6" Stroke="#5A1416" StrokeThickness="1.2" Stretch="Uniform"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
          </Button>
          <TextBlock Text="Nuova tabella" FontSize="13" FontWeight="SemiBold" HorizontalAlignment="Center"
                     VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
        </Grid>
      </Border>

      <Border CornerRadius="12" Background="{DynamicResource SheetScrim}" Margin="18,16,18,8" Padding="15,13"
              BorderBrush="{DynamicResource GlassEdge}" BorderThickness="1">
        <StackPanel>
          <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
            <TextBlock Text="Righe" Width="100" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
            <TextBox x:Name="TRows" Style="{DynamicResource Field}" Width="80" TextAlignment="Right" Text="3"/>
          </StackPanel>
          <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
            <TextBlock Text="Colonne" Width="100" VerticalAlignment="Center" Foreground="{DynamicResource Label}"/>
            <TextBox x:Name="TCols" Style="{DynamicResource Field}" Width="80" TextAlignment="Right" Text="3"/>
          </StackPanel>
          <CheckBox x:Name="THead" Content="Prima riga come intestazione" IsChecked="True"/>
        </StackPanel>
      </Border>

      <Border Background="{DynamicResource BgToolbarGlass}" Padding="18,12" CornerRadius="0,0,13,13" Margin="0,8,0,0">
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
          <Button x:Name="TCancel" Style="{DynamicResource QuietBtn}" Content="Annulla" Margin="0,0,8,0"/>
          <Button x:Name="TOk" Style="{DynamicResource PrimaryBtn}" Content="Inserisci"/>
        </StackPanel>
      </Border>
    </StackPanel>
  </Border>
</Window>
'@
    try {
        $rd  = [System.Xml.XmlReader]::Create([System.IO.StringReader]$xaml)
        $dlg = [System.Windows.Markup.XamlReader]::Load($rd)
        try { [void]$dlg.Resources.MergedDictionaries.Add($script:Window.Resources) } catch {}
        $dlg.Owner = $script:Window
        $rows = $dlg.FindName('TRows'); $cols = $dlg.FindName('TCols'); $head = $dlg.FindName('THead')
        $dlg.FindName('TTitleBar').Add_MouseLeftButtonDown({ try { $dlg.DragMove() } catch {} })
        $dlg.FindName('TClose').Add_Click({ $dlg.DialogResult = $false; $dlg.Close() })
        $dlg.Add_KeyDown({
            param($s, $e)
            if ($e.Key -eq 'Escape') { Close-DialogAnimated $dlg $proot $false }
        })
        $dlg.FindName('TCancel').Add_Click({ $dlg.DialogResult = $false; $dlg.Close() })
        $dlg.FindName('TOk').Add_Click({
            $r = 3; $cN = 3
            [void][int]::TryParse($rows.Text, [ref]$r)
            [void][int]::TryParse($cols.Text, [ref]$cN)
            $r  = [Math]::Max(1, [Math]::Min(60, $r))
            $cN = [Math]::Max(1, [Math]::Min(16, $cN))
            $dlg.DialogResult = $true
            $dlg.Close()
            Insert-EditorTable -Rows $r -Cols $cN -Header ([bool]$head.IsChecked)
        })
        [void]$dlg.ShowDialog()
    } catch { Set-Status "Tabella non inserita: $_" }
}

function Apply-Timers {
    if ($script:LockTimer) {
        if ((Test-VaultOpen) -and $script:Settings.AutoLockMinutes -gt 0) { $script:LockTimer.Start() }
        else { $script:LockTimer.Stop() }
    }
    $script:SaveTimer.Interval    = [TimeSpan]::FromMilliseconds($script:Settings.AutosaveDebounceMs)
    $script:FormatTimer.Interval  = [TimeSpan]::FromMilliseconds($script:Settings.FormatDebounceMs)
    $script:HostsTimer.Interval   = [TimeSpan]::FromMilliseconds($script:Settings.HostDebounceMs)
    $script:MonitorTimer.Interval = [TimeSpan]::FromSeconds($script:Settings.MonitorIntervalSec)
    if ($script:Settings.MonitorEnabled) { $script:MonitorTimer.Start() } else { $script:MonitorTimer.Stop() }
}

function Invoke-EditorHistory {
    param([bool]$Redo)
    $script:SkipFormat = $true
    $script:FormatTimer.Stop()
    try { if ($Redo) { $script:Editor.Redo() } else { $script:Editor.Undo() } } catch {}
    [void]$script:Editor.Dispatcher.BeginInvoke([Action]{
        try {
            foreach ($p in (Get-AllParagraphs)) { $p.Tag = Get-ParagraphText $p }
            $script:DirtyParas.Clear()
            $script:LastPara   = Get-CaretParagraph
            $script:HostsCache = $null
        } catch {}
        $script:SkipFormat = $false
    }, [System.Windows.Threading.DispatcherPriority]::Background)
}

function Update-WindowClip {
    $g = $script:UI.RootGrid
    if ($null -eq $g) { return }
    $w = $g.ActualWidth; $h = $g.ActualHeight
    if ($w -le 0 -or $h -le 0) { return }
    $r = 10.0
    $geo = New-Object System.Windows.Media.RectangleGeometry
    $geo.Rect = New-Object System.Windows.Rect (0.0, 0.0, $w, $h)
    $geo.RadiusX = $r
    $geo.RadiusY = $r
    $g.Clip = $geo
    $script:UI.RootBorder.CornerRadius = New-Object System.Windows.CornerRadius $r
}

function Start-NoteScan {
    $hosts = @(Get-EditorHosts)
    if ($hosts.Count -eq 0) { Set-Status 'Nella nota non ci sono host da analizzare.'; return }
    Start-Scan -Targets $hosts
    Set-Status ('Analisi di {0} host della nota...' -f $hosts.Count)
}

function Start-RangeScan {
    $spec = $script:UI.RangeBox.Text.Trim()
    if (-not $spec) { Set-Status 'Indica un intervallo, per esempio 192.168.1.0/24.'; return }
    $targets = @(Expand-Targets $spec)
    if ($targets.Count -eq 0) { Set-Status 'Intervallo non riconosciuto.'; return }
    $script:Settings.LastRange = $spec
    Save-Settings
    Save-PrivateData
    Start-Scan -Targets $targets
    Set-Status ('Analisi di {0} indirizzi...' -f $targets.Count)
}

try {
    Load-Settings
    [void][DuckNative.Shell]::SetAppId('DuckNote.Note.1')
    if ($script:Settings.FollowSystemTheme) { $script:Settings.Theme = Get-SystemTheme }

    if (Test-VaultLocked) {
        if ((Show-VaultGate -Modo 'sblocca' -Trattieni) -ne 'aperto') { return }
        Read-PrivateData
        Remove-PlaintextLeftovers
    }
    elseif (-not $script:Settings.SecurityPrompted) {
        $script:Settings.SecurityPrompted = $true
        Save-Settings
        if ((Show-VaultGate -Modo 'crea' -Trattieni) -eq 'creato') { $script:CifraDopoAvvio = $true }
    }
    Read-IgnoredHosts

    $reader        = [System.Xml.XmlReader]::Create([System.IO.StringReader]$script:MainXaml)
    $script:Window = [System.Windows.Markup.XamlReader]::Load($reader)

    foreach ($n in @('RootGrid','RootBorder','BtnClose','BtnMin','BtnZoom','BtnSidebar','TabNote','TabNet',
                     'MonGlyph','MonLbl','BtnScan','BtnTheme','ThemeGlyph','BtnPrefs','ColSidebar','SidebarPanel',
                     'SideSearch','SideSearchHint','SideList','SideFoot','ViewNotes','ViewNet','Editor',
                     'FmtH1','FmtH2','FmtH3','FmtBold','FmtItalic','FmtUnder','FmtCode','FmtBullet',
                     'FmtNumber','FmtQuote','TblNew','TblRowAdd','TblColAdd','TblRowDel','TblColDel','FmtClear',
                     'RangeBox','RangeHint','BtnScanRange','BtnScanNote','BtnStop','GridFilter',
                     'GridFilterHint','BtnExport','BtnToNote','BtnInspect','GlyphInspect','NetGrid','CmbStatus',
                     'StatusText','ScanProgress','CountText',
                     'AppGrid','DuckLayer','LogoDuck','LogoVector','LogoImage','SideInner',
                     'SegHost','SegPill','SegPillT','SegPillS','ViewNotesT','ViewNetT',
                     'FindBarT','FindBarS','StatHosts','StatUp','StatDown',
                     'SideModeHost','SideModeOutline','WordCount','ZoomText',
                     'SideSegHost','SideSegPill','SideSegPillT','SideSegPillS','SideListT',
                     'FmtUndo','FmtRedo','FmtStrike','FmtMark','FmtCodeBlock','FmtTodo','FmtRule',
                     'FmtLink','FmtFind','FindBar','FindBox','FindHint','FindPrev','FindNext',
                     'FindCount','ReplBox','ReplHint','ReplOne','ReplAll','FindCase','FindClose',
                     'BtnFilterOpen','GlyphFilter','FilterWrap','FilterField','ColFilterSlot',
                     'LockVeil','VeilImg','VeilVec','VeilDuck','BtnLock',
                     'VeilShake','VeilRing1','VeilRing2','VeilRot1','VeilRot2','VeilSeal','VeilSealS',
                     'VeilDuckS','VeilDuckT','VeilSub','VeilCard','VeilForm','VeilPwd','VeilGo',
                     'VeilMsg','VeilWork','VeilLoader','VeilLoaderT','VeilBody','VeilBodyT','VeilTitle',
                     'HostMenu','HostMenuIgnore','HostMenuIgnored',
                     'EdMenu','EdMenuWatch','EdMenuPing','EdMenuCopyHost','EdMenuSep',
                     'EdMenuCut','EdMenuCopy','EdMenuPaste','EdMenuPasteRaw','EdMenuAll')) {
        $script:UI[$n] = $script:Window.FindName($n)
    }
    $script:Editor = $script:UI.Editor

    $script:UI.SideList.ItemsSource = $script:SideItems
    $script:UI.NetGrid.ItemsSource  = $script:Rows

    $script:UI.HostMenu.Resources = $script:Window.Resources
    $script:UI.EdMenu.Resources   = $script:Window.Resources

    $script:PumpTimer    = New-Object System.Windows.Threading.DispatcherTimer
    $script:PumpTimer.Interval = [TimeSpan]::FromMilliseconds(110)
    $script:PumpTimer.Add_Tick({ Pump-ScanResults })

    $script:SaveTimer    = New-Object System.Windows.Threading.DispatcherTimer
    $script:SaveTimer.Add_Tick({
        $script:SaveTimer.Stop()
        if ($script:Settings.AutosaveEnabled) { Save-Note -Silent }
        Update-WordCount
        if ($script:Settings.SidebarMode -eq 'outline') { Refresh-SideList }
    })

    $script:FormatTimer  = New-Object System.Windows.Threading.DispatcherTimer
    $script:FormatTimer.Add_Tick({ $script:FormatTimer.Stop(); Flush-DirtyParagraphs })

    $script:HostsTimer = New-Object System.Windows.Threading.DispatcherTimer
    $script:HostsTimer.Add_Tick({ Sync-NoteHosts })

    $script:TypingTimer = New-Object System.Windows.Threading.DispatcherTimer
    $script:TypingTimer.Interval = [TimeSpan]::FromMilliseconds(900)
    $script:TypingTimer.Add_Tick({ $script:TypingTimer.Stop(); Resume-Ducks })

    $script:FirstTimer = New-Object System.Windows.Threading.DispatcherTimer
    $script:FirstTimer.Interval = [TimeSpan]::FromMilliseconds(1200)
    $script:FirstTimer.Add_Tick({ $script:FirstTimer.Stop(); Start-FirstCheck })

    $script:LockTimer = New-Object System.Windows.Threading.DispatcherTimer
    $script:LockTimer.Interval = [TimeSpan]::FromSeconds(30)
    $script:LockTimer.Add_Tick({ Test-IdleLock })

    $script:MonitorTimer = New-Object System.Windows.Threading.DispatcherTimer
    $script:MonitorTimer.Add_Tick({
        if ($script:ScanActive) { return }
        $h = @(Get-EditorHosts)
        if ($h.Count -gt 0) { Start-Scan -Targets $h -KeepExisting }
    })

    $script:UI.BtnClose.Add_Click({ $script:Window.Close() })
    $script:UI.BtnMin.Add_Click({ $script:Window.WindowState = 'Minimized' })
    $script:UI.BtnZoom.Add_Click({
        if ($script:Zoomed) { Set-WindowZoom -Off } else { Set-WindowZoom }
    })
    $script:UI.DuckLayer.Add_SizeChanged({
        if ($null -eq $script:DuckResizeTimer) {
            $script:DuckResizeTimer = New-Object System.Windows.Threading.DispatcherTimer
            $script:DuckResizeTimer.Interval = [TimeSpan]::FromMilliseconds(350)
            $script:DuckResizeTimer.Add_Tick({ $script:DuckResizeTimer.Stop(); Build-Ducks })
        }
        $script:DuckResizeTimer.Stop(); $script:DuckResizeTimer.Start()
    })

    $script:UI.RootGrid.Add_SizeChanged({ Update-WindowClip; Sync-DetailBounds; Request-WindowSettle })
    $script:Window.Add_MouseDoubleClick({
        param($s, $e)
        try {
            if ($e.GetPosition($script:Window).Y -gt 52) { return }
            $src = $e.OriginalSource
            while ($null -ne $src) {
                if ($src -is [System.Windows.Controls.Primitives.ButtonBase]) { return }
                try { $src = [System.Windows.Media.VisualTreeHelper]::GetParent($src) } catch { break }
            }
            if ($script:Zoomed) { Set-WindowZoom -Off } else { Set-WindowZoom }
        } catch {}
    })

    $script:UI.TabNote.Add_Checked({ Move-SegPill 0; Switch-View 'note' })
    $script:UI.TabNet.Add_Checked({
        Move-SegPill 1
        Switch-View 'rete'
        if (-not $script:UI.GridFilter.Text) { Show-GridFilter -Hide -Immediate }
    })
    $script:UI.BtnSidebar.Add_Click({
        if ($script:UI.SidebarPanel.Visibility -eq 'Visible' -and $script:UI.ColSidebar.ActualWidth -gt 1) {
            $script:Settings.SidebarWidth = [int]$script:UI.ColSidebar.ActualWidth
            $script:UI.SideInner.Width = [double]$script:Settings.SidebarWidth
            Animate-Sidebar 0
        } else {
            $script:UI.SidebarPanel.Visibility = 'Visible'
            $script:UI.SideInner.Width = [double]$script:Settings.SidebarWidth
            Animate-Sidebar ([double]$script:Settings.SidebarWidth)
        }
    })
    $script:UI.SidebarPanel.Add_SizeChanged({
        if ($null -eq $script:SbAnim) { $script:UI.SideInner.Width = $script:UI.SidebarPanel.ActualWidth }
    })
    $script:UI.BtnInspect.Add_Click({
        if ($script:DetailOpen) { Hide-HostDetails } else { Show-HostDetails }
    })
    $script:Window.Add_LocationChanged({ Sync-DetailBounds; Request-WindowSettle })
    $script:Window.Add_StateChanged({
        if ($script:Window.WindowState -eq 'Maximized') {
            $script:Window.WindowState = 'Normal'
            Set-WindowZoom
            return
        }
        Update-WindowClip
        Sync-DetailBounds
    })
    $script:UI.BtnTheme.Add_Click({
        $script:Settings.FollowSystemTheme = $false
        $next = if ($script:Settings.Theme -eq 'dark') { 'light' } else { 'dark' }
        Apply-Theme $next
        Update-MonitorUi
        Save-Settings
    })
    $script:UI.BtnPrefs.Add_Click({ Show-Preferences })
    $script:UI.BtnLock.Add_Click({ Lock-Vault -Motivo 'a mano' })
    $script:UI.VeilGo.Add_Click({ Start-PanelUnlock })
    $script:UI.VeilPwd.Add_KeyDown({
        param($s, $e)
        if ($e.Key -eq 'Return') { Start-PanelUnlock; $e.Handled = $true }
    })
    $script:Window.Add_PreviewKeyDown({ Register-Activity })
    $script:Window.Add_PreviewMouseDown({ Register-Activity })
    $script:Window.Add_PreviewMouseMove({ Register-Activity })
    $script:DuckResizeTimer = $null
    $script:UI.BtnScan.Add_Click({ Start-NoteScan })

    $script:Editor.Add_TextChanged({
        if ($script:IsFormatting -or $script:SkipFormat) { return }
        Suspend-Ducks
        $script:TypingTimer.Stop(); $script:TypingTimer.Start()
        $script:HostsCache = $null
        $p = Get-CaretParagraph
        if ($p) { [void]$script:DirtyParas.Add($p) }
        if ($script:Settings.LiveFormatting) { $script:FormatTimer.Stop(); $script:FormatTimer.Start() }
        if ($script:Settings.AutosaveEnabled) { $script:SaveTimer.Stop(); $script:SaveTimer.Start() }
        $script:HostsTimer.Stop(); $script:HostsTimer.Start()
    })
    $script:Editor.Add_SelectionChanged({
        if ($script:IsFormatting -or $script:SkipFormat -or -not $script:Settings.LiveFormatting) { return }
        try { $sel = $script:Editor.Selection; if ($sel -and -not $sel.IsEmpty) { return } } catch { return }
        $cur = Get-CaretParagraph
        if ([object]::ReferenceEquals($script:LastPara, $cur)) { return }
        if ($null -ne $script:LastPara) {
            try { if ($null -ne $script:LastPara.Parent) { Format-Paragraph $script:LastPara } } catch {}
            [void]$script:DirtyParas.Remove($script:LastPara)
        }
        $script:LastPara = $cur
    })
    $script:Editor.Add_PreviewKeyDown({
        param($s, $e)
        $mods  = [System.Windows.Input.Keyboard]::Modifiers
        $ctrl  = ($mods -band [System.Windows.Input.ModifierKeys]::Control) -ne 0
        $shift = ($mods -band [System.Windows.Input.ModifierKeys]::Shift) -ne 0
        $alt   = ($mods -band [System.Windows.Input.ModifierKeys]::Alt) -ne 0
        if (-not $ctrl -and -not $alt -and ($e.Key -eq 'Space' -or $e.Key -eq 'Return')) {
            Request-EditorCommit
        }
        if ($ctrl -and $e.Key -eq 'S')        { Save-Note; $e.Handled = $true; return }
        if ($ctrl -and $e.Key -eq 'L')        { Lock-Vault -Motivo 'a mano'; $e.Handled = $true; return }
        if ($ctrl -and $e.Key -eq 'B')        { Wrap-Selection '**'; $e.Handled = $true; return }
        if ($ctrl -and $e.Key -eq 'I' -and -not $shift) { Wrap-Selection '*'; $e.Handled = $true; return }
        if ($ctrl -and $e.Key -eq 'U')        { Wrap-Selection '__'; $e.Handled = $true; return }
        if ($ctrl -and $shift -and $e.Key -eq 'X') { Wrap-Selection '~~'; $e.Handled = $true; return }
        if ($ctrl -and $shift -and $e.Key -eq 'M') { Wrap-Selection '=='; $e.Handled = $true; return }
        if ($ctrl -and $e.Key -eq 'OemComma'){ Show-Preferences; $e.Handled = $true; return }
        if ($ctrl -and $e.Key -eq 'F')        { Show-FindBar; $e.Handled = $true; return }
        if ($ctrl -and $e.Key -eq 'H')        { Show-FindBar; $script:UI.ReplBox.Focus(); $e.Handled = $true; return }
        if ($e.Key -eq 'F3')                  { Invoke-FindNext -Backward:$shift; $e.Handled = $true; return }
        if ($e.Key -eq 'Escape' -and $script:UI.FindBar.Visibility -eq 'Visible') {
            Show-FindBar -Hide; $e.Handled = $true; return
        }
        if ($ctrl -and $e.Key -eq 'D')        { Copy-EditorLine; $e.Handled = $true; return }
        if ($ctrl -and $e.Key -eq 'Return')   { Toggle-Todo; $e.Handled = $true; return }
        if ($alt -and $e.SystemKey -eq 'Up')   { Move-EditorLine -1; $e.Handled = $true; return }
        if ($alt -and $e.SystemKey -eq 'Down') { Move-EditorLine  1; $e.Handled = $true; return }
        if ($ctrl -and ($e.Key -eq 'OemPlus'  -or $e.Key -eq 'Add'))      { Set-EditorZoom ($script:Settings.EditorZoom + 10); $e.Handled = $true; return }
        if ($ctrl -and ($e.Key -eq 'OemMinus' -or $e.Key -eq 'Subtract')) { Set-EditorZoom ($script:Settings.EditorZoom - 10); $e.Handled = $true; return }
        if ($ctrl -and ($e.Key -eq 'D0' -or $e.Key -eq 'NumPad0'))        { Set-EditorZoom 100; $e.Handled = $true; return }
        if ($ctrl -and $shift -and $e.Key -eq 'V') { Paste-PlainText; $e.Handled = $true; return }
        if ($e.Key -eq 'Return' -and -not $shift -and -not $ctrl) {
            if (Invoke-SmartEnter) { $e.Handled = $true; return }
        }
        if ($ctrl -and ($e.Key -eq 'Z' -or $e.Key -eq 'Y')) {
            Invoke-EditorHistory (($e.Key -eq 'Y') -or ($e.Key -eq 'Z' -and $shift))
            $e.Handled = $true; return
        }
        if ($e.Key -eq 'Tab') {
            $ctx = Get-CurrentTableContext
            if ($ctx.Table) {
                $cells = $ctx.Row.Cells
                if ($ctx.ColIdx -lt ($cells.Count - 1)) {
                    $script:Editor.CaretPosition = $cells[$ctx.ColIdx + 1].ContentStart
                } elseif ($ctx.RowIdx -lt ($ctx.RowGroup.Rows.Count - 1)) {
                    $script:Editor.CaretPosition = $ctx.RowGroup.Rows[$ctx.RowIdx + 1].Cells[0].ContentStart
                } else {
                    Add-TableRow
                }
                $e.Handled = $true; return
            }
        }
    })
    $script:Editor.Add_PreviewMouseWheel({
        param($s, $e)
        if (([System.Windows.Input.Keyboard]::Modifiers -band [System.Windows.Input.ModifierKeys]::Control) -eq 0) { return }
        $step = if ($e.Delta -gt 0) { 10 } else { -10 }
        Set-EditorZoom ($script:Settings.EditorZoom + $step)
        $e.Handled = $true
    })
    $script:TodoDownPt = $null
    $script:Editor.Add_PreviewMouseLeftButtonDown({
        param($s, $e)
        try {
            $pt = $e.GetPosition($script:Editor)
            $script:TodoDownPt = if (Test-TodoAtPoint $pt) { $pt } else { $null }
        } catch { $script:TodoDownPt = $null }
    })
    $script:Editor.Add_PreviewMouseLeftButtonUp({
        param($s, $e)
        try {
            $down = $script:TodoDownPt
            $script:TodoDownPt = $null
            if ($null -eq $down) { return }
            $pt = $e.GetPosition($script:Editor)
            if ([Math]::Abs($pt.X - $down.X) -gt 3 -or [Math]::Abs($pt.Y - $down.Y) -gt 3) { return }
            if (-not (Test-TodoAtPoint $pt)) { return }
            $script:Editor.CaretPosition = $script:Editor.GetPositionFromPoint($pt, $false)
            Toggle-Todo
            $e.Handled = $true
        } catch {}
    })
    $script:LastCurPt   = New-Object System.Windows.Point -1, -1
    $script:LastCurHand = $false
    $script:Editor.AddHandler(
        [System.Windows.Input.Mouse]::QueryCursorEvent,
        [System.Windows.Input.QueryCursorEventHandler]{
            param($s, $e)
            try {
                if ([System.Windows.Input.Mouse]::LeftButton -eq 'Pressed') { return }
                $pt = $e.GetPosition($script:Editor)
                if ([Math]::Abs($pt.X - $script:LastCurPt.X) -ge 2 -or
                    [Math]::Abs($pt.Y - $script:LastCurPt.Y) -ge 2) {
                    $script:LastCurPt   = $pt
                    $script:LastCurHand = Test-TodoAtPoint $pt
                }
                if ($script:LastCurHand) {
                    $e.Cursor  = [System.Windows.Input.Cursors]::Hand
                    $e.Handled = $true
                }
            } catch {}
        },
        $true)

    $script:Window.Add_PreviewKeyDown({
        param($s, $e)
        if ($e.Key -eq 'F5') {
            if ($script:UI.TabNet.IsChecked) { Start-RangeScan } else { Start-NoteScan }
            $e.Handled = $true
        }
        elseif ($e.Key -eq 'Escape' -and $script:ScanActive) { Stop-Scan; $e.Handled = $true }
    })

    $script:UI.FmtH1.Add_Click({ Set-LinePrefix '# '   -Toggle })
    $script:UI.FmtH2.Add_Click({ Set-LinePrefix '## '  -Toggle })
    $script:UI.FmtH3.Add_Click({ Set-LinePrefix '### ' -Toggle })
    $script:UI.FmtQuote.Add_Click({ Set-LinePrefix '> ' -Toggle })
    $script:UI.FmtBold.Add_Click({ Wrap-Selection '**' })
    $script:UI.FmtItalic.Add_Click({ Wrap-Selection '*' })
    $script:UI.FmtUnder.Add_Click({ Wrap-Selection '__' })
    $script:UI.FmtCode.Add_Click({ Wrap-Selection ([string][char]0x60) })
    $script:UI.FmtClear.Add_Click({ Clear-Formatting })
    $script:UI.FmtBullet.Add_Click({
        try { [System.Windows.Documents.EditingCommands]::ToggleBullets.Execute($null, $script:Editor) } catch {}
    })
    $script:UI.FmtNumber.Add_Click({
        try { [System.Windows.Documents.EditingCommands]::ToggleNumbering.Execute($null, $script:Editor) } catch {}
    })
    $script:UI.FmtStrike.Add_Click({ Wrap-Selection '~~' })
    $script:UI.FmtMark.Add_Click({ Wrap-Selection '==' })
    $script:UI.FmtCodeBlock.Add_Click({ Insert-CodeBlock })
    $script:UI.FmtTodo.Add_Click({ Toggle-Todo })
    $script:UI.FmtRule.Add_Click({ Insert-Rule })
    $script:UI.FmtLink.Add_Click({ Insert-Link })
    $script:UI.FmtFind.Add_Click({ Show-FindBar })
    $script:UI.FmtUndo.Add_Click({ Invoke-EditorHistory $false })
    $script:UI.FmtRedo.Add_Click({ Invoke-EditorHistory $true })
    $script:UI.TblNew.Add_Click({ Show-TableDialog })
    $script:UI.TblRowAdd.Add_Click({ Add-TableRow })
    $script:UI.TblColAdd.Add_Click({ Add-TableColumn })
    $script:UI.TblRowDel.Add_Click({ Remove-TableRow })
    $script:UI.TblColDel.Add_Click({ Remove-TableColumn })

    $script:UI.FindNext.Add_Click({ Invoke-FindNext })
    $script:UI.FindPrev.Add_Click({ Invoke-FindNext -Backward })
    $script:UI.ReplOne.Add_Click({ Invoke-ReplaceOne })
    $script:UI.ReplAll.Add_Click({ Invoke-ReplaceAll })
    $script:UI.FindClose.Add_Click({ Show-FindBar -Hide })
    $script:UI.FindCase.Add_Click({ Update-FindStatus })
    $script:UI.FindBox.Add_TextChanged({
        $script:UI.FindHint.Visibility = if ($script:UI.FindBox.Text) { 'Collapsed' } else { 'Visible' }
        Update-FindStatus
    })
    $script:UI.ReplBox.Add_TextChanged({
        $script:UI.ReplHint.Visibility = if ($script:UI.ReplBox.Text) { 'Collapsed' } else { 'Visible' }
    })
    $script:UI.FindBox.Add_KeyDown({
        param($s, $e)
        if ($e.Key -eq 'Return') {
            if (([System.Windows.Input.Keyboard]::Modifiers -band [System.Windows.Input.ModifierKeys]::Shift) -ne 0) {
                Invoke-FindNext -Backward
            } else { Invoke-FindNext }
            $e.Handled = $true
        } elseif ($e.Key -eq 'Escape') { Show-FindBar -Hide; $e.Handled = $true }
    })
    $script:UI.ReplBox.Add_KeyDown({
        param($s, $e)
        if ($e.Key -eq 'Return')      { Invoke-ReplaceOne; $e.Handled = $true }
        elseif ($e.Key -eq 'Escape')  { Show-FindBar -Hide; $e.Handled = $true }
    })

    $script:UI.SideModeHost.Add_Checked({ Switch-SideMode 'host' })
    $script:UI.SideModeOutline.Add_Checked({ Switch-SideMode 'outline' })
    $script:UI.SideSegHost.Add_SizeChanged({
        Move-SideSegPill ([int]($script:Settings.SidebarMode -eq 'outline')) -Immediate
    })

    $script:UI.BtnScanRange.Add_Click({ Start-RangeScan })
    $script:UI.BtnScanNote.Add_Click({ Start-NoteScan; $script:UI.TabNet.IsChecked = $true })
    $script:UI.BtnStop.Add_Click({ Stop-Scan })
    $script:UI.BtnExport.Add_Click({ Export-ScanCsv })
    $script:UI.BtnToNote.Add_Click({ Send-ScanToNote })
    $script:UI.CmbStatus.Add_SelectionChanged({ Apply-GridFilter })
    $script:UI.GridFilter.Add_TextChanged({
        $script:UI.GridFilterHint.Visibility = if ($script:UI.GridFilter.Text) { 'Collapsed' } else { 'Visible' }
        Update-FilterGlyph
        Apply-GridFilter
    })
    $script:UI.BtnFilterOpen.Add_Click({
        if ($script:FilterOpen -and -not $script:UI.GridFilter.Text) { Show-GridFilter -Hide }
        else { Show-GridFilter }
    })
    $script:UI.GridFilter.Add_LostKeyboardFocus({
        if (-not $script:UI.GridFilter.Text) { Show-GridFilter -Hide }
    })
    $script:UI.GridFilter.Add_KeyDown({
        param($s, $e)
        if ($e.Key -eq 'Escape') {
            $script:UI.GridFilter.Text = ''
            Show-GridFilter -Hide
            $e.Handled = $true
        }
    })
    $script:UI.RangeBox.Add_TextChanged({
        $script:UI.RangeHint.Visibility = if ($script:UI.RangeBox.Text) { 'Collapsed' } else { 'Visible' }
    })
    $script:UI.RangeBox.Add_KeyDown({
        param($s, $e)
        if ($e.Key -eq 'Return') { Start-RangeScan; $e.Handled = $true }
    })
    $script:UI.SideSearch.Add_TextChanged({
        $script:UI.SideSearchHint.Visibility = if ($script:UI.SideSearch.Text) { 'Collapsed' } else { 'Visible' }
        Refresh-SideList
    })
    $script:UI.NetGrid.Add_SelectionChanged({
        $r = $script:UI.NetGrid.SelectedItem
        if ($null -eq $r) { return }
        Show-Inspector $r
        if (-not $script:SyncingSide -and -not $script:DetailOpen) { Show-HostDetails }
        Select-SideHost $r.IP
    })
    $script:UI.NetGrid.Add_MouseDoubleClick({
        $r = $script:UI.NetGrid.SelectedItem
        if ($r -and $r.PortCount -gt 0) {
            $p = if ($r.OpenPorts -match '\b443\b') { 'https://' + $r.IP } else { 'http://' + $r.IP }
            if ($r.OpenPorts -match '\b(80|443)\b') { try { Start-Process $p } catch {} }
        }
    })
    $script:UI.SideList.Add_ContextMenuOpening({
        param($s, $e)
        Prepare-HostMenu $e.OriginalSource
    })
    $script:UI.HostMenuIgnore.Add_Click({ Ignore-Host $script:MenuHost })

    $script:Editor.Add_ContextMenuOpening({
        param($s, $e)
        $pos = if ($e.CursorLeft -ge 0) {
            $script:Editor.GetPositionFromPoint((New-Object System.Windows.Point $e.CursorLeft, $e.CursorTop), $true)
        } else {
            $script:Editor.CaretPosition
        }
        Prepare-EditorMenu $pos
    })
    $script:UI.EdMenuWatch.Add_Click({
        if (-not $script:MenuHost) { return }
        if ($script:Ignored.Contains($script:MenuHost)) { Restore-Host $script:MenuHost }
        else { Ignore-Host $script:MenuHost }
    })
    $script:UI.EdMenuPing.Add_Click({
        if (-not $script:MenuHost) { return }
        Start-Scan -Targets @($script:MenuHost) -KeepExisting
        Set-Status ('Analisi di {0}...' -f $script:MenuHost)
    })
    $script:UI.EdMenuCopyHost.Add_Click({
        if ($script:MenuHost) { try { [System.Windows.Clipboard]::SetText($script:MenuHost) } catch {} }
    })
    $script:UI.EdMenuCut.Add_Click({ $script:Editor.Cut() })
    $script:UI.EdMenuCopy.Add_Click({ $script:Editor.Copy() })
    $script:UI.EdMenuPaste.Add_Click({ $script:Editor.Paste() })
    $script:UI.EdMenuPasteRaw.Add_Click({ Paste-PlainText })
    $script:UI.EdMenuAll.Add_Click({ $script:Editor.SelectAll() })
    $script:UI.SideList.Add_SelectionChanged({
        $it = $script:UI.SideList.SelectedItem
        if ($null -eq $it) { return }
        if ($null -ne $it.Para) {
            try {
                $script:UI.TabNote.IsChecked = $true
                $script:Editor.CaretPosition = $it.Para.ContentEnd
                $r = $it.Para.ContentStart.GetCharacterRect([System.Windows.Documents.LogicalDirection]::Forward)
                $script:Editor.ScrollToVerticalOffset($script:Editor.VerticalOffset + $r.Top - 60)
                $script:Editor.Focus()
            } catch {}
            return
        }
        if ($it.IP -and $script:RowIndex.ContainsKey($it.IP)) {
            $row = $script:RowIndex[$it.IP]
            Show-Inspector $row
            if ($script:SyncingSide) { return }
            if (-not $script:DetailOpen) { Show-HostDetails }
            $script:SyncingSide = $true
            try {
                $script:UI.NetGrid.SelectedItem = $row
                $script:UI.NetGrid.ScrollIntoView($row)
            } catch {} finally { $script:SyncingSide = $false }
        }
    })

    $script:Window.Add_Closing({
        try { $script:SaveTimer.Stop(); $script:FormatTimer.Stop(); $script:MonitorTimer.Stop() } catch {}
        try { $script:LockTimer.Stop() } catch {}
        try { $script:HostsTimer.Stop(); $script:TypingTimer.Stop() } catch {}
        try { Stop-DuckBackground } catch {}
        try { if ($script:ScanActive) { Stop-Scan } } catch {}
        try { Stop-ParallelScan; Clear-ParallelScan } catch {}
        try { $script:PumpTimer.Stop() } catch {}
        try {
            $bnd = if ($script:Zoomed -and $script:ZoomRestore) { $script:ZoomRestore }
                   else { @{ Width = $script:Window.ActualWidth; Height = $script:Window.ActualHeight } }
            $script:Settings.WindowWidth  = [int]$bnd.Width
            $script:Settings.WindowHeight = [int]$bnd.Height
            if ($script:UI.SidebarPanel.Visibility -eq 'Visible') {
                $script:Settings.SidebarWidth = [int]$script:UI.ColSidebar.ActualWidth
            }
            Save-Settings
            Save-PrivateData
        } catch {}
        Save-Note -Silent
        Clear-VaultKey
        Clear-DuckIcons
        try { if ($script:Pool) { $script:Pool.Close(); $script:Pool.Dispose() } } catch {}
    })

    $script:Window.Add_Loaded({
        [void](Initialize-DuckImage)
        Apply-DuckLogo
        Apply-Theme $script:Settings.Theme
        Move-SegPill 0 -Immediate
        $script:UI.TabNote.IsChecked = $true
        Apply-Timers
        Update-MonitorUi
        Load-Note
        if ($script:CifraDopoAvvio) { Convert-DataToVault }
        Set-EditorZoom $script:Settings.EditorZoom
        if ($script:Settings.SidebarMode -eq 'outline') { $script:UI.SideModeOutline.IsChecked = $true }
        Start-DuckBackground
        Update-WindowClip
        Update-WordCount
        Update-HeaderStats
        Update-FilterGlyph
        Update-InspectGlyph
        $script:UI.ColSidebar.Width = New-Object System.Windows.GridLength ([double]$script:Settings.SidebarWidth)
        $r = $script:Settings.LastRange
        if (-not $r) { $r = @(Get-LocalRanges) | Select-Object -First 1 }
        if ($r) { $script:UI.RangeBox.Text = $r }
        Register-Activity
        Update-LockButton
        $ripresi = Restore-ScanSnapshot
        if ($ripresi -gt 0) {
            Refresh-EditorDots
            Update-ScanUi
            Update-HeaderStats
        }
        Refresh-SideList
        $script:LastHostsSeen = (@(Get-EditorHosts) -join ',')
        foreach ($h in (Get-EditorHosts)) { [void]$script:FirstChecked.Add($h) }
        $eng = if ($script:IsPS7 -and $script:Settings.UseParallel) {
            "motore parallelo, $($script:Settings.MaxThreads) thread"
        } else { "pool di runspace, $($script:Settings.MaxThreads) thread" }
        Set-Status "Pronto ($eng). F5 analizza, Ctrl+S salva, Esc ferma la scansione." 
        try { $script:Editor.Focus() } catch {}
        if ($script:Settings.MonitorEnabled) {
            $h = @(Get-EditorHosts)
            if ($h.Count -gt 0) { Start-Scan -Targets $h -KeepExisting }
        }
        Close-GateOverlay
    })

    if ($script:Settings.WindowWidth  -ge 900) { $script:Window.Width  = $script:Settings.WindowWidth }
    if ($script:Settings.WindowHeight -ge 560) { $script:Window.Height = $script:Settings.WindowHeight }

    [void]$script:Window.ShowDialog()

} catch {
    $msg = "Avvio non riuscito.`n`n$($_.Exception.Message)`n`n$($_.ScriptStackTrace)"
    try {
        [System.Windows.MessageBox]::Show($msg, 'DuckNote', [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]::Error) | Out-Null
    } catch { Write-Error $msg }
} finally {
    try { if ($script:Pool) { $script:Pool.Close(); $script:Pool.Dispose() } } catch {}
}
