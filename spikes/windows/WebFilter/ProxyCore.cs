using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Globalization;

namespace ControlParental.Spike;

internal sealed class DomainRules
{
    private readonly HashSet<string> blocked;
    public DomainRules(IEnumerable<string> lines)
    {
        blocked = lines.Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#'))
            .Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (blocked.Count == 0) throw new ArgumentException("Lista de dominios vacia.");
    }
    public static string Normalize(string host)
    {
        host = host.TrimEnd('.');
        if (host.Length == 0 || host.Any(char.IsWhiteSpace) || host.Contains('/') || host.Contains(':'))
            throw new ArgumentException("Dominio no valido.");
        host = new IdnMapping().GetAscii(host).ToLowerInvariant();
        if (Uri.CheckHostName(host) != UriHostNameType.Dns)
            throw new ArgumentException("Se requiere un nombre DNS.");
        return host;
    }
    public bool IsBlocked(string host)
    {
        host = Normalize(host);
        return blocked.Any(domain => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal));
    }
}

internal sealed class TunnelProxy(DomainRules rules, int port, Func<string, int, CancellationToken, Task<TcpClient>>? connector = null, Func<bool>? blockingEnabled = null)
{
    private readonly TcpListener listener = new(IPAddress.Loopback, port);
    private readonly SemaphoreSlim slots = new(32);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TcpClient> clients = new();
    private int nextClient;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public void DisconnectClients()
    {
        foreach (var client in clients.Values) client.Dispose();
    }

    public async Task Run(CancellationToken stop)
    {
        listener.Start();
        var pending = new HashSet<Task>();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await slots.WaitAsync(stop);
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stop); }
                catch { slots.Release(); throw; }
                pending.RemoveWhere(t => t.IsCompleted);
                var id = Interlocked.Increment(ref nextClient);
                clients[id] = client;
                pending.Add(HandleSlot(id, client, stop));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            listener.Stop();
            await Task.WhenAll(pending);
        }
    }

    private async Task HandleSlot(int id, TcpClient client, CancellationToken stop)
    {
        try { await Handle(client, stop); }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ArgumentException or ObjectDisposedException)
        {
            // No registrar destinos, URL ni contenido de navegacion.
        }
        finally { clients.TryRemove(id, out _); client.Dispose(); slots.Release(); }
    }

    private static async Task Reply(NetworkStream stream, string status, CancellationToken stop)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), stop);
    }

    private async Task Handle(TcpClient client, CancellationToken stop)
    {
        var input = client.GetStream();
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        headerTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        // Lectura hasta CRLFCRLF sin consumir bytes TLS que puedan seguir a la cabecera.
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 16384)
        {
            if (await input.ReadAsync(one, headerTimeout.Token) == 0) return;
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10) break;
        }
        if (bytes.Count >= 16384) { await Reply(input, "431 Request Header Fields Too Large", stop); return; }
        var header = Encoding.ASCII.GetString(bytes.ToArray());
        var first = header.Split("\r\n")[0].Split(' ');
        if (first.Length != 3 || first[2] != "HTTP/1.1") { await Reply(input, "400 Bad Request", stop); return; }
        if (first[0] != "CONNECT") { await Reply(input, "501 Not Implemented", stop); return; }
        var authority = first[1].Split(':');
        // Solo 443: no permitir un proxy generico a puertos internos.
        if (authority.Length != 2 || authority[1] != "443") { await Reply(input, "400 Bad Request", stop); return; }
        if (IPAddress.TryParse(authority[0], out _)) { await Reply(input, "403 Forbidden", stop); return; }
        string host;
        try { host = DomainRules.Normalize(authority[0]); }
        catch (ArgumentException) { await Reply(input, "400 Bad Request", stop); return; }
        if (IPAddress.TryParse(host, out _)) { await Reply(input, "403 Forbidden", stop); return; }
        if ((blockingEnabled?.Invoke() ?? true) && rules.IsBlocked(host)) { await Reply(input, "403 Forbidden", stop); return; }
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        TcpClient upstream;
        try { upstream = await (connector ?? ConnectPublic)(host, 443, connectTimeout.Token); }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        { await Reply(input, "502 Bad Gateway", stop); return; }
        using (upstream)
        using (var tunnelStop = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            // Limite de laboratorio para que una conexion no ocupe el proxy indefinidamente.
            tunnelStop.CancelAfter(TimeSpan.FromMinutes(5));
            await input.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), stop);
            var remote = upstream.GetStream();
            var outbound = input.CopyToAsync(remote, tunnelStop.Token);
            var inbound = remote.CopyToAsync(input, tunnelStop.Token);
            await Task.WhenAny(outbound, inbound);
            tunnelStop.Cancel();
            try { await Task.WhenAll(outbound, inbound); }
            catch (OperationCanceledException) when (tunnelStop.IsCancellationRequested) { }
        }
    }

    private static async Task<TcpClient> ConnectPublic(string host, int port, CancellationToken stop)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, stop);
        // Evitar loopback, redes privadas, link-local, multicast y direcciones especiales.
        var publicAddresses = addresses.Where(IsPublic).ToArray();
        if (publicAddresses.Length == 0) throw new IOException("Destino no publico.");
        var client = new TcpClient();
        try { await client.ConnectAsync(publicAddresses, port, stop); return client; }
        catch { client.Dispose(); throw; }
    }

    internal static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length == 16) return (b[0] & 0xe0) == 0x20; // IPv6 global unicast 2000::/3.
        return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
            && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }
}
