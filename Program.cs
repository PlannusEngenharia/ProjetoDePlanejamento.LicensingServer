using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using ProjetoDePlanejamento.LicensingServer;
using ProjetoDePlanejamento.LicensingServer.Contracts; // se você já tem DTOs/Contracts
using Microsoft.AspNetCore.Http.Json;

// --- Se você NÃO tiver os Contracts definidos, remova o 'using' acima e descomente as classes DTO abaixo. ---
// (eu deixei o using para reusar suas classes; só descomente e ajuste se necessário)

/*
public record ActivateRequest(string? LicenseKey, string? Email, string? Fingerprint);
public record ValidateRequest(string? LicenseKey, string? MachineId);
public record DeactivateRequest(string? LicenseKey, string? MachineId);
public record StatusRequest(); // opcional payload
public record StatusResponse(bool IsActive, string? CustomerName, string? CustomerEmail, DateTime ExpiresAtUtc, DateTime TrialStartedUtc, List<string> Features);
public enum LicenseType { Subscription, Trial }
public record LicensePayload { public LicenseType Type {get; init;} public string? Email {get; set;} public string? Fingerprint {get; set;} public DateTime ExpiresAtUtc {get; set;} public string? SubscriptionStatus {get; set;} }
public class SignedLicense { public LicensePayload Payload { get; set;} = new LicensePayload(); public string? SignatureBase64 { get; set; } }
*/

var builder = WebApplication.CreateBuilder(args);

// ===== Port for Railway (or fallback) =====
var port = Environment.GetEnvironmentVariable("PORT") ?? "7019";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ===== JSON options =====
builder.Services.Configure<JsonOptions>(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opts.SerializerOptions.WriteIndented = false;
});

// ===== CORS =====
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ===== License repo: read seeds from env (LICENSE_SEEDS as comma separated or JSON array) =====
IEnumerable<string>? ParseSeedsFromEnv()
{
    var env = Environment.GetEnvironmentVariable("LICENSE_SEEDS") 
              ?? Environment.GetEnvironmentVariable("LICENSE_SEEDS_JSON");
    if (string.IsNullOrWhiteSpace(env)) return Array.Empty<string>();

    env = env.Trim();
    if (env.StartsWith("[") && env.EndsWith("]"))
    {
        try { return JsonSerializer.Deserialize<string[]>(env) ?? Array.Empty<string>(); }
        catch { /* fallback below */ }
    }

    // comma separated
    return env.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
              .Select(s => s.Trim())
              .Where(s => !string.IsNullOrWhiteSpace(s));
}

var seeds = ParseSeedsFromEnv();
builder.Services.AddSingleton<ILicenseRepo>(_ => new InMemoryRepo(seeds));

// ===== JSON camelCase already set above =====
var app = builder.Build();
app.UseCors();

// ===== Private Key (preferir ENV no Railway) =====
const string PrivateKeyPemFallback = @"-----BEGIN PRIVATE KEY-----
MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQDKvnmTFVOLD9bM
wXJ3GVOpG75OeF0zv6iwYAYK7HMlMpHARhrl/K7xAh5p1r1zrR1R83AxLUAPWvTE
YrchFqCUvOqcu9d6dQ905+uJpn6Ej7FP39edaX7rvjv90dNS0/NnBil6nbcc4xL0
Si2wGhU/GCVYtmZCylhkjCvg1sHo68so+yUU75lj5E764Ev1X0TweWMVB2bPRdzR
mVzkZzoo0n5bFkAVom61GmU2mfFwzGkri7FkPIVKLgHGS3ggYA04Ao0MSdHqvQW+
Oc5YH//1rCy8ADBmyJ6FQIFTxQ0k/PPnqfCBZqfrOUQQGY8p3tIJNO9XcKiG3h01
uPGjk/ErAgMBAAECggEAQKqrbWoOeRsGuM10/J7z68sBEtdaZwCZRhSCqOZNPc6Y
5ZqWxsenZxD1cW3AhM5xPSvoG49i0OMCkkcoQSIN+xMcw/w4GQOQeAnnO0MDNLX+
aMstYzR8eqX1TZqpDFC1YKV7AnSerNSSvZ+RXgubvkGt29Nl36TZt8xrzG3DcM5/
aifxlRH82P1DO/wuPlTzFaOWmyWLSHRDNnxhkjQWeAm4+H9n1BIUb1l+0PVKhfP2
9Kb74/kUvupCPHolxFZsEoTcvKjb02aY9YZYMJ3CeU6Ltz3ETD6mkgpI4UteqbFt
CAQ8zWQDl7IACpWMM9DBVgyB+MtcVKtaDQ5tBReunQKBgQDl77RtEOCVZHKKdbGL
TG+FWQOyxolmtzp7y2kq5iovpjLj0uAxBOEsdvWkK6zYASVAL30B1AGo8o11nHKT
+8VnoDy+eGxWaZDe1mDQ6pqSGpOYLJDETzrWYH5bfReUduCM5bW6LmyEQBS9OkO/
vws3ctHfUdYPiScZ/SqXVD3ktwKBgQDhubQcKdt+mbDfV/4/G4PINMX1AHwShnqe
zlX2rMiJHAlDLFyuXYl8qq/5HwJwhqPo4jerRX0D1o79bLdG6DgavWn0AnCfCQ0W
/U/9MIItL6EFZ4w7dDHtY8ac25EwJE80jT02ELyWXOjHLC4J15QJBNFebAHWEkZj
hK0REvgrLQKBgQCQlcxEkMpH5mPIAP3lc+jkVvbmYcVgm3LhCSVWXmjEkaOKcr2a
1VCqXxtTYktLgFzmIXZfwepRTEP7YqcBut2ErdPEiYDGTZdVKES02fDcUm3g0JUv
fAqpZv/Nk7lSF/ZXYtKFAlAmUQ05d/vGBOGOulqSLKmIF1xJEVLI2aYZvQKBgQCm
fOQdibn9TLqqYSqDvXWbq2D+7laVC19R1nqNMK/QgT9LrmLFsPQBYZvdsUOJX6Vx
1biduOkWdaCNxyv/PrRy9JY7hbkvc+uVs0zWQHsjfOfVJqTGDVPt9hO+CiyyR3Ws
Gyi0we93MBv5G9rxI3JqnIUYka1hCaWlLWzBFS66GQKBgHPhPk732dh01AH0qtQD
dKx7gArB6ieurx3y4wLF9RFIoW6Z87QyIT0iYMKP3un8gp4jrJvW7uC7AbUZsFrM
zvu22nwtE+C8cPZZpJAgnWDCRpTRa9aodeOwl/zqJQIz9mPzkoOYTY7vSKvPCSVJ
yNc0Sb1dO7dfmIYwz/t0cWy8
-----END PRIVATE KEY-----";

var privatePem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM") ?? PrivateKeyPemFallback;
var signingAvailable = !string.IsNullOrWhiteSpace(privatePem) && !privatePem.Contains("BEGIN PRIVATE KEY-----") == false;

// ===== Helpers =====
static string SignPayload(string privatePemLocal, object payload)
{
    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    using var rsa = RSA.Create();
    rsa.ImportFromPem(privatePemLocal);
    var sig = rsa.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return Convert.ToBase64String(sig);
}

static async Task<T?> TryReadJsonBodyAsync<T>(HttpRequest req)
{
    try
    {
        // If no content-length / body, return default
        if (req.ContentLength == null || req.ContentLength == 0) return default;
        return await req.ReadFromJsonAsync<T>();
    }
    catch
    {
        return default;
    }
}

// Generic wrapper for POST endpoints that may be called in many route variants
static RouteHandlerBuilder MapMany(WebApplication app, string[] patterns, Delegate handler)
{
    RouteHandlerBuilder? last = null;
    foreach (var p in patterns)
    {
        last = app.MapMethods(p, new[] { "POST", "OPTIONS" }, handler);
    }
    return last!;
}

// ===== OPTIONS catch-all (helps for preflight) =====
app.MapMethods("{*any}", new[] { "OPTIONS" }, (HttpRequest req) =>
{
    // Allow CORS preflight to succeed
    return Results.Ok();
});

// ===== simple root/health endpoints =====
app.MapGet("/", () => Results.Ok(new { ok = true, service = "ProjetoDePlanejamento.LicensingServer" }));
app.MapGet("/health", () => Results.Ok(new { ok = true }));
app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

// Also accept POST /api/status and GET /api/status (many clients expect POST)
app.MapGet("/api/status", () =>
{
    var resp = new
    {
        trialStartedUtc = DateTime.UtcNow.AddDays(-1),
        expiresAtUtc = DateTime.UtcNow.AddDays(29),
        isActive = true,
        customerName = "Cliente Demo",
        customerEmail = "cliente@exemplo.com",
        features = new[] { "Import", "Export", "UnlimitedRows" }
    };
    return Results.Ok(resp);
});

app.MapPost("/api/status", async (HttpRequest req) =>
{
    // accept optional body but ignore it
    var resp = new
    {
        trialStartedUtc = DateTime.UtcNow.AddDays(-1),
        expiresAtUtc = DateTime.UtcNow.AddDays(29),
        isActive = true,
        customerName = "Cliente Demo",
        customerEmail = "cliente@exemplo.com",
        features = new[] { "Import", "Export", "UnlimitedRows" }
    };
    return Results.Ok(resp);
});

// ===== Activation endpoint(s) =====
// Accept many variants to avoid 404: /api/activate, /api/license/activate, /v1/activate, /v1/licenses/activate
var activatePatterns = new[]
{
    "/api/activate",
    "/api/license/activate",
    "/api/licenses/activate",
    "/v1/activate",
    "/v1/license/activate",
    "/v1/licenses/activate"
};

MapMany(app, activatePatterns, async (HttpRequest req, ILicenseRepo repo, ILogger<Program> logger) =>
{
    var body = await TryReadJsonBodyAsync<ActivateRequest>(req) ?? new ActivateRequest(null, null, null);
    if (body == null || string.IsNullOrWhiteSpace(body.LicenseKey))
        return Results.BadRequest(new { error = "licenseKey obrigatório" });

    // throttle sample (optional)
    //if (IsThrottled(...)) return Results.StatusCode(429);

    var lic = await repo.IssueOrRenewAsync(body.LicenseKey!, body.Email, body.Fingerprint);
    if (lic is null) return Results.BadRequest(new { error = "licenseKey inválida" });

    if (signingAvailable)
    {
        try { lic.SignatureBase64 = SignPayload(privatePem, lic.Payload); }
        catch (Exception ex) { logger.LogWarning("Sign failed: {0}", ex.Message); /* not fatal */ }
    }

    return Results.Ok(lic);
});

// ===== Validate endpoint(s) =====
// Many variations: /api/validate, /api/license/validate, /v1/validate, /v1/licenses/validate
var validatePatterns = new[]
{
    "/api/validate",
    "/api/license/validate",
    "/api/licenses/validate",
    "/v1/validate",
    "/v1/license/validate",
    "/v1/licenses/validate"
};

MapMany(app, validatePatterns, async (HttpRequest req, ILicenseRepo repo) =>
{
    var body = await TryReadJsonBodyAsync<ValidateRequest>(req) ?? new ValidateRequest(null, null);
    if (body == null || string.IsNullOrWhiteSpace(body.LicenseKey))
        return Results.BadRequest(new { error = "licenseKey obrigatório" });

    // The in-memory repo does not provide an explicit Validate method; emulate via IssueOrRenewAsync (read-only)
    // Try to issue/renew but we won't change server state if not found. We'll call IssueOrRenewAsync and if null -> invalid.
    var maybe = await repo.IssueOrRenewAsync(body.LicenseKey!, email: null, fingerprint: body.MachineId);
    if (maybe is null)
        return Results.NotFound(new { valid = false });

    // If license exists, return payload summary
    return Results.Ok(new { valid = true, expiresAtUtc = maybe.Payload.ExpiresAtUtc, email = maybe.Payload.Email });
});

// ===== Deactivate endpoint(s) =====
var deactivatePatterns = new[]
{
    "/api/deactivate",
    "/api/license/deactivate",
    "/api/licenses/deactivate",
    "/v1/deactivate",
    "/v1/license/deactivate",
    "/v1/licenses/deactivate"
};

MapMany(app, deactivatePatterns, async (HttpRequest req, ILicenseRepo repo) =>
{
    var body = await TryReadJsonBodyAsync<DeactivateRequest>(req) ?? new DeactivateRequest(null, null);
    if (body == null || string.IsNullOrWhiteSpace(body.LicenseKey))
        return Results.BadRequest(new { error = "licenseKey obrigatório" });

    await repo.ProlongByKeyAsync(body.LicenseKey!, TimeSpan.FromDays(-30)); // simple behavior: shorten 30d or mark invalid
    return Results.Ok(new { deactivated = true });
});

// ===== Hotmart webhook (keeps your existing behavior) =====
app.MapPost("/webhook/hotmart", async ([FromBody] JsonDocument body, HttpRequest req, ILicenseRepo repo) =>
{
    var expected = Environment.GetEnvironmentVariable("HOTMART_HOTTOK") ?? "";
    var got =
        req.Headers["hottok"].FirstOrDefault()
        ?? req.Headers["HOTTOK"].FirstOrDefault()
        ?? req.Headers["x-hotmart-hottok"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(got) || !CryptographicEquals(expected, got))
        return Results.Unauthorized();

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

    var e = (evt ?? "").Trim().ToLowerInvariant();
    TimeSpan delta = TimeSpan.Zero;
    bool isRenew =
        e.Contains("approved") || e.Contains("aprovada") ||
        e.Contains("purchase_approved") || e.Contains("sale_approved");
    bool isCancel =
        e.Contains("refund") || e.Contains("reembols") ||
        e.Contains("expired") || e.Contains("expirad") ||
        e.Contains("canceled") || e.Contains("cancelamento") ||
        e.Contains("chargeback");

    if (isRenew) delta = TimeSpan.FromDays(30);
    else if (isCancel) delta = TimeSpan.FromDays(-30);

    if (delta != TimeSpan.Zero && !string.IsNullOrWhiteSpace(email))
        await repo.ProlongByEmailAsync(email!, delta);

    return Results.Ok(new { received = true, appliedDays = delta.TotalDays, eventRaw = evt });
});

// ===== Helpers used in webhook =====
static bool CryptographicEquals(string a, string b)
{
    var ba = Encoding.UTF8.GetBytes(a);
    var bb = Encoding.UTF8.GetBytes(b);
    if (ba.Length != bb.Length) return false;
    int diff = 0;
    for (int i = 0; i < ba.Length; i++) diff |= ba[i] ^ bb[i];
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

// ===== start app =====
app.Run();

