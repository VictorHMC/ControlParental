using System.Security.Principal;
using System.Text.Json;

namespace ControlParental.Spike;

internal static class AgentPaths
{
    public const string ServiceName = "ControlParentalLabAgent";
    public const string PipeName = "ControlParentalLab.Agent.v1";
    public static string Data => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ControlParentalLabAgent");
    public static string State => Path.Combine(Data, "state.json");
    public static string Install => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ControlParentalLabAgent");
    public static string Executable => Path.Combine(Install, "ControlParental.Agent.exe");
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") return await AgentTests.Run();
            if (args.Length == 1 && args[0] == "--service")
            {
                if (WindowsIdentity.GetCurrent().User?.Value != "S-1-5-19") throw new UnauthorizedAccessException("El servicio debe ejecutarse como LocalService.");
                return new WindowsServiceHost(RunService).Run();
            }
            if (args.Length == 2 && args[0] == "initialize")
            {
                RequireAdministrator();
                if (!Directory.Exists(AgentPaths.Data) || File.Exists(AgentPaths.State))
                    throw new IOException("El instalador debe preparar el directorio; no se reemplaza una credencial existente.");
                var sid = new SecurityIdentifier(args[1]);
                if (!sid.IsAccountSid()) throw new ArgumentException("Se requiere el SID de una cuenta de usuario.");
                // El instalador puede enviar las dos lineas por stdin anonimo, nunca por argumentos.
                var first = Console.IsInputRedirected ? Console.ReadLine() ?? "" : ParentPassword.ReadHidden("Nueva contrasena del responsable (minimo 12 caracteres): ");
                var second = Console.IsInputRedirected ? Console.ReadLine() ?? "" : ParentPassword.ReadHidden("Repite la contrasena: ");
                if (first != second || first.Length > 1024) throw new ArgumentException("Las contrasenas no coinciden o exceden el limite.");
                new StateStore(AgentPaths.State).Save(new(1, ParentPassword.Create(first), sid.Value, false, 0, DateTimeOffset.MinValue));
                Console.WriteLine("Servicio configurado, modo hijo inicialmente desactivado.");
                return 0;
            }
            if (args.Length == 1 && args[0] == "authorize-local")
            {
                RequireAdministrator();
                await ParentPassword.Authorize(new StateStore(AgentPaths.State).Load().Credential);
                return 0;
            }
            var stdin = args.Length == 2 && args[1] == "--stdin-password";
            if ((args.Length != 1 && !stdin) || args[0] is not ("status" or "activate" or "deactivate" or "change-password"))
            {
                Console.WriteLine("Comandos: status | activate | deactivate | change-password");
                return 2;
            }
            var command = args[0];
            var password = command == "status" ? null : stdin ? Console.ReadLine() ?? "" : ParentPassword.ReadHidden("Contrasena del responsable: ");
            string? next = null;
            if (command == "change-password")
            {
                next = stdin ? Console.ReadLine() ?? "" : ParentPassword.ReadHidden("Nueva contrasena: ");
                var confirmation = stdin ? Console.ReadLine() ?? "" : ParentPassword.ReadHidden("Repite la nueva contrasena: ");
                if (next != confirmation) throw new ArgumentException("Las contrasenas no coinciden.");
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await PipeControl.Send(AgentPaths.PipeName, AgentPaths.Executable, new(command, password, next), timeout.Token);
            Console.WriteLine($"Modo hijo HTTPS: {(response.Active ? "ACTIVO" : "INACTIVO")}. {response.Message}");
            if (response.RetryAfterSeconds > 0) Console.WriteLine($"Reintentar en {response.RetryAfterSeconds} segundos.");
            return response.Ok ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or FormatException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Console.Error.WriteLine($"No se completo la operacion: {ex.Message}");
            return 1;
        }
    }

    private static void RequireAdministrator()
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Esta operacion requiere una consola elevada de administrador.");
    }

    private static async Task RunService(CancellationToken stop)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stop);
        AgentController? controller = null;
        var rules = new DomainRules(File.ReadAllLines(Path.Combine(AgentPaths.Install, "blocked-domains.txt")));
        var proxy = new TunnelProxy(rules, 18777, blockingEnabled: () => controller!.Current.Active);
        controller = new AgentController(new StateStore(AgentPaths.State), proxy.DisconnectClients);
        var filter = proxy.Run(lifetime.Token);
        var serviceSid = ((SecurityIdentifier)new NTAccount("NT SERVICE", AgentPaths.ServiceName).Translate(typeof(SecurityIdentifier))).Value;
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(new SecurityIdentifier(serviceSid)))
            throw new UnauthorizedAccessException("Falta el SID por servicio en el token.");
        var pipe = PipeControl.Serve(AgentPaths.PipeName, controller.Current.TargetSid, controller, lifetime.Token, serviceSid);
        try { await Task.WhenAny(filter, pipe); }
        finally
        {
            lifetime.Cancel();
            try { await Task.WhenAll(filter, pipe); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
}
