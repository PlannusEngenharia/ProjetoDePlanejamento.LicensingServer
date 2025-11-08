namespace ProjetoDePlanejamento.LicensingServer
{
    public interface ILicenseRepo
    {
        Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint);
        Task ProlongByKeyAsync(string licenseKey, TimeSpan delta);
        Task ProlongByEmailAsync(string email, TimeSpan delta);
        
        // NOVOS:
        Task<SignedLicense?> TryGetByKeyAsync(string licenseKey);
        Task DeactivateAsync(string licenseKey); // deixa expirada imediatamente
    }

    public sealed class InMemoryRepo : ILicenseRepo
    {
        private readonly HashSet<string> _validKeys;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SignedLicense> _byKey = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _keyByEmail = new();

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

        public Task ProlongByEmailAsync(string email, TimeSpan delta)
        {
            if (string.IsNullOrWhiteSpace(email)) return Task.CompletedTask;

            var emailNorm = NormalizeEmail(email)!;
            if (_keyByEmail.TryGetValue(emailNorm, out var key))
                return ProlongByKeyAsync(key, delta);

            return Task.CompletedTask;
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
