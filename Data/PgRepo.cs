using System.Text.Json;
using Npgsql;
using ProjetoDePlanejamento.LicensingServer.Contracts;

namespace ProjetoDePlanejamento.LicensingServer.Data;

public sealed class PgRepo : ILicenseRepo
{
    private readonly string _cs;
    public PgRepo(string cs) => _cs = cs;

    // ======================================================
    // Helper para converter payload SQL -> objeto de licença
    // ======================================================
    private static SignedLicense ToSigned(int id, string email, string status, DateTime expiresAtUtc, string? fingerprint)
        => new SignedLicense
        {
            SignatureBase64 = null,
            Payload = new LicensePayload
            {
                Email = email,
                SubscriptionStatus = status,
                ExpiresAtUtc = expiresAtUtc,
                Fingerprint = fingerprint
            }
        };

    // ======================================================
    // TRY GET BY KEY
    // ======================================================
    public async Task<SignedLicense?> TryGetByKeyAsync(string licenseKey)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            select id, email, status, expires_at
              from public.licenses
             where license_key = @k
             limit 1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@k", licenseKey);

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        var id = rd.GetInt32(0);
        var email = rd.IsDBNull(1) ? "" : rd.GetString(1);
        var status = rd.IsDBNull(2) ? "inactive" : rd.GetString(2);
        var expiresAt = rd.IsDBNull(3) ? DateTime.UtcNow.AddDays(-1) : rd.GetDateTime(3);

        return ToSigned(id, email, status, expiresAt, null);
    }

  // ===============================
// ISSUE / RENEW LICENSE (corrigido)
// ===============================
public async Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
{
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();

    // 1️⃣ Verifica se a licença existe
    const string checkSql = @"
        select id, status, coalesce(expires_at, now()) as expires_at
          from public.licenses
         where license_key = @k
         limit 1;";
    int? licId = null;
    string? status = null;
    DateTime expiresAt = DateTime.UtcNow;

    await using (var checkCmd = new NpgsqlCommand(checkSql, con))
    {
        checkCmd.Parameters.AddWithValue("@k", licenseKey);
        await using var rd = await checkCmd.ExecuteReaderAsync();
        if (await rd.ReadAsync())
        {
            licId = rd.GetInt32(0);
            status = rd.IsDBNull(1) ? null : rd.GetString(1);
            expiresAt = rd.GetDateTime(2);
        }
    }

    // 2️⃣ Se NÃO existir → não cria, retorna null
    if (licId is null)
        return null;

    // 3️⃣ Bloqueia canceladas
    if (string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase))
        return null;

    // 4️⃣ Renova por +30 dias
    var newExpires = DateTime.UtcNow.AddDays(30);
    const string upSql = @"
        update public.licenses
           set email = coalesce(@e, email),
               status = 'active',
               expires_at = @x,
               updated_at = now()
         where license_key = @k;";
    await using (var upCmd = new NpgsqlCommand(upSql, con))
    {
        upCmd.Parameters.AddWithValue("@k", licenseKey);
        upCmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);
        upCmd.Parameters.AddWithValue("@x", newExpires);
        await upCmd.ExecuteNonQueryAsync();
    }

    // 5️⃣ Registra ativação
    const string actSql = @"
        insert into public.activations (license_id, fingerprint, first_seen_at, last_seen_at, status)
        values (@lid, @f, now(), now(), 'active')
        on conflict (license_id, fingerprint) do update
          set last_seen_at = now(),
              status = 'active';";
    await using (var aCmd = new NpgsqlCommand(actSql, con))
    {
        aCmd.Parameters.AddWithValue("@lid", licId);
        aCmd.Parameters.AddWithValue("@f", (object?)fingerprint ?? DBNull.Value);
        await aCmd.ExecuteNonQueryAsync();
    }

    // ✅ Retorna SignedLicense, conforme interface
    return new SignedLicense
    {
        Payload = new LicensePayload
        {
            LicenseId = licenseKey,
            Email = email ?? "",
            Fingerprint = fingerprint ?? "",
            ExpiresAtUtc = newExpires,
            SubscriptionStatus = "active"
        }
    };
}

    // ======================================================
    // GET OR START TRIAL
    // ======================================================
    public async Task<SignedLicense> GetOrStartTrialAsync(string fingerprint, string? email, int trialDays)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var trialKey = $"TRIAL-{fingerprint}";

            const string upLic = @"
                insert into public.licenses (created_at, updated_at, email, license_key, status, expires_at)
                values (now(), now(), coalesce(@e,'trial@user'), @k, 'trial', now() + (@dias || ' days')::interval)
                on conflict (license_key) do update
                  set updated_at = now(),
                      status     = 'trial',
                      expires_at = greatest(public.licenses.expires_at, now() + (@dias || ' days')::interval)
                returning id, email, status, expires_at;";

            int licId; string retEmail, retStatus; DateTime retExpires;
            await using (var cmd = new NpgsqlCommand(upLic, conn, tx))
            {
                cmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@k", trialKey);
                cmd.Parameters.AddWithValue("@dias", trialDays);

                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                    throw new InvalidOperationException("UPSERT trial não retornou linha.");

                licId = rd.GetInt32(0);
                retEmail = rd.GetString(1);
                retStatus = rd.GetString(2);
                retExpires = rd.GetDateTime(3);
            }

            const string upAct = @"
                insert into public.activations
                    (license_id, fingerprint, first_seen_at, last_seen_at, status)
                values
                    (@lid, @fp, now(), now(), 'active')
                on conflict (license_id, fingerprint) do update
                  set last_seen_at = now(),
                      status = 'active';";
            await using (var cmd2 = new NpgsqlCommand(upAct, conn, tx))
            {
                cmd2.Parameters.AddWithValue("@lid", licId);
                cmd2.Parameters.AddWithValue("@fp", fingerprint);
                await cmd2.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return ToSigned(licId, retEmail, retStatus, retExpires, fingerprint);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine($"[PgRepo.GetOrStartTrialAsync] {ex}");
            throw;
        }
    }

    // ======================================================
    // DEACTIVATE
    // ======================================================
    public async Task DeactivateAsync(string licenseKey)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            update public.licenses
               set updated_at = now(),
                   status = 'canceled'
             where license_key = @k;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@k", licenseKey);
        await cmd.ExecuteNonQueryAsync();
    }

    // ======================================================
    // PROLONG BY EMAIL (webhook)
    // ======================================================
    public async Task ProlongByEmailAsync(string email, TimeSpan delta)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            update public.licenses
               set updated_at = now(),
                   expires_at = expires_at + (@d || ' days')::interval
             where lower(email) = lower(@e);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@e", email);
        cmd.Parameters.AddWithValue("@d", delta.Days);
        await cmd.ExecuteNonQueryAsync();
    }

    // ======================================================
    // DOWNLOAD LOG
    // ======================================================
    public async Task LogDownloadAsync(string? ip, string? ua, string? referer)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            insert into public.downloads (ts, ip, ua, source, referer)
            values (now(), @ip, @ua, 'web', @ref);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ua", (object?)ua ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ref", (object?)referer ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ======================================================
    // WEBHOOK LOG
    // ======================================================
    public async Task LogWebhookAsync(string? hottok, string? eventKey, int httpStatus, JsonDocument payload)
    {
        await Task.Yield();
        Console.Error.WriteLine($"[Webhook] token={(string.IsNullOrEmpty(hottok) ? "none" : "ok")} event={eventKey} status={httpStatus} payload={payload.RootElement.GetRawText().Length}B");
    }
}
