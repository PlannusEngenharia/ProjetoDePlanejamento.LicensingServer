using System.Text.Json;
using Npgsql;
using ProjetoDePlanejamento.LicensingServer.Contracts;

namespace ProjetoDePlanejamento.LicensingServer.Data;

public sealed class PgRepo : ILicenseRepo
{
    private readonly string _cs;
    public PgRepo(string cs) => _cs = cs;

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

    // ===============================
    // READ BY KEY
    // ===============================
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
        var email = rd.GetString(1);
        var status = rd.GetString(2);
        var expiresAt = rd.GetDateTime(3);

        return ToSigned(id, email, status, expiresAt, null);
    }

    // ===============================
    // ISSUE / RENEW LICENSE
    // ===============================
    public async Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            const string upLic = @"
                insert into public.licenses (created_at, updated_at, email, license_key, status, expires_at)
                values (CURRENT_DATE, CURRENT_DATE, coalesce(@e, 'cliente@desconhecido'), @k, 'active', CURRENT_DATE + 30)
                on conflict (license_key) do update
                  set updated_at = CURRENT_DATE,
                      email      = coalesce(@e, public.licenses.email),
                      status     = 'active',
                      expires_at = case
                                     when public.licenses.expires_at < CURRENT_DATE + 30
                                       then CURRENT_DATE + 30
                                     else public.licenses.expires_at
                                   end
                returning id, email, status, expires_at;";

            int licId;
            string retEmail, retStatus;
            DateTime retExpires;

            await using (var cmd = new NpgsqlCommand(upLic, conn, tx))
            {
                cmd.Parameters.AddWithValue("@k", licenseKey);
                cmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);

                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                    throw new InvalidOperationException("UPSERT em licenses não retornou linha.");

                licId = rd.GetInt32(0);
                retEmail = rd.GetString(1);
                retStatus = rd.GetString(2);
                retExpires = rd.GetDateTime(3);
                await rd.CloseAsync();
            }

            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                const string upAct = @"
                    insert into public.activations
                        (license_id, fingerprint, machine_name, client_version, ip, first_seen_at, last_seen_at, status)
                    values
                        (@lid, @fp, null, null, null, CURRENT_DATE, CURRENT_DATE, 'active')
                    on conflict (license_id, fingerprint) do update
                      set last_seen_at = CURRENT_DATE,
                          status = 'active';";

                await using var cmd2 = new NpgsqlCommand(upAct, conn, tx);
                cmd2.Parameters.AddWithValue("@lid", licId);
                cmd2.Parameters.AddWithValue("@fp", fingerprint!);
                await cmd2.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return ToSigned(licId, retEmail, retStatus, retExpires, fingerprint);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine($"[PgRepo.IssueOrRenewAsync] {ex}");
            throw;
        }
    }

    // ===============================
    // GET OR START TRIAL
    // ===============================
   // Data/PgRepo.cs
public async Task<LicenseResponse?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
{
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();

    // 1) Verifica se a licença existe e está ativa (ou trial/válida, conforme sua regra)
    const string checkSql = @"
        select status, coalesce(expires_at, now()) as expires_at
          from licenses
         where license_key = @k
         limit 1;
    ";
    string? status = null;
    DateTime expiresAt = DateTime.UtcNow;

    await using (var checkCmd = new NpgsqlCommand(checkSql, con))
    {
        checkCmd.Parameters.AddWithValue("@k", licenseKey);
        await using var rd = await checkCmd.ExecuteReaderAsync();
        if (await rd.ReadAsync())
        {
            status = rd.IsDBNull(0) ? null : rd.GetString(0);
            expiresAt = rd.GetDateTime(1);
        }
    }

    // 2) Se NÃO existir a licença -> NÃO cria! Sinaliza inválida.
    if (status is null)
        return null;

    // 3) Se existir mas estiver cancelada/expirada segundo sua regra de negócio, bloqueie:
    if (string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase))
        return null;

    // 4) Aqui sim renova/atualiza (regra: +30 dias)
    var newExpires = DateTime.UtcNow.AddDays(30);
    const string upSql = @"
        update licenses
           set email = coalesce(@e, email),
               status = 'active',
               expires_at = @x,
               updated_at = now()
         where license_key = @k;
    ";
    await using (var upCmd = new NpgsqlCommand(upSql, con))
    {
        upCmd.Parameters.AddWithValue("@k", licenseKey);
        upCmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);
        upCmd.Parameters.AddWithValue("@x", newExpires);
        await upCmd.ExecuteNonQueryAsync();
    }

    // 5) Registra ativação (opcional: ignore conflitos)
    const string actSql = @"
        insert into activations (license_id, fingerprint, first_seen_at, last_seen_at, status)
        values (@k, @f, now(), now(), 'active')
        on conflict do nothing;
    ";
    await using (var aCmd = new NpgsqlCommand(actSql, con))
    {
        aCmd.Parameters.AddWithValue("@k", licenseKey);
        aCmd.Parameters.AddWithValue("@f", (object?)fingerprint ?? DBNull.Value);
        await aCmd.ExecuteNonQueryAsync();
    }

    return new LicenseResponse
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

    // ===============================
    // CANCEL
    // ===============================
    public async Task DeactivateAsync(string licenseKey)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            update public.licenses
               set updated_at = CURRENT_DATE,
                   status = 'canceled'
             where license_key = @k;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@k", licenseKey);
        await cmd.ExecuteNonQueryAsync();
    }

    // ===============================
    // PROLONG BY EMAIL (webhook)
    // ===============================
    public async Task ProlongByEmailAsync(string email, TimeSpan delta)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            update public.licenses
               set updated_at = CURRENT_DATE,
                   expires_at = expires_at + @d
             where lower(email) = lower(@e);";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@e", email);
        cmd.Parameters.AddWithValue("@d", delta.Days);
        await cmd.ExecuteNonQueryAsync();
    }

    // ===============================
    // DOWNLOAD LOG
    // ===============================
    public async Task LogDownloadAsync(string? ip, string? ua, string? referer)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            insert into public.downloads (ts, ip, ua, source, referer)
            values (CURRENT_DATE, @ip, @ua, 'web', @ref);";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ua", (object?)ua ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ref", (object?)referer ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ===============================
    // WEBHOOK LOG
    // ===============================
    public async Task LogWebhookAsync(string? hottok, string? eventKey, int httpStatus, JsonDocument payload)
    {
        await Task.Yield();
        Console.Error.WriteLine($"[Webhook] hottok? {(string.IsNullOrEmpty(hottok) ? "no" : "yes")} event={eventKey} status={httpStatus} size={payload.RootElement.GetRawText().Length}");
    }
}






