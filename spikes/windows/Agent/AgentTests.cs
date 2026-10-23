using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ControlParental.Spike;

internal static class AgentTests
{
    public static async Task<int> Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "agent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checks = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + label);
            checks++;
            Console.WriteLine("PASS: " + label);
        }
        try
        {
            const string password = "Solo-prueba-aleatoria-1234";
            const string nextPassword = "Nueva-solo-prueba-567890";
            var sid = WindowsIdentity.GetCurrent().User!.Value;
            var path = Path.Combine(directory, "state.json");
            var store = new StateStore(path);
            store.Save(new(1, ParentPassword.Create(password), sid, false, 0, DateTimeOffset.MinValue));
            var now = DateTimeOffset.UtcNow;
            var resets = 0;
            var controller = new AgentController(store, () => resets++, () => now);
            Check(!controller.Current.Active, "instalacion inicial inactiva");
            Check(!controller.Execute(new("activate", "mal")).Ok && !controller.Current.Active, "contrasena incorrecta no activa");
            Check(controller.Execute(new("activate", password)).Ok && controller.Current.Active, "contrasena correcta activa");
            Check(resets == 1, "cambio de modo desconecta conexiones existentes");
            Check(!controller.Execute(new("deactivate", "mal")).Ok && controller.Current.Active, "contrasena incorrecta no desbloquea");
            Check(!controller.Execute(new("deactivate", "mal")).Ok, "segundo fallo registrado");
            var rejected = controller.Execute(new("deactivate", "mal"));
            Check(!rejected.Ok && rejected.RetryAfterSeconds == 30, "tercer fallo impone espera");
            controller = new AgentController(store, () => resets++, () => now);
            Check(controller.Current.Active, "reinicio del motor conserva modo activo");
            Check(!controller.Execute(new("deactivate", password)).Ok, "espera persiste al reiniciar el motor");
            now = now.AddSeconds(31);
            Check(controller.Execute(new("deactivate", password)).Ok && !controller.Current.Active, "desbloqueo autorizado despues de espera");
            Check(!controller.Execute(new("change-password", "mal", nextPassword)).Ok, "no cambia contrasena sin autorizar");
            Check(controller.Execute(new("change-password", password, nextPassword)).Ok, "cambio autorizado de contrasena");
            Check(!controller.Execute(new("activate", password)).Ok, "contrasena anterior ya no funciona");
            Check(controller.Execute(new("activate", nextPassword)).Ok, "nueva contrasena funciona");
            Check(!File.ReadAllText(path).Contains(nextPassword), "estado persistido no contiene contrasena visible");
            Check(!controller.Execute(new("execute-program", nextPassword)).Ok, "IPC no admite ejecutar programas arbitrarios");
            var acl = PipeControl.Security(sid);
            Check(acl.AreAccessRulesProtected, "IPC sin permisos heredados");
            var rules = acl.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
            Check(rules.Any(x => x.IdentityReference.Value == "S-1-5-2" && x.AccessControlType == AccessControlType.Deny), "IPC niega conexiones de red");
            Check(rules.Where(x => x.IdentityReference.Value == sid && x.AccessControlType == AccessControlType.Allow)
                .All(x => (x.PipeAccessRights & PipeAccessRights.CreateNewInstance) == 0), "usuario objetivo no puede crear instancias del pipe");
            var exclusive = PipeControl.Security(sid, "S-1-5-80-111-222-333-444-555")
                .GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
            Check(!exclusive.Any(x => x.IdentityReference.Value == "S-1-5-19"), "ACL de produccion usa SID exclusivo, no todos los LocalService");
            var oversized = false;
            try { _ = await PipeControl.Read<AgentRequest>(new MemoryStream([1, 64, 0, 0]), CancellationToken.None); }
            catch (InvalidDataException) { oversized = true; }
            Check(oversized, "IPC rechaza mensajes superiores al limite antes de reservar cuerpo");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var pipeName = "ControlParentalLab.test." + Guid.NewGuid().ToString("N");
            var serving = PipeControl.Serve(pipeName, sid, controller, timeout.Token);
            try
            {
                var response = await PipeControl.Send(pipeName, Environment.ProcessPath!, new("status"), timeout.Token, verifyServiceRegistration: false);
                Check(response.Ok && response.Active, "cliente separado consulta motor mediante IPC");
                response = await PipeControl.Send(pipeName, Environment.ProcessPath!, new("deactivate", "mal"), timeout.Token, verifyServiceRegistration: false);
                Check(!response.Ok && response.Active, "desbloqueo incorrecto rechazado por IPC");
                response = await PipeControl.Send(pipeName, Environment.ProcessPath!, new("status"), timeout.Token, verifyServiceRegistration: false);
                Check(response.Active, "cerrar y reconectar cliente no desbloquea");
                var spoofRejected = false;
                try { await PipeControl.Send(pipeName, Path.Combine(directory, "otro.exe"), new("deactivate", nextPassword), timeout.Token, verifyServiceRegistration: false); }
                catch (IOException) { spoofRejected = true; }
                Check(spoofRejected && controller.Current.Active, "cliente rechaza servidor con ejecutable inesperado");
                var unregistered = false;
                try { await PipeControl.Send(pipeName, Environment.ProcessPath!, new("deactivate", nextPassword), timeout.Token); }
                catch (IOException) { unregistered = true; }
                catch (System.ComponentModel.Win32Exception) { unregistered = true; }
                Check(unregistered && controller.Current.Active, "mismo ejecutable sin PID registrado en SCM tampoco recibe credenciales");
                response = await PipeControl.Send(pipeName, Environment.ProcessPath!, new("deactivate", nextPassword), timeout.Token, verifyServiceRegistration: false);
                Check(response.Ok && !response.Active, "desbloqueo correcto por IPC");
            }
            finally
            {
                timeout.Cancel();
                try { await serving; } catch (OperationCanceledException) { }
            }

            await TestLiveProxy(Check);
            controller.Execute(new("activate", nextPassword));
            File.Move(path, path + ".backup");
            Directory.CreateDirectory(path); // Obstaculo controlado a la escritura atomica.
            var persistFailed = false;
            try { controller.Execute(new("deactivate", nextPassword)); }
            catch (IOException) { persistFailed = true; }
            catch (UnauthorizedAccessException) { persistFailed = true; }
            Check(persistFailed && controller.Current.Active, "fallo de persistencia no confirma ni ejecuta desbloqueo");
            Directory.Delete(path);
            File.Move(path + ".backup", path);
            File.WriteAllText(path, "estado-corrupto");
            var failedClosed = false;
            try { _ = new AgentController(store, () => { }); }
            catch (JsonException) { failedClosed = true; }
            Check(failedClosed, "estado corrupto exige recuperacion; no crea un modo desbloqueado");
            Console.WriteLine($"{checks} comprobaciones del agente aprobadas; SCM e instalacion real pendientes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1; }
        finally
        {
            // Solo el subdirectorio aleatorio creado por esta prueba, dentro del binario local.
            var full = Path.GetFullPath(directory);
            if (full.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(full).StartsWith("agent-test-", StringComparison.Ordinal))
                Directory.Delete(full, recursive: true);
        }
    }

    private static async Task TestLiveProxy(Action<bool, string> check)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var remote = new TcpListener(IPAddress.Loopback, 0);
        remote.Start();
        var active = false;
        async Task<TcpClient> Connector(string host, int port, CancellationToken ct)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)remote.LocalEndpoint).Port, ct);
            return client;
        }
        var proxy = new TunnelProxy(new DomainRules(["example.com"]), 0, Connector, () => active);
        var running = proxy.Run(stop.Token);
        try
        {
            using var before = new TcpClient();
            await before.ConnectAsync(IPAddress.Loopback, proxy.Port, stop.Token);
            await before.GetStream().WriteAsync("CONNECT example.com:443 HTTP/1.1\r\n\r\n"u8.ToArray(), stop.Token);
            using var accepted = await remote.AcceptTcpClientAsync(stop.Token);
            var buffer = new byte[1024];
            var n = await before.GetStream().ReadAsync(buffer, stop.Token);
            check(Encoding.ASCII.GetString(buffer, 0, n).StartsWith("HTTP/1.1 200"), "modo inactivo permite dominio de prueba");
            active = true;
            proxy.DisconnectClients();
            var closed = false;
            try { closed = await before.GetStream().ReadAsync(buffer, stop.Token) == 0; }
            catch (IOException) { closed = true; }
            check(closed, "activar modo cierra tunel abierto antes del bloqueo");
            using var after = new TcpClient();
            await after.ConnectAsync(IPAddress.Loopback, proxy.Port, stop.Token);
            await after.GetStream().WriteAsync("CONNECT example.com:443 HTTP/1.1\r\n\r\n"u8.ToArray(), stop.Token);
            n = await after.GetStream().ReadAsync(buffer, stop.Token);
            check(Encoding.ASCII.GetString(buffer, 0, n).StartsWith("HTTP/1.1 403"), "modo activo rechaza nueva conexion al dominio bloqueado");
        }
        finally { stop.Cancel(); remote.Stop(); await running; }
    }
}
