using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DuckNote.Scan.Probes;

public static class HttpProbe
{
    private const string SubjectAlternativeNameOid = "2.5.29.17";
    private const int MaximumResponse = 64 * 1024;

    public static async Task<HttpFindings> AskAsync(
        string address, int port, bool useTls, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout * 2);

        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(address, port, deadline.Token).ConfigureAwait(false);

            await using NetworkStream raw = client.GetStream();
            if (!useTls)
            {
                string plain = await ExchangeAsync(raw, address, deadline.Token).ConfigureAwait(false);
                return HttpResponseReader.Read(plain);
            }

            await using SslStream secure = new(raw, leaveInnerStreamOpen: false, (_, _, _, _) => true);
            await AuthenticateAsync(secure, address, deadline.Token).ConfigureAwait(false);

            HttpFindings certificate = ReadCertificate(secure);
            string body = await ExchangeAsync(secure, address, deadline.Token).ConfigureAwait(false);
            HttpFindings page = HttpResponseReader.Read(body);

            return page with
            {
                TlsSubject = certificate.TlsSubject,
                TlsIssuer = certificate.TlsIssuer,
                TlsExpiry = certificate.TlsExpiry,
                TlsProtocol = certificate.TlsProtocol,
                TlsSubjectAlternativeNames = certificate.TlsSubjectAlternativeNames
            };
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException
                                      or OperationCanceledException or InvalidOperationException)
        {
            return HttpFindings.Empty;
        }
    }

    private static async Task AuthenticateAsync(SslStream stream, string address, CancellationToken cancellationToken)
    {
        SslClientAuthenticationOptions options = new()
        {
            TargetHost = address,
            EnabledSslProtocols = SslProtocols.None,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        };

        await stream.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
    }

    private static HttpFindings ReadCertificate(SslStream stream)
    {
        if (stream.RemoteCertificate is null)
        {
            return HttpFindings.Empty with { TlsProtocol = stream.SslProtocol.ToString() };
        }

        using X509Certificate2 certificate = new(stream.RemoteCertificate);
        string alternativeNames = string.Empty;

        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension.Oid?.Value == SubjectAlternativeNameOid)
            {
                alternativeNames = TextSanitiser.Clean(extension.Format(false), 200);
            }
        }

        return HttpFindings.Empty with
        {
            TlsSubject = TextSanitiser.Clean(certificate.Subject, 160),
            TlsIssuer = TextSanitiser.Clean(certificate.Issuer, 160),
            TlsExpiry = certificate.NotAfter.ToString("yyyy-MM-dd"),
            TlsProtocol = stream.SslProtocol.ToString(),
            TlsSubjectAlternativeNames = alternativeNames
        };
    }

    private static async Task<string> ExchangeAsync(Stream stream, string address, CancellationToken cancellationToken)
    {
        byte[] request = Encoding.ASCII.GetBytes(
            $"GET / HTTP/1.1\r\nHost: {address}\r\nUser-Agent: DuckNote/2.0\r\nAccept: */*\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        using MemoryStream received = new();
        byte[] buffer = new byte[4096];

        while (received.Length < MaximumResponse)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }
            received.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(received.ToArray());
    }
}
