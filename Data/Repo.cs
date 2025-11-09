using System.Collections.Concurrent;
using System.Text.Json;
using ProjetoDePlanejamento.LicensingServer.Contracts;

namespace ProjetoDePlanejamento.LicensingServer.Data
{
    // Contrato único que Program.cs injeta (PgRepo e InMemoryRepo implementam)
    public interface ILicenseRepo
    {
        Task<LicenseResponse?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint);
        Task<LicenseResponse?> TryGetByKeyAsync(string licenseKey);
        Task<LicenseResponse>  GetOrStartTrialAsync(string fingerprint, string? email, int trialDays);
        Task ProlongByEmailAsync(string email, TimeSpan delta);
        Task DeactivateAsync(string licenseKey);

        // logs (no-op no InMemory; PgRepo grava no Postgres)
        Task LogDownloadAsync(string? ip, string? ua, string? referer);
        Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw);
    }

    // Implementação em memória (fallback local / DEV)
    public sealed class InMemoryRepo : ILicenseRepo
    {
        private readonly HashSet<string> _validKeys;
        private readonly ConcurrentDictionary<string, LicenseResponse> _byKey = new();
        private readonly ConcurrentDictionary<string, string> _keyByEmail = new();
        private readonly ConcurrentDictionary<string, LicenseResponse> _trialByFp = new();

        // dias de trial (pode sobrescrever via ENV TRIAL_DAYS)
        public static int TrialDays =>
            int.TryParse(Environment.GetEnvironmentVariable("TRIAL_DAYS"), out var d) && d > 0 ? d : 7;

        public InMemoryRepo(IEnumerable<string>? seedKeys)
        {
            _validKeys = new HashSet<string>((seedKeys ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        public Task<LicenseResponse?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
        {
            if (string.IsNullOrWhiteSpace(licenseKey) || !_validKeys.Contains(licenseKey))
                return Task.FromResult<LicenseResponse?>(null);

            var emailNorm = NormalizeEmail(email);

            var lic = _byKey.AddOrUpdate(
                licenseKey,
                _ => Create(licenseKey, emailNorm, fingerprint, days: 30, status: "active"),
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

            return Task.FromResult<LicenseResponse?>(lic);
        }

        public Task<LicenseResponse?> TryGetByKeyAsync(string licenseKey)
            => Task.FromResult(_byKey.TryGetValue(licenseKey, out var lic) ? lic : null);

        public Task<LicenseResponse> GetOrStartTrialAsync(string fingerprint, string? email, int trialDays)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
                throw new ArgumentException("fingerprint required", nameof(fingerprint));

            var lic = _trialByFp.GetOrAdd(
                fingerprint,
                _ => Create("TRIAL", NormalizeEmail(email), fingerprint, days: trialDays, status: "trial"));

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

        // ===== Logs (no-op aqui; PgRepo implementa gravando no DB) =====
        public Task LogDownloadAsync(string? ip, string? ua, string? referer) => Task.CompletedTask;
        public Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw) => Task.CompletedTask;

        // ===== Helpers =====
        private static string? NormalizeEmail(string? email)
            => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        private static LicenseResponse Create(string licenseId, string? email, string? fingerprint, int days, string status)
            => new LicenseResponse
            {
                Payload = new LicensePayload
                {
                    LicenseId = licenseId,
                    Email = email ?? "",
                    Fingerprint = fingerprint ?? "",
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(days),
                    SubscriptionStatus = status
                }
            };
    }
}

