using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ControlParental.Spike;

internal static class PipeControl
{
    private const int MaximumFrame = 16384;
    internal static PipeSecurity Security(string targetSid, string serverSid = "S-1-5-19")
    {
        var acl = new PipeSecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        foreach (var sid in new[] { "S-1-5-18", serverSid, "S-1-5-32-544" })
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid), PipeAccessRights.FullControl, AccessControlType.Allow));
        // ReadWrite no concede CreateNewInstance.
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(targetSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return acl;
    }

    public static async Task Serve(string name, string targetSid, AgentController controller, CancellationToken stop, string serverSid = "S-1-5-19")
    {
        using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            4096, 4096, Security(targetSid, serverSid));
        // Conservar la misma instancia entre clientes para evitar una ventana de suplantacion del nombre.
        while (!stop.IsCancellationRequested)
        {
            await server.WaitForConnectionAsync(stop);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var request = await Read<AgentRequest>(server, timeout.Token);
                var response = controller.Execute(request);
                await Write(server, response, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or ArgumentException)
            {
                // No registrar las solicitudes: contienen credenciales.
            }
            finally { server.Disconnect(); }
        }
    }

    public static async Task<AgentResponse> Send(string name, string expectedExecutable, AgentRequest request, CancellationToken stop, bool verifyServiceRegistration = true)
    {
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await client.ConnectAsync(3000, stop);
        VerifyServer(client.SafePipeHandle, expectedExecutable, verifyServiceRegistration);
        await Write(client, request, stop);
        return await Read<AgentResponse>(client, stop);
    }

    internal static async Task<T> Read<T>(Stream stream, CancellationToken stop)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, stop);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is < 1 or > MaximumFrame) throw new InvalidDataException("Mensaje fuera de limite.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, stop);
        return JsonSerializer.Deserialize<T>(body) ?? throw new InvalidDataException("Mensaje no valido.");
    }

    private static async Task Write<T>(Stream stream, T value, CancellationToken stop)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value);
        if (body.Length > MaximumFrame) throw new InvalidDataException("Mensaje fuera de limite.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, body.Length);
        await stream.WriteAsync(prefix, stop);
        await stream.WriteAsync(body, stop);
        await stream.FlushAsync(stop);
    }

    private static void VerifyServer(SafePipeHandle pipe, string expectedExecutable, bool verifyServiceRegistration)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var pid)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION.
        if (handle.IsInvalid) throw new IOException("No se pudo verificar la identidad del servicio.");
        var path = new StringBuilder(32768);
        uint length = 32768;
        if (!QueryFullProcessImageName(handle, 0, path, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!string.Equals(Path.GetFullPath(path.ToString()), Path.GetFullPath(expectedExecutable), StringComparison.OrdinalIgnoreCase))
            throw new IOException("El servidor IPC no corresponde al ejecutable instalado. No se enviara la contrasena.");
        if (verifyServiceRegistration)
        {
            using var scm = OpenSCManager(null, null, 1); // SC_MANAGER_CONNECT.
            if (scm.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            using var service = OpenService(scm, AgentPaths.ServiceName, 4); // SERVICE_QUERY_STATUS.
            if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!QueryServiceStatusEx(service, 0, out var status, (uint)Marshal.SizeOf<ServiceProcessStatus>(), out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (status.CurrentState != 4 || status.ProcessId != pid)
                throw new IOException("La conexion no pertenece al servicio de ControlParental activo. No se enviara la contrasena.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceProcessStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle scm, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int level, out ServiceProcessStatus status, uint size, out uint needed);
    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint flags, StringBuilder path, ref uint size);
}
