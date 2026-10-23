using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Globalization;

namespace ControlParental.Spike;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
            return await SelfTests.Run();
        if (args.Length == 2 && args[0] == "--authorize")
        {
            try { await ParentPassword.Authorize(ParentPassword.Load(args[1])); return 0; }
            catch (Exception ex) when (ex is IOException or ArgumentException or FormatException)
            { Console.Error.WriteLine(ex.Message); return 1; }
        }
        if (args.Length == 2 && args[0] == "--setup-password")
        {
            try { await ParentPassword.Setup(args[1]); return 0; }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Text.Json.JsonException or FormatException) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        if (args.Length != 4 || args[0] != "--rules" || args[2] != "--password-file")
        {
            Console.Error.WriteLine("Uso: WebFilter --rules <dominios.txt> --password-file <credencial.json> | --setup-password <credencial.json> | --self-test");
            return 2;
        }
        try
        {
            var rules = new DomainRules(File.ReadAllLines(args[1]));
            var password = ParentPassword.Load(args[3]);
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; Console.WriteLine("Usa DESBLOQUEAR y la contrasena del responsable."); };
            var proxy = new TunnelProxy(rules, 18777);
            Console.WriteLine("Filtro de laboratorio: 127.0.0.1:18777. Escribe DESBLOQUEAR para detener.");
            Console.WriteLine("Solo HTTPS CONNECT: sin inspeccion TLS, sin cambios de proxy del sistema.");
            var running = proxy.Run(stop.Token);
            try
            {
                if (running.IsCompleted) await running;
                while (true)
                {
                    var command = Console.ReadLine();
                    if (command is null) throw new IOException("Se requiere una consola interactiva.");
                    if (command != "DESBLOQUEAR") { Console.WriteLine("Comando disponible: DESBLOQUEAR"); continue; }
                    await ParentPassword.Authorize(password);
                    break;
                }
            }
            finally { stop.Cancel(); await running; }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ArgumentException or System.Text.Json.JsonException or FormatException)
        {
            Console.Error.WriteLine($"No se pudo iniciar el filtro: {ex.Message}");
            return 1;
        }
    }
}

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

internal sealed class TunnelProxy(DomainRules rules, int port, Func<string, int, CancellationToken, Task<TcpClient>>? connector = null)
{
    private readonly TcpListener listener = new(IPAddress.Loopback, port);
    private readonly SemaphoreSlim slots = new(32);
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

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
                pending.Add(HandleSlot(client, stop));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            listener.Stop();
            await Task.WhenAll(pending);
        }
    }

    private async Task HandleSlot(TcpClient client, CancellationToken stop)
    {
        try { await Handle(client, stop); }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ArgumentException)
        {
            // No registrar destinos, URL ni contenido de navegacion.
        }
        finally { client.Dispose(); slots.Release(); }
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
        if (rules.IsBlocked(host)) { await Reply(input, "403 Forbidden", stop); return; }
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

internal static class SelfTests
{
    public static async Task<int> Run()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }
        try
        {
            var rules = new DomainRules(["example.com", "bücher.example"]);
            var credential = ParentPassword.Create("Prueba-unicamente-12345");
            Check(ParentPassword.Verify(credential, "Prueba-unicamente-12345"), "contrasena correcta aceptada");
            Check(!ParentPassword.Verify(credential, "incorrecta"), "contrasena incorrecta rechazada");
            Check(credential.Salt != ParentPassword.Create("Prueba-unicamente-12345").Salt, "salt aleatorio por credencial");
            Check(!System.Text.Json.JsonSerializer.Serialize(credential).Contains("Prueba-unicamente-12345"), "sin contrasena en texto plano");
            Check(rules.IsBlocked("EXAMPLE.com."), "normalizacion de mayusculas y punto final");
            Check(rules.IsBlocked("a.example.com"), "subdominios bloqueados");
            Check(!rules.IsBlocked("notexample.com"), "sin coincidencia por sufijo parcial");
            Check(!rules.IsBlocked("example.com.evil.test"), "sin coincidencia por prefijo");
            Check(rules.IsBlocked("xn--bcher-kva.example"), "IDN normalizado");
            Check(!TunnelProxy.IsPublic(IPAddress.Parse("127.0.0.1")) && !TunnelProxy.IsPublic(IPAddress.Parse("::1"))
                && !TunnelProxy.IsPublic(IPAddress.Parse("192.168.1.2")), "destinos privados rechazados");

            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var echo = new TcpListener(IPAddress.Loopback, 0);
            echo.Start();
            try
            {
                var connected = 0;
                async Task<TcpClient> TestConnector(string host, int port, CancellationToken ct)
                {
                    Interlocked.Increment(ref connected);
                    var c = new TcpClient();
                    await c.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)echo.LocalEndpoint).Port, ct);
                    return c;
                }
                var proxy = new TunnelProxy(rules, 0, TestConnector);
                var running = proxy.Run(stop.Token);
                try
                {
                    async Task<string> Request(string request)
                    {
                        using var c = new TcpClient();
                        await c.ConnectAsync(IPAddress.Loopback, proxy.Port, stop.Token);
                        var s = c.GetStream();
                        await s.WriteAsync(Encoding.ASCII.GetBytes(request), stop.Token);
                        var b = new byte[1024];
                        var n = await s.ReadAsync(b, stop.Token);
                        return Encoding.ASCII.GetString(b, 0, n);
                    }
                    Check((await Request("CONNECT example.com:443 HTTP/1.1\r\n\r\n")).StartsWith("HTTP/1.1 403"), "CONNECT bloqueado");
                    Check(connected == 0, "dominio bloqueado no abre conexion remota");
                    Check((await Request("GET http://example.org/ HTTP/1.1\r\n\r\n")).StartsWith("HTTP/1.1 501"), "HTTP plano fuera de alcance");
                    Check((await Request("CONNECT example.org:22 HTTP/1.1\r\n\r\n")).StartsWith("HTTP/1.1 400"), "puerto arbitrario rechazado");
                    Check((await Request("CONNECT 127.0.0.1:443 HTTP/1.1\r\n\r\n")).StartsWith("HTTP/1.1 403"), "IP directa rechazada");
                    using var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, proxy.Port, stop.Token);
                    var input = client.GetStream();
                    await input.WriteAsync("CONNECT example.org:443 HTTP/1.1\r\n\r\n"u8.ToArray(), stop.Token);
                    using var upstream = await echo.AcceptTcpClientAsync(stop.Token);
                    var buffer = new byte[1024];
                    var length = await input.ReadAsync(buffer, stop.Token);
                    Check(Encoding.ASCII.GetString(buffer, 0, length).StartsWith("HTTP/1.1 200"), "CONNECT permitido");
                    await input.WriteAsync("ping"u8.ToArray(), stop.Token);
                    var data = new byte[4];
                    await upstream.GetStream().ReadExactlyAsync(data, stop.Token);
                    Check(Encoding.ASCII.GetString(data) == "ping", "tunel transmite hacia destino");
                    await upstream.GetStream().WriteAsync("pong"u8.ToArray(), stop.Token);
                    await input.ReadExactlyAsync(data, stop.Token);
                    Check(Encoding.ASCII.GetString(data) == "pong", "tunel transmite hacia navegador");
                }
                finally { stop.Cancel(); await running; }
            }
            finally { echo.Stop(); }
            Console.WriteLine($"{checks} comprobaciones aprobadas. No prueban bloqueo global de Windows.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
