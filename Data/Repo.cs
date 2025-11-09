using System.Collections.Concurrent;
using ProjetoDePlanejamento.LicensingServer.Contracts;


namespace ProjetoDePlanejamento.LicensingServer.Data

{
 public interface ILicenseRepo
    {
        Task<LicenseResponse?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint);
        Task<LicenseResponse?> TryGetByKeyAsync(string licenseKey);
        Task<LicenseResponse>  GetOrStartTrialAsync(string fingerprint, string? email, int trialDays);
        Task ProlongByEmailAsync(string email, TimeSpan delta);
        Task DeactivateAsync(string licenseKey);

        // logs (no-op no InMemory)
        Task LogDownloadAsync(string? ip, string? ua, string? referer);
        Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw);
    }

    public sealed class InMemoryRepo : ILicenseRepo
    {
        private readonly HashSet<string> _validKeys;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SignedLicense> _byKey = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _keyByEmail = new();
        private readonly ConcurrentDictionary<string, SignedLicense> _trialByFp = new();

          // quantos dias de trial (opcional via ENV TRIAL_DAYS; padrão = 7)
        public static int TrialDays =>
            int.TryParse(Environment.GetEnvironmentVariable("TRIAL_DAYS"), out var d) && d > 0 ? d : 7;

        public InMemoryRepo(IEnumerable<string>? seedKeys)
        {
            // seeds defensivos (ignora nulos/vazios/whitespace)
            _validKeys = new HashSet<string>(
                (seedKeys ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s))
            );
        }

        public Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
        {
            if (string.IsNullOrWhiteSpace(licenseKey) || !_validKeys.Contains(licenseKey))
                return Task.FromResult<SignedLicense?>(null);

            string? emailNorm = NormalizeEmail(email);

            var lic = _byKey.AddOrUpdate(
                licenseKey,
                // Criar novo
                _ => CreateNew(licenseKey, emailNorm, fingerprint),
                // Atualizar existente
                (_, existing) =>
{
    existing.Payload.SubscriptionStatus = "active";            // << ADICIONE ESTA LINHA
    existing.Payload.ExpiresAtUtc = DateTime.UtcNow.AddDays(30);
    if (!string.IsNullOrWhiteSpace(emailNorm))
    {
        existing.Payload.Email = emailNorm;
        _keyByEmail[emailNorm!] = licenseKey;
    }
    if (!string.IsNullOrWhiteSpace(fingerprint))
        existing.Payload.Fingerprint = fingerprint;
    return existing;
});


            return Task.FromResult<SignedLicense?>(lic);
        }

        public Task ProlongByKeyAsync(string licenseKey, TimeSpan delta)
        {
            _byKey.AddOrUpdate(
                licenseKey,
                _ => CreateNew(licenseKey, email: null, fingerprint: null, baseDays: 30).WithProlong(delta),
                (_, existing) =>
                {
                    existing.Payload.ExpiresAtUtc = existing.Payload.ExpiresAtUtc.Add(delta);
                    return existing;
                });
            return Task.CompletedTask;
        }
            // helper específico para trial (não altera o CreateNew de licença)


        public Task ProlongByEmailAsync(string email, TimeSpan delta)
        {
            if (string.IsNullOrWhiteSpace(email)) return Task.CompletedTask;

            var emailNorm = NormalizeEmail(email)!;
            if (_keyByEmail.TryGetValue(emailNorm, out var key))
                return ProlongByKeyAsync(key, delta);

            return Task.CompletedTask;
        }

        public Task<SignedLicense?> TryGetByKeyAsync(string licenseKey)
       {
             if (_byKey.TryGetValue(licenseKey, out var lic))
               return Task.FromResult<SignedLicense?>(lic);
            return Task.FromResult<SignedLicense?>(null);
        }
public Task DeactivateAsync(string licenseKey)
{
    if (_byKey.TryGetValue(licenseKey, out var existing))
    {
        existing.Payload.SubscriptionStatus = "canceled";
        existing.Payload.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
    }
    // se não existir, não cria nada
    return Task.CompletedTask;
}
public Task<SignedLicense?> TryGetTrialByFingerprintAsync(string fingerprint)
{
    if (string.IsNullOrWhiteSpace(fingerprint))
        return Task.FromResult<SignedLicense?>(null);

    return Task.FromResult(_trialByFp.TryGetValue(fingerprint, out var lic) ? lic : null);
}

public Task<SignedLicense> GetOrStartTrialAsync(string fingerprint, string? email, int days)
{
    if (string.IsNullOrWhiteSpace(fingerprint))
        throw new ArgumentException("fingerprint required", nameof(fingerprint));

    var lic = _trialByFp.AddOrUpdate(
        fingerprint,
        // cria trial uma única vez
        _ => CreateTrial(fingerprint, email, days),
        // se já existe, retorna como está (não reinicia o relógio do trial)
        (_, existing) => existing
    );

    return Task.FromResult(lic);
}
      


        // ===== Helpers =====
        private static string? NormalizeEmail(string? email)
            => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        private static SignedLicense CreateNew(string licenseKey, string? email, string? fingerprint, int baseDays = 30)
            => new SignedLicense
            {
                Payload = new LicensePayload
                {
                    Type = LicenseType.Subscription,
                    SubscriptionStatus = "active",
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(baseDays),
                    Email = email,
                    Fingerprint = fingerprint,
                    // opcional: atribuir um LicenseId se seu modelo tiver
                    // LicenseId = licenseKey
                }
            };
            private static SignedLicense CreateTrial(string fingerprint, string? email, int days)
    => new SignedLicense
    {
        Payload = new LicensePayload
        {
            Type = LicenseType.Trial,          // distingue de Subscription
            SubscriptionStatus = "trial",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(days),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            Fingerprint = fingerprint
        }
    };

    }



    // Pequeno helper de extensão só pra deixar claro o "prolongamento" ao criar novo
    internal static class LicenseHelpers
    {
        public static SignedLicense WithProlong(this SignedLicense lic, TimeSpan delta)
        {
            lic.Payload.ExpiresAtUtc = lic.Payload.ExpiresAtUtc.Add(delta);
            return lic;
        }
    }
}
