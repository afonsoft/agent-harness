using Taskboard.Domain.Shared.Configuration;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;

namespace Taskboard.Server.Services;

/// <summary>
/// Representa o usuário administrador carregado do arquivo <c>admin.json</c>
/// ou, quando não existe, a partir das configurações/variáveis de ambiente.
/// </summary>
public sealed class AdminUser
{
    /// <summary>Nome de usuário do administrador.</summary>
    public string Username { get; }

    /// <summary>Hash da senha gerado pelo <see cref="PasswordHasher{TUser}"/>.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Caminho do arquivo <c>admin.json</c>.</summary>
    public string? AdminFilePath { get; }

    /// <summary>
    /// Cria uma nova instância de <see cref="AdminUser"/>.
    /// </summary>
    public AdminUser(string username, string passwordHash, string? adminFilePath = null)
    {
        Username = username;
        PasswordHash = passwordHash;
        AdminFilePath = adminFilePath;
    }

    /// <summary>
    /// Verifica se a senha informada corresponde ao hash armazenado.
    /// </summary>
    public bool Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var result = new PasswordHasher<AdminUser>().VerifyHashedPassword(this, PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    /// <summary>
    /// Altera a senha do administrador e persiste o novo hash em <c>admin.json</c>.
    /// </summary>
    public void ChangePassword(string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new ArgumentException("New password cannot be empty.", nameof(newPassword));
        }

        var hasher = new PasswordHasher<AdminUser>();
        PasswordHash = hasher.HashPassword(this, newPassword);

        if (!string.IsNullOrEmpty(AdminFilePath))
        {
            Save(AdminFilePath);
        }
    }

    /// <summary>
    /// Carrega as credenciais do administrador a partir do <c>admin.json</c>,
    /// ou semente do arquivo de configuração/variável de ambiente, persistindo o hash.
    /// </summary>
    public static AdminUser CreateFromConfiguration(IConfiguration configuration, string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var adminFilePath = Path.Combine(dataDirectory, "admin.json");

        if (File.Exists(adminFilePath))
        {
            try
            {
                var content = File.ReadAllText(adminFilePath);
                var data = JsonSerializer.Deserialize<AdminData>(content);
                if (!string.IsNullOrWhiteSpace(data?.Username) && !string.IsNullOrWhiteSpace(data?.PasswordHash))
                {
                    return new AdminUser(data.Username, data.PasswordHash, adminFilePath);
                }
            }
            catch (JsonException)
            {
                // corrupted file; fall back to seed and recreate
            }
        }

        var username = HarnessEnv.Get("HARNESS_ADMIN_USERNAME");
        if (string.IsNullOrWhiteSpace(username))
        {
            username = configuration["Admin:Username"];
        }
        if (string.IsNullOrWhiteSpace(username))
        {
            username = "admin";
        }

        var password = HarnessEnv.Get("HARNESS_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            password = configuration["Admin:Password"];
        }

        string? legacyPasswordFilePath = null;

        if (string.IsNullOrWhiteSpace(password))
        {
            password = GenerateRandomPassword();
            legacyPasswordFilePath = Path.Combine(dataDirectory, ".admin-password");
            File.WriteAllText(legacyPasswordFilePath, password);

            Console.WriteLine($"[Taskboard] Admin password generated and saved to: {legacyPasswordFilePath}");

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(legacyPasswordFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        var user = new AdminUser(username, string.Empty, adminFilePath);
        var hasher = new PasswordHasher<AdminUser>();
        user.PasswordHash = hasher.HashPassword(user, password);
        user.Save(adminFilePath);

        return user;
    }

    /// <summary>
    /// Persiste as credenciais no arquivo <c>admin.json</c> com permissões restritas.
    /// </summary>
    public void Save(string path)
    {
        var data = new AdminData(Username, PasswordHash);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(path, json);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string GenerateRandomPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record AdminData(string Username, string PasswordHash);
}
