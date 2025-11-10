// Program.cs
using ProjetoDePlanejamento.LicensingServer;
using ProjetoDePlanejamento.LicensingServer.Contracts;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjetoDePlanejamento.LicensingServer.Data;


var builder = WebApplication.CreateBuilder(args);

// ===== Porta para Railway =====
var port = Environment.GetEnvironmentVariable("PORT") ?? "7019";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ===== JSON camelCase =====
builder.Services.ConfigureHttpJsonOptions(
    (Microsoft.AspNetCore.Http.Json.JsonOptions o) =>
    {
        o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.SerializerOptions.WriteIndented = false;
    });

// ===== CORS =====
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ===== Repositório em memória (seed de teste) =====
//  -> Troque pelo seu repositório real quando conectar a um DB
var cs = Environment.GetEnvironmentVariable("DATABASE_URL");

if (!string.IsNullOrWhiteSpace(cs))
{
    // Converta a URL do Railway para uma connection string Npgsql
    var pg = BuildPgConnectionString(cs);
    builder.Services.AddSingleton<ILicenseRepo>(_ => new PgRepo(pg));
}
else
{
    builder.Services.AddSingleton<ILicenseRepo>(_ => new InMemoryRepo(new[] { "TESTE-123-XYZ" }));
}



var app = builder.Build();
app.UseCors();

// Program.cs
var SigJson = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // <-- TEM que ser camelCase
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = false
};




// ======================================================================
//  CHAVE PRIVADA (use ENV em produção)
//
//  1) Railway -> Variables:
//     - PRIVATE_KEY_PEM  : cole a chave PEM completa (com BEGIN/END)
//     - HOTMART_HOTTOK   : seu hottok do Hotmart
//
//  2) Se a variável não existir, usa o fallback abaixo (apenas DEV).
// ======================================================================
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

string privateKeyPem = Environment.GetEnvironmentVariable("PRIVATE_KEY_PEM") ?? PrivateKeyPemFallback;

// Normaliza quebras de linha caso o ENV tenha vindo com \n literais
privateKeyPem = privateKeyPem
    .Replace("\\r", "\r")
    .Replace("\\n", "\n")
    .Replace("\r\n", "\n")
    .Trim();

// ===== Throttle (opcional, simples) =====
var lastHitByIp  = new ConcurrentDictionary<string, DateTime>();
var lastHitByKey = new ConcurrentDictionary<string, DateTime>();

// ===== Rotas utilitárias =====
app.MapGet("/", () => new { ok = true, service = "ProjetoDePlanejamento.LicensingServer" });
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/health", () => new { ok = true });

// ===== API: ATIVAR (gera/renova licença e assina payload) =====
app.MapPost("/api/activate", async (ActivateRequest req, ILicenseRepo repo, HttpContext http) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.LicenseKey))
        return Results.BadRequest(new { error = "licenseKey obrigatório" });

    // Throttle básico por IP (3s)
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (IsThrottled(lastHitByIp, ip, TimeSpan.FromSeconds(3)))
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    var lic = await repo.IssueOrRenewAsync(req.LicenseKey!.Trim(), req.Email, req.Fingerprint);
    if (lic is null)
        return Results.BadRequest(new { error = "licenseKey inválida" });

    lic.SignatureBase64 = SignPayload(privateKeyPem, lic.Payload, SigJson);
    return Results.Ok(lic);
});

// ===== API: STATUS (stub para POC) =====
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

// ===== API: CHECK (ping periódico do app cliente) =====
app.MapPost("/api/check", (HttpRequest _) =>
{
    var resp = new
    {
        active = true,
        plan = "monthly",
        nextCheckSeconds = 43200, // 12h
        token = (string?)null
    };
    return Results.Ok(resp);
});

// ===== Webhook Hotmart (v2) =====
app.MapPost("/webhook/hotmart", async (JsonDocument body, HttpRequest req, ILicenseRepo repo) =>
{
    // 1) Valida HOTTOK (faz Trim para eliminar espaços/linhas acidentais)
    var expected = (Environment.GetEnvironmentVariable("HOTMART_HOTTOK") ?? "").Trim();

    string? got =
        req.Headers["hottok"].FirstOrDefault()
        ?? req.Headers["HOTTOK"].FirstOrDefault()
        ?? req.Headers["x-hotmart-hottok"].FirstOrDefault();

    got = got?.Trim();

    if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(got) || !CryptographicEquals(expected, got))
        return Results.Unauthorized();

    // ... (restante do handler fica igual)
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


// ===== Helpers =====
static string BuildPgConnectionString(string databaseUrl)
{
    var uri = new Uri(databaseUrl); // aceita postgres:// e postgresql://
    var userInfo = uri.UserInfo.Split(':', 2);

    var builder = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.Trim('/'),
        Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "",
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        SslMode = Npgsql.SslMode.Require,   // Railway normalmente exige SSL
        Pooling = true,
        MaxPoolSize = 20                    // <- nome correto
        // TrustServerCertificate = true   // REMOVER: obsoleto e inútil
    };

    return builder.ToString();
}



static bool IsThrottled(IDictionary<string, DateTime> map, string key, TimeSpan interval)
{
    var now = DateTime.UtcNow;
    if (map.TryGetValue(key, out var last) && (now - last) < interval) return true;
    map[key] = now;
    return false;
}

static string SignPayload(string privatePem, LicensePayload payload, JsonSerializerOptions sigJson)
{
    var json = JsonSerializer.Serialize(payload, sigJson);
    using var rsa = RSA.Create();
    rsa.ImportFromPem(privatePem);
    var sig = rsa.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return Convert.ToBase64String(sig);
}

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
// ===== Download trial (redireciona para o instalador do GitHub) =====
// Use uma variável de ambiente para não “fixar” a versão no código
var trialUrl = Environment.GetEnvironmentVariable("DOWNLOAD_TRIAL_URL")
    ?? "https://github.com/PlannusEngenharia/ProjetoDePlanejamento.LicensingServer/releases/download/v1.0.0/PlannusSetup-1.0.0.exe";


app.MapGet("/download/demo", async (HttpContext ctx, ILicenseRepo repo) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var ua = ctx.Request.Headers["User-Agent"].ToString();
    var referer = ctx.Request.Headers["Referer"].ToString();

    try
    {
        await repo.LogDownloadAsync(ip, ua, referer);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[download/log] erro: {ex.Message}");
    }

    var trialUrl = Environment.GetEnvironmentVariable("DOWNLOAD_TRIAL_URL");
    if (string.IsNullOrWhiteSpace(trialUrl))
    {
        Console.WriteLine("[download] DOWNLOAD_TRIAL_URL não configurada!");
        return Results.Problem("A URL de download não está configurada no servidor (DOWNLOAD_TRIAL_URL).");
    }

    try
    {
        Console.WriteLine($"[download] redirecionando para {trialUrl}");
        return Results.Redirect(trialUrl, permanent: false);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[download/redirect] erro: {ex.Message}");
        return Results.Problem($"Erro ao redirecionar: {ex.Message}");
    }
});



// === VALIDATE ===

app.MapPost("/api/validate", async (ValidateRequest req, ILicenseRepo repo) =>
{
    // ===== Fluxo 1: LICENÇA (se vier licenseKey, não muda sua lógica já aprovada) =====
    if (!string.IsNullOrWhiteSpace(req.LicenseKey))
    {
        var lic = await repo.TryGetByKeyAsync(req.LicenseKey!);
        if (lic is null)
            return Results.Ok(new { ok = false, reason = "license_not_found" });

        var ok = lic.Payload.ExpiresAtUtc > DateTime.UtcNow
                 && !string.Equals(lic.Payload.SubscriptionStatus, "canceled", StringComparison.OrdinalIgnoreCase);

        // Assina (se ainda não tiver assinatura populada neste objeto)
        // opcional: se você já assina no Issue/Renew, pode manter
        // lic.SignatureBase64 ??= SignPayload(PrivateKeyPem, lic.Payload);

        return Results.Ok(new
        {
            ok,
            subscriptionStatus = lic.Payload.SubscriptionStatus, // "active" / "canceled"
            expiresAtUtc = lic.Payload.ExpiresAtUtc,
            email = lic.Payload.Email,
            fingerprint = lic.Payload.Fingerprint
        });
    }

    // ===== Fluxo 2: TRIAL (sem licenseKey, exige fingerprint) =====
    if (string.IsNullOrWhiteSpace(req.Fingerprint))
        return Results.BadRequest(new { error = "missing fingerprint for trial validation" });

    // inicia (uma única vez) ou retorna o trial existente
    var trial = await repo.GetOrStartTrialAsync(req.Fingerprint!, req.Email, InMemoryRepo.TrialDays);

    // assina o payload, igual às licenças
    trial.SignatureBase64 = SignPayload(privateKeyPem, trial.Payload, SigJson);

    var trialOk = trial.Payload.ExpiresAtUtc > DateTime.UtcNow;
    return Results.Ok(new
    {
        ok = trialOk,
        subscriptionStatus = trial.Payload.SubscriptionStatus, // "trial"
        expiresAtUtc = trial.Payload.ExpiresAtUtc,
        email = trial.Payload.Email,
        fingerprint = trial.Payload.Fingerprint,

        // limites sugeridos para o cliente respeitar:
        features = new[] { "rows:max:30", "print:off" } // <- não mexe nos seus models
    });
});


// === DEACTIVATE ===
app.MapPost("/api/deactivate", async (DeactivateRequest req, ILicenseRepo repo) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.LicenseKey))
        return Results.BadRequest(new { ok = false, error = "licenseKey obrigatório" });

    await repo.DeactivateAsync(req.LicenseKey!);
    return Results.Ok(new { ok = true });
});



app.Run();

