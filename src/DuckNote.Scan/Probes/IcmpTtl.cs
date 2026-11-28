using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace DuckNote.Scan.Probes;

/// <summary>
/// Il TTL vero della risposta di un host, su macOS e Linux.
/// <para>
/// Su questi sistemi il <see cref="System.Net.NetworkInformation.Ping"/> di .NET mette nella
/// risposta le opzioni del pacchetto <em>mandato</em>: il TTL che si legge e' quello che si e'
/// chiesto (128 di serie), non quello con cui l'host ha risposto. Ogni host sembrava Windows.
/// Qui si manda un echo ICMP a mano e si legge il TTL dall'intestazione IP della risposta:
/// un socket raw (se il processo puo') o datagramma (macOS lo concede a tutti e consegna la
/// risposta con l'intestazione IP; Linux no: li' senza socket raw il TTL resta sconosciuto).
/// </para>
/// <para>
/// Su macOS un socket ICMP riceve le risposte di tutti: la nostra si riconosce dall'indirizzo e
/// dal contrassegno casuale nei dati, che l'host rimanda tale e quale.
/// </para>
/// </summary>
public static class IcmpTtl
{
    private const int EchoReply = 0;
    private const int EchoRequest = 8;

    /// <summary>Il TTL della risposta, o 0 se non si e' potuto leggere (host muto, IPv6, permessi).</summary>
    public static async Task<int> ReadAsync(string target, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows() || !IPAddress.TryParse(target, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return 0;
        }

        foreach (SocketType type in (SocketType[])[SocketType.Raw, SocketType.Dgram])
        {
            Socket? socket = Open(type);
            if (socket is null)
            {
                continue;
            }

            using (socket)
            {
                return await ExchangeAsync(socket, address, timeout, cancellationToken).ConfigureAwait(false);
            }
        }

        return 0;
    }

    private static Socket? Open(SocketType type)
    {
        try
        {
            return new Socket(AddressFamily.InterNetwork, type, ProtocolType.Icmp);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static async Task<int> ExchangeAsync(Socket socket, IPAddress address, TimeSpan timeout, CancellationToken cancellationToken)
    {
        byte[] marker = RandomNumberGenerator.GetBytes(16);
        ushort identifier = BinaryPrimitives.ReadUInt16BigEndian(marker);
        ushort sequence = BinaryPrimitives.ReadUInt16BigEndian(marker.AsSpan(2));
        byte[] request = EchoPacket(identifier, sequence, marker);

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            await socket.SendToAsync(request, SocketFlags.None, new IPEndPoint(address, 0), deadline.Token).ConfigureAwait(false);

            byte[] buffer = new byte[1500];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                SocketReceiveFromResult received = await socket
                    .ReceiveFromAsync(buffer, SocketFlags.None, from, deadline.Token)
                    .ConfigureAwait(false);

                if (received.RemoteEndPoint is IPEndPoint source && source.Address.Equals(address))
                {
                    int ttl = TtlOf(buffer.AsSpan(0, received.ReceivedBytes), marker);
                    if (ttl >= 0)
                    {
                        return ttl;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (SocketException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Il TTL di un pacchetto ricevuto se e' la risposta al nostro echo: -1 se non lo e', 0 se lo
    /// e' ma senza intestazione IP (un socket datagramma su Linux), altrimenti il TTL.
    /// </summary>
    public static int TtlOf(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> marker)
    {
        int header = 0;
        int ttl = 0;
        // con l'intestazione IPv4 (versione 4, lunghezza 20-60 byte) il TTL e' il nono byte
        if (packet.Length >= 28 && packet[0] >> 4 == 4)
        {
            int length = (packet[0] & 0x0F) * 4;
            if (length >= 20 && packet.Length >= length + 8)
            {
                header = length;
                ttl = packet[8];
            }
        }

        ReadOnlySpan<byte> icmp = packet[header..];
        if (icmp.Length < 8 + marker.Length || icmp[0] != EchoReply || icmp[1] != 0)
        {
            return -1;
        }

        // i dati tornano uguali: il contrassegno dice che e' la nostra (l'identificativo lo puo' cambiare il sistema)
        return icmp.Slice(8, marker.Length).SequenceEqual(marker) ? ttl : -1;
    }

    /// <summary>Un echo ICMP: tipo 8, codice 0, somma di controllo, identificativo, sequenza, dati.</summary>
    public static byte[] EchoPacket(ushort identifier, ushort sequence, ReadOnlySpan<byte> data)
    {
        byte[] packet = new byte[8 + data.Length];
        packet[0] = EchoRequest;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), identifier);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), sequence);
        data.CopyTo(packet.AsSpan(8));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), Checksum(packet));
        return packet;
    }

    private static ushort Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 1 < data.Length; i += 2)
        {
            sum += (uint)((data[i] << 8) | data[i + 1]);
        }
        if (i < data.Length)
        {
            sum += (uint)(data[i] << 8);
        }
        while (sum >> 16 != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }
        return (ushort)~sum;
    }
}
