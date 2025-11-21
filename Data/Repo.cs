using System.Collections.Concurrent;
using System.Text.Json;
using ProjetoDePlanejamento.LicensingServer.Contracts;

namespace ProjetoDePlanejamento.LicensingServer.Data
{
   public interface ILicenseRepo
{
    Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint);
    Task<SignedLicense?> TryGetByKeyAsync(string licenseKey);
    Task<SignedLicense>  GetOrStartTrialAsync(string fingerprint, string? email, int trialDays);
    Task ProlongByEmailAsync(string email, TimeSpan delta);
    Task DeactivateAsync(string licenseKey);

    // >>> NOVO: usado em /api/check e /api/validate <<<
    Task<SignedLicense?> GetLicenseWithFingerprintCheckAsync(string licenseKey, string? fingerprint);

    Task LogDownloadAsync(string? ip, string? ua, string? referer);
    Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw);

    Task RecordActivationAsync(string licenseKey, string fingerprint, string status);
}



    public sealed class InMemoryRepo : ILicenseRepo
    {
        private readonly HashSet<string> _validKeys;
        private readonly ConcurrentDictionary<string, SignedLicense> _byKey = new();
        private readonly ConcurrentDictionary<string, string> _keyByEmail = new();
        private readonly ConcurrentDictionary<string, SignedLicense> _trialByFp = new();
        private readonly ConcurrentDictionary<string, string> _activations = new(); // licenseKey -> fingerprint

public Task RecordActivationAsync(string licenseKey, string fingerprint, string status)
{
    if (string.IsNullOrWhiteSpace(licenseKey) || string.IsNullOrWhiteSpace(fingerprint))
        return Task.CompletedTask;

    _activations[licenseKey] = fingerprint;

    if (_byKey.TryGetValue(licenseKey, out var lic))
    {
        lic.Payload.Fingerprint = fingerprint;
        if (!string.IsNullOrWhiteSpace(status))
            lic.Payload.SubscriptionStatus = status;
        _byKey[licenseKey] = lic;
    }
    return Task.CompletedTask;
}


        public static int TrialDays =>
            int.TryParse(Environment.GetEnvironmentVariable("TRIAL_DAYS"), out var d) && d > 0 ? d : 7;

        public InMemoryRepo(IEnumerable<string>? seedKeys)
        {
            _validKeys = new HashSet<string>((seedKeys ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        public Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
        {
            if (string.IsNullOrWhiteSpace(licenseKey) || !_validKeys.Contains(licenseKey))
                return Task.FromResult<SignedLicense?>(null);

            var emailNorm = NormalizeEmail(email);

            var lic = _byKey.AddOrUpdate(
                licenseKey,
                _ => Create(licenseKey, emailNorm, fingerprint, 30, "active"),
                (_, existing) =>
                {
                    existing.Payload.SubscriptionStatus = "active";
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

        public Task<SignedLicense?> TryGetByKeyAsync(string licenseKey)
            => Task.FromResult(_byKey.TryGetValue(licenseKey, out var lic) ? lic : null);

            public Task<SignedLicense?> GetLicenseWithFingerprintCheckAsync(string licenseKey, string? fingerprint)
{
    if (!_byKey.TryGetValue(licenseKey, out var lic))
        return Task.FromResult<SignedLicense?>(null);

    // Se já tem uma ativação registrada para essa licença
    if (_activations.TryGetValue(licenseKey, out var existingFp) &&
        !string.IsNullOrWhiteSpace(existingFp) &&
        !string.Equals(existingFp, fingerprint, StringComparison.OrdinalIgnoreCase))
    {
        // bloqueia outro computador
        return Task.FromResult<SignedLicense?>(null);
    }

    // Se veio um fingerprint novo (ou o primeiro), grava
    if (!string.IsNullOrWhiteSpace(fingerprint))
    {
        _activations[licenseKey] = fingerprint;
        lic.Payload.Fingerprint = fingerprint;
    }

    return Task.FromResult<SignedLicense?>(lic);
}


        public Task<SignedLicense> GetOrStartTrialAsync(string fingerprint, string? email, int trialDays)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
                throw new ArgumentException("fingerprint required", nameof(fingerprint));

            var lic = _trialByFp.GetOrAdd(
                fingerprint,
                _ => Create("TRIAL", NormalizeEmail(email), fingerprint, trialDays, "trial"));

            return Task.FromResult(lic);
        }

        public Task ProlongByEmailAsync(string email, TimeSpan delta)
        {
            var e = NormalizeEmail(email);
            if (e != null && _keyByEmail.TryGetValue(e, out var key) && _byKey.TryGetValue(key, out var lic))
                lic.Payload.ExpiresAtUtc = lic.Payload.ExpiresAtUtc.Add(delta);
            return Task.CompletedTask;
        }

        public Task DeactivateAsync(string licenseKey)
        {
            if (_byKey.TryGetValue(licenseKey, out var lic))
            {
                lic.Payload.SubscriptionStatus = "canceled";
                lic.Payload.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
            }
            return Task.CompletedTask;
        }

        // logs (no-op aqui)
        public Task LogDownloadAsync(string? ip, string? ua, string? referer) => Task.CompletedTask;
        public Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw) => Task.CompletedTask;

        // helpers
        private static string? NormalizeEmail(string? email)
            => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        private static SignedLicense Create(string id, string? email, string? fingerprint, int days, string status)
            => new SignedLicense
            {
                Payload = new LicensePayload
                {
                    LicenseId = id,
                    Email = email ?? "",
                    Fingerprint = fingerprint ?? "",
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(days),
                    SubscriptionStatus = status
                }
            };
    }
}


