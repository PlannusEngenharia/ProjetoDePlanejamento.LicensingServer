namespace ProjetoDePlanejamento.LicensingServer
{
    public enum LicenseType { Trial = 0, Subscription = 1 }

    public sealed class LicensePayload
{
    public string? LicenseKey { get; set; }
    public string? LicenseId { get; set; }
    public string? PlanId { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }

    public LicenseType Type { get; set; } = LicenseType.Trial;
    public string SubscriptionStatus { get; set; } = "trial";
    public int MaxMachines { get; set; } = 1;

    public string? Email { get; set; }
    public string? Fingerprint { get; set; }

    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }

    public List<string> Features { get; set; } = new();
}

public sealed class SignedLicense
{
    public LicensePayload Payload { get; set; } = new();
    public string SignatureBase64 { get; set; } = "";
}
}
