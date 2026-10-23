using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ControlParental.Spike;

// Adaptador minimo al SCM. No instala servicios ni modifica sus permisos.
internal sealed class WindowsServiceHost(Func<CancellationToken, Task> run)
{
    private readonly CancellationTokenSource stop = new();
    private readonly object statusLock = new();
    private ServiceMainCallback? mainCallback;
    private HandlerCallback? controlCallback;
    private IntPtr statusHandle;
    private int result;

    public int Run()
    {
        mainCallback = Main;
        var table = new[] { new ServiceEntry { Name = AgentPaths.ServiceName, Callback = mainCallback }, new ServiceEntry() };
        if (!StartServiceCtrlDispatcher(table)) throw new Win32Exception(Marshal.GetLastWin32Error());
        GC.KeepAlive(mainCallback);
        GC.KeepAlive(controlCallback);
        return result;
    }

    private void Main(uint count, IntPtr arguments)
    {
        controlCallback = Control;
        statusHandle = RegisterServiceCtrlHandlerEx(AgentPaths.ServiceName, controlCallback, IntPtr.Zero);
        if (statusHandle == IntPtr.Zero) { result = 1; return; }
        Report(2); // START_PENDING.
        try
        {
            var task = run(stop.Token);
            if (task.IsFaulted) task.GetAwaiter().GetResult();
            Report(4); // RUNNING.
            task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            result = 1;
            try { File.WriteAllText(Path.Combine(AgentPaths.Data, "last-error.txt"), DateTimeOffset.UtcNow.ToString("o") + " " + ex.GetType().Name); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        finally { Report(1, result == 0 ? 0u : 1066u); } // STOPPED; ERROR_SERVICE_SPECIFIC_ERROR.
    }

    private uint Control(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        if (control is 1 or 5) { Report(3); stop.Cancel(); } // STOP / SHUTDOWN.
        return 0;
    }

    private void Report(uint state, uint error = 0)
    {
        lock (statusLock)
        {
            var status = new ServiceStatus
            {
                ServiceType = 0x10, CurrentState = state,
                ControlsAccepted = state == 4 ? 5u : 0u, Win32ExitCode = error,
                ServiceSpecificExitCode = error == 0 ? 0u : 1u,
                CheckPoint = state is 2 or 3 ? 1u : 0u,
                WaitHint = state is 2 or 3 ? 15000u : 0u
            };
            _ = SetServiceStatus(statusHandle, ref status);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainCallback(uint argc, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint HandlerCallback(uint control, uint eventType, IntPtr eventData, IntPtr context);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? Name;
        public ServiceMainCallback? Callback;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }
    [DllImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceEntry[] table);
    [DllImport("advapi32.dll", EntryPoint = "RegisterServiceCtrlHandlerExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string name, HandlerCallback callback, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);
}
