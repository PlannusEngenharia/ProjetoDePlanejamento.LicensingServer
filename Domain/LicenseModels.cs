namespace ProjetoDePlanejamento.LicensingServer
{
    public enum LicenseType { Subscription, Trial, Demo }

    public sealed class LicensePayload
    {
        public string LicenseId { get; set; } = Guid.NewGuid().ToString("N");
        public LicenseType Type { get; set; } = LicenseType.Subscription;
        public string SubscriptionStatus { get; set; } = "active";
        public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddDays(30);
        public string? Email { get; set; }
        public string? Fingerprint { get; set; }
    }

    public sealed class SignedLicense
    {
        public LicensePayload Payload { get; set; } = new();
        public string SignatureBase64 { get; set; } = "";
    }
}
