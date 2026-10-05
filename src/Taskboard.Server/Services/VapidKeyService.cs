using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Configuration;
using Taskboard.Integrations.Configuration;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-009: resolves the VAPID identity
/// (public/private/subject) used to sign Web Push requests. When the catalog
/// keys are empty the pair is generated once — ECDSA P-256, public as the
/// uncompressed point (0x04‖X‖Y, base64url) — and persisted through
/// <see cref="RuntimeConfigurationService.SetOverrideAsync"/> so it survives
/// restarts (rotating the private key would orphan every subscription).
/// Lives in Server (not Application) because it reloads the
/// <see cref="SqliteConfigurationProvider"/> after writing — overrides are only
/// visible through the configuration provider, not the repository.
/// </summary>
public sealed class VapidKeyService(
    RuntimeConfigurationService configuration,
    SqliteConfigurationProvider overridesProvider,
    ILogger<VapidKeyService> logger)
{
    internal const string PublicKeyKey = "Taskboard:Push:Vapid:PublicKey";
    internal const string PrivateKeyKey = "Taskboard:Push:Vapid:PrivateKey";
    internal const string SubjectKey = "Taskboard:Push:Vapid:Subject";
    internal const string DefaultSubject = "mailto:admin@localhost";

    /// <summary>The resolved identity — keys are never null after this call.</summary>
    public sealed record Keys(string PublicKey, string PrivateKey, string Subject);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Keys> GetOrCreateAsync(CancellationToken ct = default)
    {
        var subject = configuration.GetEffectiveValue(SubjectKey) ?? DefaultSubject;
        var publicKey = configuration.GetEffectiveValue(PublicKeyKey);
        var privateKey = configuration.GetEffectiveValue(PrivateKeyKey);

        if (!string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(privateKey))
        {
            return new Keys(publicKey, privateKey, subject);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-read inside the gate — another scope may have won the race.
            publicKey = configuration.GetEffectiveValue(PublicKeyKey);
            privateKey = configuration.GetEffectiveValue(PrivateKeyKey);
            if (!string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(privateKey))
            {
                return new Keys(publicKey, privateKey, subject);
            }

            var (generatedPublic, generatedPrivate) = Generate();
            var write = await configuration.SetOverrideAsync(PublicKeyKey, generatedPublic, ct).ConfigureAwait(false);
            if (write.Error != ConfigurationWriteError.None)
            {
                throw new InvalidOperationException($"Could not persist the VAPID public key: {write.Message}");
            }

            write = await configuration.SetOverrideAsync(PrivateKeyKey, generatedPrivate, ct).ConfigureAwait(false);
            if (write.Error != ConfigurationWriteError.None)
            {
                throw new InvalidOperationException($"Could not persist the VAPID private key: {write.Message}");
            }

            // SetOverrideAsync only writes the row — the provider must reload
            // before GetEffectiveValue sees it (same pattern as the endpoints).
            overridesProvider.Reload();

            logger.LogInformation("VAPID keypair generated and persisted — Web Push identity is stable now");
            return new Keys(generatedPublic, generatedPrivate, subject);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static (string Public, string Private) Generate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: true);
        var uncompressed = new byte[1 + p.Q!.X!.Length + p.Q.Y!.Length];
        uncompressed[0] = 0x04;
        p.Q.X.CopyTo(uncompressed, 1);
        p.Q.Y.CopyTo(uncompressed, 1 + p.Q.X.Length);
        return (Base64Url.EncodeToString(uncompressed), Base64Url.EncodeToString(p.D!));
    }
}
