using System.Text.Json;

namespace ControlParental.Spike;

internal sealed record AgentState(int Version, PasswordRecord Credential, string TargetSid, bool Active,
    int FailedAttempts, DateTimeOffset LockedUntil);

internal sealed record AgentRequest(string Command, string? Password = null, string? NewPassword = null);
internal sealed record AgentResponse(bool Ok, bool Active, string Message, int RetryAfterSeconds = 0);

internal sealed class StateStore(string path)
{
    public AgentState Load()
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Estado demasiado grande.");
        var state = JsonSerializer.Deserialize<AgentState>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Estado inexistente.");
        if (state.Version != 1 || state.Credential is null || string.IsNullOrWhiteSpace(state.TargetSid) || state.FailedAttempts is < 0 or > 2)
            throw new InvalidDataException("Estado no valido. No se desactiva la proteccion automaticamente.");
        _ = ParentPassword.Verify(state.Credential, "validacion-estructural");
        _ = new System.Security.Principal.SecurityIdentifier(state.TargetSid);
        return state;
    }
    public void Save(AgentState state)
    {
        // El directorio de produccion se crea con ACL antes de guardar secretos.
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

internal sealed class AgentController
{
    private readonly StateStore store;
    private AgentState current;
    private readonly Func<DateTimeOffset> now;
    private readonly Action disconnectClients;
    public AgentState Current => Volatile.Read(ref current);

    public AgentController(StateStore store, Action disconnectClients, Func<DateTimeOffset>? clock = null)
    {
        this.store = store;
        current = store.Load();
        now = clock ?? (() => DateTimeOffset.UtcNow);
        this.disconnectClients = disconnectClients;
    }

    // Un solo consumidor IPC serializa comandos y persistencia.
    public AgentResponse Execute(AgentRequest request)
    {
        var state = Current;
        if (request.Command == "status") return new(true, state.Active, "Filtro HTTPS de laboratorio; AppLocker se gestiona por separado.");
        if (request.Command is not ("activate" or "deactivate" or "change-password"))
            return new(false, state.Active, "Comando no admitido.");
        var remaining = (int)Math.Ceiling((state.LockedUntil - now()).TotalSeconds);
        if (remaining > 0) return new(false, state.Active, "Demasiados intentos. Espera antes de reintentar.", remaining);
        if (request.Password is null || request.Password.Length > 1024 || !ParentPassword.Verify(state.Credential, request.Password))
        {
            var failed = state.FailedAttempts + 1;
            var locked = failed >= 3;
            state = state with { FailedAttempts = locked ? 0 : failed, LockedUntil = locked ? now().AddSeconds(30) : DateTimeOffset.MinValue };
            Persist(state);
            return new(false, state.Active, "Contrasena incorrecta.", locked ? 30 : 0);
        }
        PasswordRecord? replacement = null;
        if (request.Command == "change-password")
        {
            if (request.NewPassword is null || request.NewPassword.Length is < 12 or > 1024)
                return new(false, state.Active, "La nueva contrasena debe tener entre 12 y 1024 caracteres.");
            replacement = ParentPassword.Create(request.NewPassword);
        }
        var active = request.Command == "activate" || (request.Command == "change-password" && state.Active);
        var changed = active != state.Active;
        state = state with { Active = active, Credential = replacement ?? state.Credential, FailedAttempts = 0, LockedUntil = DateTimeOffset.MinValue };
        Persist(state); // Nunca confirmar ni cambiar el filtro si no se pudo escribir el estado.
        if (changed) disconnectClients(); // Cortar tuneles previos, incluso los abiertos antes de activar.
        return new(true, state.Active, request.Command == "change-password" ? "Contrasena actualizada." : "Estado del modo hijo actualizado para el filtro HTTPS.");
    }

    private void Persist(AgentState state)
    {
        store.Save(state);
        Volatile.Write(ref current, state);
    }
}
