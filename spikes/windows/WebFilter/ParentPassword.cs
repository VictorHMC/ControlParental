using System.Security.Cryptography;
using System.Text.Json;

namespace ControlParental.Spike;

internal sealed record PasswordRecord(int Version, int Iterations, string Salt, string Hash);

internal static class ParentPassword
{
    private const int Iterations = 600_000;
    public static PasswordRecord Create(string password)
    {
        if (password.Length < 12) throw new ArgumentException("Usa al menos 12 caracteres.");
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return new(1, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }
    public static bool Verify(PasswordRecord record, string candidate)
    {
        if (record.Version != 1 || record.Iterations != Iterations) throw new InvalidDataException("Formato de credencial no valido.");
        var salt = Convert.FromBase64String(record.Salt);
        var expected = Convert.FromBase64String(record.Hash);
        if (salt.Length != 32 || expected.Length != 32) throw new InvalidDataException("Credencial no valida.");
        var actual = Rfc2898DeriveBytes.Pbkdf2(candidate, salt, record.Iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
    public static PasswordRecord Load(string path) => JsonSerializer.Deserialize<PasswordRecord>(File.ReadAllText(path))
        ?? throw new InvalidDataException("Configura primero la contrasena del laboratorio.");

    public static string ReadHidden(string label)
    {
        if (Console.IsInputRedirected) throw new IOException("La contrasena requiere una consola interactiva.");
        Console.Write(label);
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return new string(chars.ToArray()); }
            if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); }
            else if (!char.IsControl(key.KeyChar) && chars.Count < 1024) chars.Add(key.KeyChar);
        }
    }

    public static async Task Authorize(PasswordRecord record)
    {
        var attempts = 0;
        while (!Verify(record, ReadHidden("Contrasena del responsable: ")))
        {
            attempts++;
            Console.WriteLine("Contrasena incorrecta.");
            if (attempts % 3 == 0)
            {
                Console.WriteLine("Espera de 30 segundos tras tres intentos fallidos.");
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }
    }

    public static async Task Setup(string path)
    {
        if (File.Exists(path)) await Authorize(Load(path));
        var first = ReadHidden("Nueva contrasena del responsable (minimo 12 caracteres): ");
        var second = ReadHidden("Repite la contrasena: ");
        if (first != second) throw new ArgumentException("Las contrasenas no coinciden.");
        var record = Create(first);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temp = fullPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record));
        File.Move(temp, fullPath, overwrite: true);
        Console.WriteLine("Credencial de laboratorio guardada. No la subas al repositorio.");
    }
}
