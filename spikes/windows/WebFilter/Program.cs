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
