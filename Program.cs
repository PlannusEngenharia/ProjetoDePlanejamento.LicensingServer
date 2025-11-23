// Program.cs
using ProjetoDePlanejamento.LicensingServer;
using ProjetoDePlanejamento.LicensingServer.Contracts;
using ProjetoDePlanejamento.LicensingServer.Data;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

// ===== Repositório (Postgres Railway ou InMemory) =====
var cs = Environment.GetEnvironmentVariable("DATABASE_URL");

if (!string.IsNullOrWhiteSpace(cs))
{
    var pg = BuildPgConnectionString(cs);
    builder.Services.AddSingleton<ILicenseRepo>(_ => new PgRepo(pg));
}
else
{
    builder.Services.AddSingleton<ILicenseRepo>(_ => new InMemoryRepo(new[] { "TESTE-123-XYZ" }));
}

var app = builder.Build();
app.UseCors();

// ===== JSON para assinatura de payload =====
var SigJson = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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

// ===== Throttle (se quiser usar depois) =====
var lastHitByIp  = new ConcurrentDictionary<string, DateTime>();
var lastHitByKey = new ConcurrentDictionary<string, DateTime>();

// ===== Rotas utilitárias =====
app.MapGet("/", () => new { ok = true, service = "ProjetoDePlanejamento.LicensingServer" });
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/health", () => new { ok = true });



// ===== API: ATIVAR (gera/renova licença e devolve SignedLicense) =====
app.MapPost("/api/activate", async (ActivateRequest req, ILicenseRepo repo) =>
{
    if (string.IsNullOrWhiteSpace(req.LicenseKey))
        return Results.BadRequest(new { ok = false, error = "licenseKey_obrigatorio" });

    // fingerprint enviada pelo cliente
    var fp = string.IsNullOrWhiteSpace(req.Fingerprint)
        ? null
        : req.Fingerprint.Trim();

    // 1) Se já existe essa licença ativa em OUTRO PC -> bloqueia
    if (!string.IsNullOrWhiteSpace(fp))
    {
        var bound = await repo.GetLicenseWithFingerprintCheckAsync(req.LicenseKey!, fp);
        if (bound is null)
        {
            return Results.Ok(new
{
    ok = false,
    error = "license_in_use_in_other_computer"
});

        }
    }

    // 2) Confere/renova licença normalmente
    var lic = await repo.IssueOrRenewAsync(req.LicenseKey!, req.Email, fp);
    if (lic is null)
        return Results.NotFound(new { ok = false, error = "license_not_found_or_canceled" });

    // 3) Normaliza payload como assinatura mensal
    lic.Payload.Type = LicenseType.Subscription;
    if (string.IsNullOrWhiteSpace(lic.Payload.PlanId))
        lic.Payload.PlanId = "monthly";

    if (lic.Payload.Features == null || lic.Payload.Features.Count == 0)
    {
        lic.Payload.Features = new()
        {
            "Import",
            "Export",
            "UnlimitedRows"
        };
    }

       // 4) Assina o payload com a chave privada
    lic.SignatureBase64 = SignPayload(privateKeyPem, lic.Payload, SigJson);

    // 5) Registra ativação dessa máquina
    try
    {
        if (!string.IsNullOrWhiteSpace(fp))
        {
            await repo.RecordActivationAsync(
                req.LicenseKey!,
                fp,
                lic.Payload.SubscriptionStatus ?? "active");
        }
    }
    catch
    {
        // falha de log não deve quebrar a ativação
    }

    // 6) Retorna o SignedLicense completo (como o cliente espera)
    return Results.Ok(new
    {
        ok = true,
        payload = lic.Payload,
        signatureBase64 = lic.SignatureBase64
    });
});





// ===== API: STATUS =====
app.MapPost("/api/status", async (StatusRequest req, HttpContext http, ILicenseRepo repo) =>
{
    var key = req.LicenseKey?.Trim();
    SignedLicense? lic = null;

    if (!string.IsNullOrEmpty(key))
    {
        // assinatura paga -> busca no Postgres
        lic = await repo.TryGetByKeyAsync(key);
    }

    var clientIp       = http.Connection.RemoteIpAddress?.ToString();
    var clientVersion  = req.AppVersion;

    // ---------------------------
    // CAMINHO 1: LICENÇA ENCONTRADA
    // ---------------------------
    if (lic is not null && lic.Payload is not null)
    {
        var p = lic.Payload;
        var isActive =
            p.ExpiresAtUtc > DateTime.UtcNow &&
            !string.Equals(p.SubscriptionStatus, "canceled", StringComparison.OrdinalIgnoreCase);

        var resp = new StatusResponse
        {
            TrialStartedUtc = null,
            ExpiresAtUtc    = p.ExpiresAtUtc,
            IsActive        = isActive,
            CustomerName    = p.Email,
            CustomerEmail   = p.Email,
            Features        = p.Features ?? new List<string>()
        };

        return Results.Ok(resp);
    }

    // ---------------------------
    // CAMINHO 2: TRIAL (SEM LICENÇA)
    // ---------------------------
    if (!string.IsNullOrWhiteSpace(req.Fingerprint))
    {
        await repo.UpsertTrialDeviceAsync(
            req.Fingerprint.Trim(),
            req.TrialStartedUtc,
            req.TrialExpiresUtc,
            clientVersion,
            clientIp);
    }

    // resposta "genérica" de trial para o cliente
    var trialExpires = req.TrialExpiresUtc ?? DateTime.UtcNow.AddDays(7);

    var trialResp = new StatusResponse
    {
        TrialStartedUtc = req.TrialStartedUtc,
        ExpiresAtUtc    = trialExpires,
        IsActive        = true,
        CustomerName    = null,
        CustomerEmail   = null,
        Features        = new List<string>() // sem features especiais
    };

    return Results.Ok(trialResp);
});



// ===== API: CHECK (ping periódico do app cliente) =====
app.MapPost("/api/check", async (CheckRequest req, HttpContext http, ILicenseRepo repo) =>
{
    var fp  = req.Fingerprint?.Trim();
    var key = req.LicenseKey?.Trim();
    SignedLicense? lic = null;

    // LICENÇA PAGA
    if (!string.IsNullOrWhiteSpace(key))
    {
        lic = await repo.GetLicenseWithFingerprintCheckAsync(key, fp);

        if (lic is null)
        {
            return Results.Ok(new
            {
                active = false,
                plan = "subscription",
                nextCheckSeconds = 43200,
                token = (string?)null,
                error = "license_bound_to_other_machine"
            });
        }
    }
    // TRIAL (sem licenseKey, mas com fingerprint)
    else if (!string.IsNullOrWhiteSpace(fp))
    {
        // cria/renova licença trial na tabela licenses
        lic = await repo.GetOrStartTrialAsync(fp, null, InMemoryRepo.TrialDays);

        // >>> também registra na trial_devices <<<
        try
        {
            await repo.UpsertTrialDeviceAsync(
                fp,
                lic.Payload?.IssuedAtUtc,          // início do trial
                lic.Payload?.ExpiresAtUtc,         // fim do trial
                req.Client,                        // versão que veio do cliente (ex: "Planner/1.0.0")
                http.Connection.RemoteIpAddress?.ToString()
            );
        }
        catch
        {
            // falha de log não deve quebrar o check
        }
    }

    var active = lic != null &&
                 lic.Payload.ExpiresAtUtc > DateTime.UtcNow &&
                 !string.Equals(
                     lic.Payload.SubscriptionStatus,
                     "canceled",
                     StringComparison.OrdinalIgnoreCase);

    // Apenas registra activations se a licença está válida E o fingerprint bate
    if (active &&
        !string.IsNullOrWhiteSpace(fp) &&
        lic.Payload.Fingerprint != null &&
        string.Equals(lic.Payload.Fingerprint, fp, StringComparison.OrdinalIgnoreCase))
    {
        await repo.RecordActivationAsync(
            lic.Payload.LicenseId!,
            fp!,
            lic.Payload.SubscriptionStatus ?? "active"
        );
    }

    var plan = lic?.Payload.SubscriptionStatus == "trial" ? "trial" : "subscription";

    return Results.Ok(new
    {
        active,
        plan,
        nextCheckSeconds = 43200,
        token = (string?)null
    });
});



// ===============================================
// WEBHOOK HOTMART
// ===============================================
app.MapPost("/webhook/hotmart", async (JsonDocument body, HttpRequest req, ILicenseRepo repo) =>
{
    var expected = (Environment.GetEnvironmentVariable("HOTMART_HOTTOK") ?? "").Trim();
    string? got =
        req.Headers["hottok"].FirstOrDefault()
        ?? req.Headers["HOTTOK"].FirstOrDefault()
        ?? req.Headers["x-hotmart-hottok"].FirstOrDefault();
    got = got?.Trim();

    if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(got) || !CryptographicEquals(expected, got))
        return Results.Unauthorized();

    var root = body.RootElement;

    string? evt =
        TryGetString(root, "event") ??
        TryGetString(root, "event_key") ??
        TryGetString(root, "status") ??
        TryGetString(root, "type");

    string? emailRaw =
        TryGetString(root, "buyer_email") ??
        TryGetString(root, "email") ??
        TryGetString(root, "customer_email") ??
        TryGetByPath(root, "data.buyer.email") ??
        TryGetByPath(root, "subscription.customer.email");

    var e = (evt ?? "").Trim().ToLowerInvariant();
    var email = string.IsNullOrWhiteSpace(emailRaw) ? null : emailRaw!.Trim().ToLowerInvariant();

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
        isCancel ? TimeSpan.FromDays(-3650) :
        TimeSpan.Zero;

    if (!string.IsNullOrWhiteSpace(email))
    {
        if (isRenew)
        {
            await EnsureLicenseForEmailAsync(repo, email, TimeSpan.FromDays(30));
        }
        else if (isCancel)
        {
            await repo.ProlongByEmailAsync(email, delta);
        }
    }

    try { await repo.LogWebhookAsync(evt, email, (int)delta.TotalDays, body); } catch { }

    return Results.Ok(new { received = true, eventRaw = evt, email, appliedDays = delta.TotalDays });
});

// ===== Download trial (redireciona para o instalador do GitHub) =====
// lê uma vez só e normaliza
var downloadTrialUrl = (Environment.GetEnvironmentVariable("DOWNLOAD_TRIAL_URL")
    ?? "https://github.com/PlannusEngenharia/ProjetoDePlanejamento.LicensingServer/releases/download/v1.0.0/PlannusSetup-1.0.0.exe")
    .Trim();

app.MapMethods("/download/demo", new[] { "GET", "HEAD" }, async (HttpRequest req, HttpContext ctx, ILicenseRepo repo) =>
{
    try
    {
        // log no Postgres (não pode derrubar a rota)
        try
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = req.Headers["User-Agent"].ToString();
            var referer = req.Headers["Referer"].ToString();
            await repo.LogDownloadAsync(ip, ua, string.IsNullOrWhiteSpace(referer) ? null : referer);
        }
        catch { /* ignore */ }

        // valida a URL vinda da env
        if (string.IsNullOrWhiteSpace(downloadTrialUrl) ||
            !Uri.TryCreate(downloadTrialUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return Results.Problem("DOWNLOAD_TRIAL_URL ausente ou inválida (configure a env no Railway).", statusCode: 500);
        }

        // se mobile e GET, mostra página informativa
        var uaLower = req.Headers["User-Agent"].ToString().ToLowerInvariant();
        bool isMobile = uaLower.Contains("iphone") || uaLower.Contains("ipad") || uaLower.Contains("android");

        if (isMobile && string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var html = $"""
            <!doctype html><meta charset="utf-8">
            <title>Baixe no computador</title>
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial;max-width:680px;margin:48px auto;padding:24px;border:1px solid #e5e7eb;border-radius:12px;">
              <h2>Baixe no seu computador Windows</h2>
              <p>Este instalador (.exe) só funciona no Windows.<br>
              Envie este link para seu e-mail ou abra no computador para baixar.</p>
              <p style="margin-top:16px"><a href="{downloadTrialUrl}" style="display:inline-block;padding:12px 16px;border-radius:8px;background:#2563eb;color:#fff;text-decoration:none;">Baixar instalador</a></p>
            </div>
            """;
            return Results.Content(html, "text/html; charset=utf-8");
        }

        // redirect normal
        return Results.Redirect(downloadTrialUrl, permanent: false);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Falha ao processar o download: {ex.Message}", statusCode: 500);
    }
});



// === VALIDATE ===
app.MapPost("/api/validate", async (ValidateRequest req, ILicenseRepo repo) =>
{
    // ===== Fluxo 1: LICENÇA (se vier licenseKey) =====
    if (!string.IsNullOrWhiteSpace(req.LicenseKey))
    {
        // <<< AQUI é o ponto importante: usamos o método com checagem de fingerprint >>>
        var lic = await repo.GetLicenseWithFingerprintCheckAsync(
            req.LicenseKey!,
            req.Fingerprint
        );

        if (lic is null)
        {
            // licença existe mas já está amarrada em outro computador
            return Results.Ok(new
            {
                ok = false,
                reason = "license_in_use_in_other_computer"
            });
        }

        var ok = lic.Payload.ExpiresAtUtc > DateTime.UtcNow
                 && !string.Equals(
                        lic.Payload.SubscriptionStatus,
                        "canceled",
                        StringComparison.OrdinalIgnoreCase);

        // registra o 'ping' desta máquina para auditoria/telemetria
        if (!string.IsNullOrWhiteSpace(req.Fingerprint))
        {
            try
            {
                await repo.RecordActivationAsync(
                    req.LicenseKey!,
                    req.Fingerprint!,
                    lic.Payload.SubscriptionStatus ?? "active"
                );
            }
            catch { /* silencioso */ }
        }

        return Results.Ok(new
        {
            ok,
            subscriptionStatus = lic.Payload.SubscriptionStatus,
            expiresAtUtc = lic.Payload.ExpiresAtUtc,
            email = lic.Payload.Email,
            fingerprint = lic.Payload.Fingerprint
        });
    }

    // ===== Fluxo 2: TRIAL (sem licenseKey, exige fingerprint) =====
    if (string.IsNullOrWhiteSpace(req.Fingerprint))
        return Results.BadRequest(new { error = "missing fingerprint for trial validation" });

    // inicia (uma única vez) ou retorna o trial existente
    var trial = await repo.GetOrStartTrialAsync(
        req.Fingerprint!,
        req.Email,
        InMemoryRepo.TrialDays);

    // assina o payload para o cliente validar localmente
    trial.SignatureBase64 = SignPayload(privateKeyPem, trial.Payload, SigJson);

    var trialOk = trial.Payload.ExpiresAtUtc > DateTime.UtcNow;
    return Results.Ok(new
    {
        ok = trialOk,
        subscriptionStatus = trial.Payload.SubscriptionStatus, // "trial"
        expiresAtUtc = trial.Payload.ExpiresAtUtc,
        email = trial.Payload.Email,
        fingerprint = trial.Payload.Fingerprint,

        // limites sugeridos para o cliente respeitar
        features = new[] { "rows:max:30", "print:off" }
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


// ======================================================================
//  DECLARAÇÕES (TIPOS E HELPERS) – APENAS DEPOIS DO app.Run()
// ======================================================================

public sealed class CheckRequest
{
    public string? LicenseKey { get; set; }
    public string? Fingerprint { get; set; }
    public string? Client { get; set; }
}

// === Helpers ===

static async Task EnsureLicenseForEmailAsync(ILicenseRepo repo, string email, TimeSpan renewDelta)
{
    // cria ou prolonga licença vinculada ao e-mail
    await repo.ProlongByEmailAsync(email, renewDelta);
}

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
        SslMode = Npgsql.SslMode.Require,
        Pooling = true,
        MaxPoolSize = 20
        // TrustServerCertificate = true // removido conforme seu comentário
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
