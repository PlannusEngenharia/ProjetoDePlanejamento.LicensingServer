namespace ProjetoDePlanejamento.LicensingServer.Contracts
{
    // /api/activate
    public sealed class ActivateRequest
    {
        public string? LicenseKey { get; set; }
        public string? Email { get; set; }
        public string? Fingerprint { get; set; }
    }

    // /api/status (mock compat)
    public sealed class StatusRequest
{
    public string? LicenseKey { get; set; }
    public string? AppVersion { get; set; }

    // >>> campos novos para controle de trial <<<
    public string? Fingerprint { get; set; }
    public DateTime? TrialStartedUtc { get; set; }
    public DateTime? TrialExpiresUtc { get; set; }
}


    public sealed class StatusResponse
    {
        public DateTime? TrialStartedUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public bool IsActive { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public List<string> Features { get; set; } = new();
    }

    // Webhook (Hotmart) – mock
    public sealed class HotmartWebhook
    {
        public string? Event { get; set; }
        public string? LicenseKey { get; set; }
        public string? BuyerEmail { get; set; }
    }
     // /api/validate

    public sealed class ValidateRequest
    {
        public string? LicenseKey { get; set; }   // opcional (se vier, valida como licença)
        public string? Email { get; set; }        // opcional (telemetria/ajuda a vincular depois)
        public string? Fingerprint { get; set; }  // obrigatório no fluxo de TRIAL
        public string? AppVersion { get; set; }   // opcional
    }

    // /api/deactivate
    public sealed class DeactivateRequest
    {
        public string? LicenseKey { get; set; }
        public string? Fingerprint { get; set; }
        // opcional: motivo/log
        public string? Reason { get; set; }
    }
}
