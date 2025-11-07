// Program.cs
using ProjetoDePlanejamento.LicensingServer;
using ProjetoDePlanejamento.LicensingServer.Contracts;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// === NOVOS USINGS PARA TELEMETRIA ===
using Microsoft.Data.Sqlite;
using System.Text.Json.Serialization;

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
//  -> Troque por repo real quando conectar a um DB
builder.Services.AddSingleton<ILicenseRepo>(_ => new InMemoryRepo(new[] { "TESTE-123-XYZ" }));

var app = builder.Build();
app.UseCors();

// ======================================================================
//  Variáveis de ambiente úteis
// ======================================================================
var demoDownloadUrl = Environment.GetEnvironmentVariable("DEMO_DOWNLOAD_URL")
    ?? "https://SEU-DOMINIO/arquivos/PlannusDemoSetup.exe"; // <-- TROQUE DEPOIS

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);
var telemetryDbPath = Path.Combine(dataDir, "telemetry.db");

// ======================================================================
//  CHAVE PRIVADA (use ENV em produção)
//     - PRIVATE_KEY_PEM  : cole a chave PEM completa (com BEGIN/END)
//     - HOTMART_HOTTOK   : seu hottok do Hotmart
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

// Normaliza \n literais caso ENV venha “escapado”
privateKeyPem = privateKeyPem
    .Replace("\\r", "\r")
    .Replace("\\n", "\n")
    .Replace("\r\n", "\n")
    .Trim();

// ===== Throttle (opcional, simples) =====
var lastHitByIp  = new ConcurrentDictionary<string, DateTime>();
var lastHitByKey = new ConcurrentDictionary<string, DateTime>();

// ====== Telemetria: prepara SQLite ======
EnsureTelemetrySchema(telemetryDbPath);

// ===== Rotas utilitárias =====
app.MapGet("/", () => new { ok = true, service = "ProjetoDePlanejamento.LicensingServer" });
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/health", () => new { ok = true });

// ===== DOWNLOAD DEMO (loga e redireciona) =====
app.MapGet("/download/demo", async (HttpContext http) =>
{
    var email = http.Request.Query["email"].ToString(); // opcional: ?email=...
    var ip = GetClientIp(http);
    var ua = http.Request.Headers.UserAgent.ToString();

    await LogDownloadAsync(telemetryDbPath, string.IsNullOrWhiteSpace(email) ? null : email, ip, ua);

    // redireciona para o instalador real
    return Results.Redirect(demoDownloadUrl);
});

// ===== API: ATIVAR (gera/renova licença e assina payload) =====
app.MapPost("/api/activate", async (ActivateRequest req, ILicenseRepo repo, HttpContext http) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.LicenseKey))
        return Results.BadRequest(new { error = "licenseKey obrigatório" });

    // Throttle básico por IP (3s)
    var ip = GetClientIp(http) ?? "unknown";
    if (IsThrottled(lastHitByIp, ip, TimeSpan.FromSeconds(3)))
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    var lic = await repo.IssueOrRenewAsync(req.LicenseKey!.Trim(), req.Email, req.Fingerprint);
    if (lic is null)
        return Results.BadRequest(new { error = "licenseKey inválida" });

    lic.SignatureBase64 = SignPayload(privateKeyPem, lic.Payload);
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

// ==== Modelo do check (entrada do cliente) ====
sealed class CheckRequest
{
    [JsonPropertyName("licenseKey")] public string? LicenseKey { get; set; }
    [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
    [JsonPropertyName("client")] public string? Client { get; set; } // ex.: "Planner/1.2.3"
    [JsonPropertyName("appVersion")] public string? AppVersion { get; set; } // opcional
}

// ===== API: CHECK (ping periódico + log) =====
app.MapPost("/api/check", async (HttpContext http) =>
{
    try
    {
        var req = await http.Request.ReadFromJsonAsync<CheckRequest>() ?? new();
        var fingerprint = (req.Fingerprint ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fingerprint))
            return Results.BadRequest(new { error = "fingerprint obrigatório" });

        var ip = GetClientIp(http);
        var ua = http.Request.Headers.UserAgent.ToString();
        var version = !string.IsNullOrWhiteSpace(req.AppVersion) ? req.AppVersion
                    : (req.Client ?? "").Split('/').LastOrDefault();

        await LogCheckinAsync(telemetryDbPath, fingerprint, version, req.LicenseKey, ip, ua);
        await UpsertActivationAsync(telemetryDbPath, req.LicenseKey, fingerprint);

        var resp = new
        {
            active = true,
            plan = "trial-or-paid",
            nextCheckSeconds = 43200, // 12h
            token = (string?)null
        };
        return Results.Ok(resp);
    }
    catch
    {
        var resp = new { active = true, nextCheckSeconds = 43200 };
        return Results.Ok(resp);
    }
});

// ===== Webhook Hotmart (v2) =====
app.MapPost("/webhook/hotmart", async (JsonDocument body, HttpRequest req, ILicenseRepo repo) =>
{
    // 1) Valida HOTTOK (Trim para eliminar espaços/linhas acidentais)
    var expected = (Environment.GetEnvironmentVariable("HOTMART_HOTTOK") ?? "").Trim();

    string? got =
        req.Headers["hottok"].FirstOrDefault()
        ?? req.Headers["HOTTOK"].FirstOrDefault()
        ?? req.Headers["x-hotmart-hottok"].FirstOrDefault();

    got = got?.Trim();

    if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(got) || !CryptographicEquals(expected, got))
        return Results.Unauthorized();

    // 2) Extrai campos relevantes
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

// ===== Helpers =====
static bool IsThrottled(IDictionary<string, DateTime> map, string key, TimeSpan interval)
{
    var now = DateTime.UtcNow;
    if (map.TryGetValue(key, out var last) && (now - last) < interval) return true;
    map[key] = now;
    return false;
}

static string SignPayload(string privatePem, LicensePayload payload)
{
    var json = JsonSerializer.Serialize(payload);
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

// ===== Helpers Telemetria (SQLite) =====
static void EnsureTelemetrySchema(string dbPath)
{
    using var con = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
    con.Open();

    var cmd = con.CreateCommand();
    cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS download_logs (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  email             TEXT,
  ip                TEXT,
  user_agent        TEXT,
  downloaded_at_utc TEXT
);

CREATE TABLE IF NOT EXISTS checkins (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  fingerprint       TEXT,
  app_version       TEXT,
  license_key       TEXT,
  ip                TEXT,
  user_agent        TEXT,
  checked_at_utc    TEXT
);

CREATE TABLE IF NOT EXISTS activations (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  license_key       TEXT NOT NULL,
  fingerprint       TEXT NOT NULL,
  first_seen_utc    TEXT NOT NULL,
  last_seen_utc     TEXT NOT NULL,
  UNIQUE(license_key, fingerprint)
);";
    cmd.ExecuteNonQuery();
}

static string UtcNow() => DateTime.UtcNow.ToString("u");

static async Task LogDownloadAsync(string dbPath, string? email, string? ip, string? userAgent)
{
    using var con = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
    await con.OpenAsync();
    using var cmd = con.CreateCommand();
    cmd.CommandText = @"INSERT INTO download_logs(email, ip, user_agent, downloaded_at_utc)
                        VALUES (@e, @ip, @ua, @ts)";
    cmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ua", (object?)userAgent ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ts", UtcNow());
    await cmd.ExecuteNonQueryAsync();
}

static async Task UpsertActivationAsync(string dbPath, string? licenseKey, string fingerprint)
{
    licenseKey ??= ""; // trial local pode vir vazio; ainda assim rastreamos
    using var con = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
    await con.OpenAsync();

    // tenta update; se 0 linhas, insere
    using (var upd = con.CreateCommand())
    {
        upd.CommandText = @"UPDATE activations
                            SET last_seen_utc=@now
                            WHERE license_key=@k AND fingerprint=@f";
        upd.Parameters.AddWithValue("@now", UtcNow());
        upd.Parameters.AddWithValue("@k", licenseKey);
        upd.Parameters.AddWithValue("@f", fingerprint);
        var n = await upd.ExecuteNonQueryAsync();
        if (n > 0) return;
    }

    using (var ins = con.CreateCommand())
    {
        ins.CommandText = @"INSERT OR IGNORE INTO activations
                            (license_key, fingerprint, first_seen_utc, last_seen_utc)
                            VALUES (@k, @f, @now, @now)";
        ins.Parameters.AddWithValue("@k", licenseKey);
        ins.Parameters.AddWithValue("@f", fingerprint);
        ins.Parameters.AddWithValue("@now", UtcNow());
        await ins.ExecuteNonQueryAsync();
    }
}

static async Task LogCheckinAsync(string dbPath, string fingerprint, string? appVersion,
                                  string? licenseKey, string? ip, string? userAgent)
{
    using var con = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
    await con.OpenAsync();

    using var cmd = con.CreateCommand();
    cmd.CommandText = @"INSERT INTO checkins(fingerprint, app_version, license_key, ip, user_agent, checked_at_utc)
                        VALUES (@f, @v, @k, @ip, @ua, @ts)";
    cmd.Parameters.AddWithValue("@f", fingerprint);
    cmd.Parameters.AddWithValue("@v", (object?)appVersion ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@k", (object?)licenseKey ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ua", (object?)userAgent ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ts", UtcNow());
    await cmd.ExecuteNonQueryAsync();
}

static string? GetClientIp(HttpContext ctx)
{
    // Railway geralmente injeta X-Forwarded-For
    var fwd = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(fwd)) return fwd.Split(',')[0].Trim();
    return ctx.Connection.RemoteIpAddress?.ToString();
}

app.Run();



