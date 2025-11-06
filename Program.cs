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

string PrivateKeyPem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM") ?? PrivateKeyPemFallback;

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


