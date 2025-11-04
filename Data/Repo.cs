namespace ProjetoDePlanejamento.LicensingServer
{
    public interface ILicenseRepo
    {
        Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint);
        Task ProlongByKeyAsync(string licenseKey, TimeSpan delta);
        Task ProlongByEmailAsync(string email, TimeSpan delta);
    }

    public sealed class InMemoryRepo : ILicenseRepo
    {
        private readonly HashSet<string> _validKeys;
        private readonly Dictionary<string, SignedLicense> _byKey = new();
        private readonly Dictionary<string, string> _keyByEmail = new();

        public InMemoryRepo(IEnumerable<string> seedKeys) => _validKeys = new HashSet<string>(seedKeys);

        public Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
        {
            if (string.IsNullOrWhiteSpace(licenseKey) || !_validKeys.Contains(licenseKey))
                return Task.FromResult<SignedLicense?>(null);

            if (!_byKey.TryGetValue(licenseKey, out var lic))
            {
                lic = new SignedLicense
                {
                    Payload = new LicensePayload
                    {
                        Type = LicenseType.Subscription,
                        SubscriptionStatus = "active",
                        ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
                        Email = email,
                        Fingerprint = fingerprint
                    }
                };
                _byKey[licenseKey] = lic;
                if (!string.IsNullOrWhiteSpace(email)) _keyByEmail[email!] = licenseKey;
            }
            else
            {
                lic.Payload.ExpiresAtUtc = DateTime.UtcNow.AddDays(30);
                if (!string.IsNullOrWhiteSpace(email)) { lic.Payload.Email = email; _keyByEmail[email!] = licenseKey; }
                if (!string.IsNullOrWhiteSpace(fingerprint)) lic.Payload.Fingerprint = fingerprint;
            }

            return Task.FromResult<SignedLicense?>(lic);
        }

        public Task ProlongByKeyAsync(string licenseKey, TimeSpan delta)
        {
            if (_byKey.TryGetValue(licenseKey, out var lic))
                lic.Payload.ExpiresAtUtc = lic.Payload.ExpiresAtUtc.Add(delta);
            return Task.CompletedTask;
        }

        public Task ProlongByEmailAsync(string email, TimeSpan delta)
        {
            if (_keyByEmail.TryGetValue(email, out var key))
                return ProlongByKeyAsync(key, delta);
            return Task.CompletedTask;
        }
    }
}
