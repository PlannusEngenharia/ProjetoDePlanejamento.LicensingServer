// Program.cs
using ProjetoDePlanejamento.LicensingServer;
using ProjetoDePlanejamento.LicensingServer.Contracts;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ===== Porta para Railway =====
var port = Environment.GetEnvironmentVariable("PORT") ?? "7019";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ===== JSON camelCase (força o tipo para evitar ambiguidade) =====
builder.Services.ConfigureHttpJsonOptions(
    (Microsoft.AspNetCore.Http.Json.JsonOptions o) =>
    {
        o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.SerializerOptions.WriteIndented = false;
    });

// ===== CORS =====
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ===== Repositório em memória (chave seed de teste) =====
builder.Services.AddSingleton<ILicenseRepo>(_ => new InMemoryRepo(new[] { "TESTE-123-XYZ" }));

var app = builder.Build();
app.UseCors();

$base = "https://projetodeplanejamentolicensingserver-production.up.railway.app"

# health
Invoke-RestMethod -Uri "$base/health" -Method Get

# activate
$payload = @{
  licenseKey  = "TESTE-123-XYZ"
  email       = "teste@exemplo.com"
  fingerprint = "PC-DEMO-001"
} | ConvertTo-Json
Invoke-RestMethod -Uri "$base/api/activate" -Method Post -Body $payload -ContentType "application/json"




// ===== Assina payload com RS256 =====
static string SignPayload(string privatePem, LicensePayload payload)
{
    var json = JsonSerializer.Serialize(payload);
    using var rsa = RSA.Create();
    rsa.ImportFromPem(privatePem);
    var sig = rsa.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return Convert.ToBase64String(sig);
}

// ===== (opcional) throttle helpers =====
var lastHitByIp  = new ConcurrentDictionary<string, DateTime>();
var lastHitByKey = new ConcurrentDictionary<string, DateTime>();
static bool IsThrottled(IDictionary<string, DateTime> map, string key, TimeSpan interval)
{
    var now = DateTime.UtcNow;
    if (map.TryGetValue(key, out var last) && (now - last) < interval) return true;
    map[key] = now;
    return false;
}

// ===== Util =====
app.MapGet("/", () => new { ok = true, service = "ProjetoDePlanejamento.LicensingServer" });
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/health", () => new { ok = true });

// ===== API =====
app.MapPost("/api/activate", async (ActivateRequest req, ILicenseRepo repo) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.LicenseKey))
        return Results.BadRequest(new { error = "licenseKey obrigatório" });

    var lic = await repo.IssueOrRenewAsync(req.LicenseKey!, req.Email, req.Fingerprint);
    if (lic is null)
        return Results.BadRequest(new { error = "licenseKey inválida" });

    lic.SignatureBase64 = SignPayload(PrivateKeyPem, lic.Payload);
    return Results.Ok(lic);
});

app.MapPost("/api/status", (StatusRequest req) =>
{
    var resp = new StatusResponse
    {
        TrialStartedUtc = DateTime.UtcNow.AddDays(-1),
        ExpiresAtUtc    = DateTime.UtcNow.AddDays(29),
        IsActive        = true,
        CustomerName    = "Cliente Demo",
        CustomerEmail   = "cliente@exemplo.com",
        Features        = new() { "Import", "Export", "UnlimitedRows" }
    };
    return Results.Ok(resp);
});

// ===== Webhook Hotmart (v2) =====
app.MapPost("/webhook/hotmart", async (JsonDocument body, HttpRequest req, ILicenseRepo repo) =>
{
    // 1) HOTTOK
    var expected = Environment.GetEnvironmentVariable("HOTMART_HOTTOK") ?? "";
    var got =
        req.Headers["hottok"].FirstOrDefault()
        ?? req.Headers["HOTTOK"].FirstOrDefault()
        ?? req.Headers["x-hotmart-hottok"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(got) || !CryptographicEquals(expected, got))
        return Results.Unauthorized();

    // 2) Parse resiliente
    var root = body.RootElement;

    string? evt =
        TryGetString(root, "event") ??
        TryGetString(root, "event_key") ??
        TryGetString(root, "status") ??
        TryGetString(root, "type");

    string? email =
        TryGetString(root, "buyer_email") ??
        TryGetString(root, "email") ??
        TryGetString(root, "customer_email") ??
        TryGetByPath(root, "data.buyer.email") ??
        TryGetByPath(root, "subscription.customer.email");

    // 3) Normaliza evento
    var e = (evt ?? "").Trim().ToLowerInvariant();

    bool isRenew =
        e.Contains("approved") || e.Contains("aprovad") ||
        e.Contains("purchase_approved") || e.Contains("sale_approved");

    bool isCancel =
        e.Contains("refund") || e.Contains("reembols") ||
        e.Contains("expired") || e.Contains("expirad") ||
        e.Contains("canceled") || e.Contains("cancel") ||
        e.Contains("chargeback");

    TimeSpan delta =
        isRenew ? TimeSpan.FromDays(30) :
        isCancel ? TimeSpan.FromDays(-30) :
        TimeSpan.Zero;

    if (delta != TimeSpan.Zero && !string.IsNullOrWhiteSpace(email))
        await repo.ProlongByEmailAsync(email!.Trim().ToLowerInvariant(), delta);

    return Results.Ok(new { received = true, appliedDays = delta.TotalDays, eventRaw = evt });
});
app.MapPost("/api/check", async (HttpRequest req) =>
{
    // Pode ler o body se quiser (licenseKey/fingerprint). Para POC, ignora.
    var resp = new
    {
        active = true,           // ou false conforme sua regra
        plan = "monthly",
        nextCheckSeconds = 43200, // 12h de backoff
        token = (string?)null     // se quiser, depois mande um JWT RS256 aqui
    };
    return Results.Ok(resp);
});

// ===== helpers =====
static bool CryptographicEquals(string a, string b)
{
    var ba = Encoding.UTF8.GetBytes(a);
    var bb = Encoding.UTF8.GetBytes(b);
    if (ba.Length != bb.Length) return false;
    int diff = 0; for (int i = 0; i < ba.Length; i++) diff |= ba[i] ^ bb[i];
    return diff == 0;
}

static string? TryGetString(JsonElement el, string name)
    => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

static string? TryGetByPath(JsonElement el, string path)
{
    var cur = el;
    foreach (var seg in path.Split('.'))
    {
        if (!cur.TryGetProperty(seg, out var next)) return null;
        cur = next;
    }
    return cur.ValueKind == JsonValueKind.String ? cur.GetString() : null;
}

app.Run();


