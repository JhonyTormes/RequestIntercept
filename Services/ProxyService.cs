using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using RequestIntercept.Models;

namespace RequestIntercept.Services;

public class ProxyService : BackgroundService
{
    private readonly ILogger<ProxyService> _logger;
    private readonly CertificateService _certService;
    private readonly RequestStore _store;
    private readonly int _proxyPort;
    private TcpListener? _listener;
    private static readonly Encoding HeaderEncoding = Encoding.ASCII;
    private static readonly Encoding BodyEncoding = Encoding.UTF8;
    private const int MaxBodySize = 2 * 1024 * 1024; // 2 MB

    public int ProxyPort { get; }

    public ProxyService(ILogger<ProxyService> logger, CertificateService certService,
        RequestStore store, IConfiguration config)
    {
        _logger = logger;
        _certService = certService;
        _store = store;
        _proxyPort = config.GetValue<int>("Proxy:Port", 8888);
        ProxyPort = _proxyPort;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new TcpListener(IPAddress.Any, _proxyPort);
        _listener.Start();
        _logger.LogInformation("[Proxy] Listening on 0.0.0.0:{Port}", _proxyPort);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _listener?.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 30000;
                client.SendTimeout = 30000;
                var stream = client.GetStream();

                var line = await ReadLineAsync(stream, ct);
                if (line is null) return;

                var parts = line.Split(' ');
                if (parts.Length < 3) return;

                var method = parts[0];
                var rawUrl = parts[1];

            if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                // Read and discard remaining CONNECT headers before TLS handshake
                await ReadHeadersAsync(stream, ct);
                await HandleConnectTunnel(stream, rawUrl, ct);
            }
                else
                {
                    var version = parts[2];
                    await HandleHttpRequest(stream, method, rawUrl, version, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Proxy] Client handling error: {Message}", ex.Message);
        }
    }

    private async Task HandleConnectTunnel(NetworkStream clientStream, string hostPort, CancellationToken ct)
    {
        var (hostname, port) = ParseHostPort(hostPort, 443);
        _logger.LogDebug("[Proxy] CONNECT {Host}:{Port}", hostname, port);

        // Send 200 Connection Established
        await clientStream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), ct);

        // Get certificate for this host
        X509Certificate2 cert;
        try
        {
            cert = _certService.GetOrCreateHostCertificate(hostname);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Proxy] Failed to get cert for {Host}: {Message}", hostname, ex.Message);
            return;
        }

        // Establish TLS with client
        var clientSsl = new SslStream(clientStream, false,
            (sender, certificate, chain, sslPolicyErrors) => true);
        try
        {
            await clientSsl.AuthenticateAsServerAsync(cert, false, SslProtocols.Tls12, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Proxy] TLS handshake failed: {Msg}", ex.Message);
            return;
        }

        // Now read HTTP request from the decrypted client stream
        await HandleHttpOverSsl(clientSsl, hostname, port, ct);
    }

    private async Task HandleHttpOverSsl(SslStream clientSsl, string hostname, int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await ReadLineAsync(clientSsl, ct);
            if (line is null) break;

            var parts = line.Split(' ');
            if (parts.Length < 2) break;

            var method = parts[0];
            var path = parts[1];
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(path);
                path = uri.PathAndQuery;
            }

            var (headers, bodyBytes) = await ReadRequestDetails(clientSsl, ct, method);

            var sw = Stopwatch.StartNew();
            var record = new InterceptedRequest
            {
                Method = method,
                Url = $"https://{hostname}:{port}{path}",
                Host = hostname,
                IsHttps = true,
                RequestHeaders = headers,
                RequestBody = bodyBytes is not null ? TryDecodeBody(headers, bodyBytes) : null
            };

            try
            {
                using var server = new TcpClient();
                await server.ConnectAsync(hostname, port, ct);
                using var serverSsl = new SslStream(server.GetStream(), false,
                    (_, _, _, _) => true);
                await serverSsl.AuthenticateAsClientAsync(hostname);

                var reqText = BuildHttpRequest(method, path, headers, bodyBytes);
                await serverSsl.WriteAsync(reqText, ct);

                var (statusCode, respHeaders, respBody) = await ReadHttpResponseAsync(serverSsl, ct);

                record.StatusCode = statusCode;
                record.ResponseHeaders = respHeaders;
                record.ResponseBody = respBody is not null ? TryDecodeBody(respHeaders, respBody) : null;

                var respText = BuildHttpResponse(statusCode, respHeaders, respBody);
                await clientSsl.WriteAsync(respText, ct);

                record.DurationMs = sw.ElapsedMilliseconds;
                _store.Add(record);
            }
            catch (Exception ex)
            {
                record.Error = ex.Message;
                record.DurationMs = sw.ElapsedMilliseconds;
                _store.Add(record);
                break;
            }

            // Check for keep-alive
            if (!ShouldKeepAlive(headers)) break;
        }
    }

    private async Task HandleHttpRequest(NetworkStream clientStream, string method,
        string rawUrl, string version, CancellationToken ct)
    {
        var uri = new Uri(rawUrl);
        var hostname = uri.Host;
        var port = uri.Port;
        var path = uri.PathAndQuery;

        var (headers, bodyBytes) = await ReadRequestDetails(clientStream, ct, method);

        var sw = Stopwatch.StartNew();
        var record = new InterceptedRequest
        {
            Method = method,
            Url = rawUrl,
            Host = hostname,
            IsHttps = false,
            RequestHeaders = headers,
            RequestBody = bodyBytes is not null ? TryDecodeBody(headers, bodyBytes) : null
        };

        try
        {
            using var server = new TcpClient();
            await server.ConnectAsync(hostname, port, ct);
            var serverStream = server.GetStream();

            var reqText = BuildHttpRequest(method, path, headers, bodyBytes);
            await serverStream.WriteAsync(reqText, ct);

            var (statusCode, respHeaders, respBody) = await ReadHttpResponseAsync(serverStream, ct);

            record.StatusCode = statusCode;
            record.ResponseHeaders = respHeaders;
            record.ResponseBody = respBody is not null ? TryDecodeBody(respHeaders, respBody) : null;

            var respText = BuildHttpResponse(statusCode, respHeaders, respBody);
            await clientStream.WriteAsync(respText, ct);

            record.DurationMs = sw.ElapsedMilliseconds;
            _store.Add(record);
        }
        catch (Exception ex)
        {
            record.Error = ex.Message;
            record.DurationMs = sw.ElapsedMilliseconds;
            _store.Add(record);
        }
    }

    private async Task<(Dictionary<string, string[]> Headers, byte[]? Body)> ReadRequestDetails(
        Stream stream, CancellationToken ct, string method)
    {
        var headers = await ReadHeadersAsync(stream, ct);
        var body = await ReadBodyAsync(stream, headers, ct);
        return (headers, body);
    }

    // ---- HTTP Parsing Helpers ----

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var sb = new StringBuilder(256);
        var prev = -1;
        byte[] buf = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
            if (read == 0) return sb.Length > 0 ? sb.ToString() : null;
            var b = buf[0];
            if (prev == '\r' && b == '\n') return sb.ToString();
            if (b != '\r' && b != '\n') sb.Append((char)b);
            prev = b;
            if (sb.Length > 8192) return null;
        }
    }

    private static async Task<Dictionary<string, string[]>> ReadHeadersAsync(Stream stream, CancellationToken ct)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await ReadLineAsync(stream, ct);
            if (string.IsNullOrEmpty(line)) break;
            var colonPos = line.IndexOf(':');
            if (colonPos > 0)
            {
                var name = line[..colonPos].Trim();
                var value = line[(colonPos + 1)..].Trim();
                headers[name] = [value];
            }
        }
        return headers;
    }

    private static async Task<byte[]?> ReadBodyAsync(Stream stream, Dictionary<string, string[]> headers,
        CancellationToken ct)
    {
        if (headers.TryGetValue("content-length", out var cl))
        {
            if (int.TryParse(cl[0], out var len) && len > 0)
            {
                len = Math.Min(len, MaxBodySize);
                return await ReadExactlyAsync(stream, len, ct);
            }
        }

        if (headers.TryGetValue("transfer-encoding", out var te) &&
            te.Any(v => v.Contains("chunked", StringComparison.OrdinalIgnoreCase)))
        {
            return await ReadChunkedBodyAsync(stream, ct);
        }

        return null;
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return buffer;
    }

    private static async Task<byte[]?> ReadChunkedBodyAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var line = await ReadLineAsync(stream, ct);
            if (line is null) break;
            var chunkSizeStr = line.Split(';')[0].Trim();
            if (!int.TryParse(chunkSizeStr, System.Globalization.NumberStyles.HexNumber, null, out var size))
                break;
            if (size == 0) break;

            var chunk = await ReadExactlyAsync(stream, Math.Min(size, MaxBodySize - (int)ms.Length), ct);
            await ms.WriteAsync(chunk, ct);
            await ReadLineAsync(stream, ct); // trailing CRLF
            if (ms.Length >= MaxBodySize) break;
        }
        // Read trailing headers
        while (true)
        {
            var line = await ReadLineAsync(stream, ct);
            if (string.IsNullOrEmpty(line)) break;
        }
        return ms.ToArray();
    }

    private async Task<(int StatusCode, Dictionary<string, string[]> Headers, byte[]? Body)> ReadHttpResponseAsync(
        Stream stream, CancellationToken ct)
    {
        var statusLine = await ReadLineAsync(stream, ct);
        var statusCode = 0;
        if (statusLine is not null)
        {
            var sp = statusLine.Split(' ');
            if (sp.Length >= 2) int.TryParse(sp[1], out statusCode);
        }

        var headers = await ReadHeadersAsync(stream, ct);
        var body = await ReadBodyAsync(stream, headers, ct);

        // Handle connection close if no content-length
        if (body is null && !headers.ContainsKey("content-length") &&
            statusCode is not (204 or 304) &&
            statusCode >= 200)
        {
            using var ms = new MemoryStream();
            var buf = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buf, ct)) > 0)
            {
                await ms.WriteAsync(buf.AsMemory(0, read), ct);
                if (ms.Length > MaxBodySize) break;
            }
            body = ms.ToArray();
        }

        return (statusCode, headers, body);
    }

    // ---- HTTP Message Builders ----

    private static byte[] BuildHttpRequest(string method, string path,
        Dictionary<string, string[]> headers, byte[]? body)
    {
        var sb = new StringBuilder();
        sb.Append($"{method} {path} HTTP/1.1\r\n");
        foreach (var (name, values) in headers)
        {
            if (name.Equals("proxy-connection", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var v in values)
                sb.Append($"{name}: {v}\r\n");
        }
        sb.Append("\r\n");
        var headBytes = HeaderEncoding.GetBytes(sb.ToString());

        if (body is not null)
            return [.. headBytes, .. body];
        return headBytes;
    }

    private static byte[] BuildHttpResponse(int statusCode, Dictionary<string, string[]> headers, byte[]? body)
    {
        var reason = statusCode switch
        {
            200 => "OK",
            204 => "No Content",
            301 => "Moved Permanently",
            302 => "Found",
            304 => "Not Modified",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => "Unknown"
        };

        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {reason}\r\n");
        foreach (var (name, values) in headers)
        {
            foreach (var v in values)
                sb.Append($"{name}: {v}\r\n");
        }
        sb.Append("\r\n");
        var headBytes = HeaderEncoding.GetBytes(sb.ToString());

        if (body is not null)
            return [.. headBytes, .. body];
        return headBytes;
    }

    // ---- Utilities ----

    private static (string hostname, int port) ParseHostPort(string hostPort, int defaultPort)
    {
        if (hostPort.Contains(':'))
        {
            var sp = hostPort.Split(':');
            return (sp[0], int.Parse(sp[1]));
        }
        return (hostPort, defaultPort);
    }

    private static readonly HashSet<string> TextContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain", "text/html", "text/xml", "text/css", "text/csv",
        "application/json", "application/xml", "application/xhtml+xml",
        "application/x-www-form-urlencoded",
        "application/javascript", "application/ecmascript",
    };

    private static string? TryDecodeBody(Dictionary<string, string[]> headers, byte[] body)
    {
        if (body.Length == 0) return null;

        var contentType = "";
        if (headers.TryGetValue("content-type", out var ct))
            contentType = string.Join(" ", ct).Split(';')[0].Trim().ToLowerInvariant();

        var isTextType = !string.IsNullOrEmpty(contentType) &&
            (TextContentTypes.Contains(contentType) || contentType.StartsWith("text/"));

        if (isTextType)
        {
            try { return BodyEncoding.GetString(body); }
            catch { return $"[Binary data: {body.Length} bytes]"; }
        }

        // Try UTF-8 decode and check if result looks like text
        string decoded;
        try { decoded = BodyEncoding.GetString(body); }
        catch { return $"[Binary data: {body.Length} bytes]"; }

        var nonPrintable = decoded.Count(c => c != '\n' && c != '\r' && c != '\t' && !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && !char.IsPunctuation(c));
        if ((double)nonPrintable / Math.Max(decoded.Length, 1) > 0.3)
            return $"[Binary data: {body.Length} bytes]";

        return decoded;
    }

    private static bool ShouldKeepAlive(Dictionary<string, string[]> headers)
    {
        if (headers.TryGetValue("connection", out var conn))
        {
            var val = string.Join(", ", conn);
            if (val.Contains("close", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
