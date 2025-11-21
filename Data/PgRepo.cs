using System.Text.Json;
using Npgsql;
using ProjetoDePlanejamento.LicensingServer.Contracts;

namespace ProjetoDePlanejamento.LicensingServer.Data;

public sealed class PgRepo : ILicenseRepo
{
    private readonly string _cs;
    public PgRepo(string cs) => _cs = cs;

    // ======================================================
    // Helper: row -> objeto de licença
    // ======================================================
    private static SignedLicense ToSigned(int id, string email, string status, DateTime expiresAtUtc, string? fingerprint)
        => new SignedLicense
        {
            // evita warning/nullable; se sua propriedade for nullable, pode deixar null
            SignatureBase64 = string.Empty,
            Payload = new LicensePayload
            {
                Email = email,
                SubscriptionStatus = status,
                ExpiresAtUtc = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc),
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

        var id        = rd.GetInt32(0);
        var email     = rd.IsDBNull(1) ? "" : rd.GetString(1);
        var status    = rd.IsDBNull(2) ? "inactive" : rd.GetString(2);
        var expiresAt = rd.IsDBNull(3) ? DateTime.UtcNow.AddDays(-1) : rd.GetDateTime(3);

        return ToSigned(id, email, status, expiresAt, null);
    }

 // ===============================
    // Novo ajuste de computado fingerprint
    // ===============================

public async Task<SignedLicense?> GetLicenseWithFingerprintCheckAsync(string licenseKey, string? fingerprint)
{
    await using var conn = new NpgsqlConnection(_cs);
    await conn.OpenAsync();

    // 1) Busca a licença
    const string licSql = @"
        select id, email, status, expires_at
          from public.licenses
         where license_key = @k
         limit 1;";
    int? licId = null;
    string? email = null;
    string? status = null;
    DateTime expiresAt = DateTime.UtcNow.AddDays(-1);

    await using (var cmd = new NpgsqlCommand(licSql, conn))
    {
        cmd.Parameters.AddWithValue("@k", licenseKey);
        await using var rd = await cmd.ExecuteReaderAsync();
        if (await rd.ReadAsync())
        {
            licId    = rd.GetInt32(0);
            email    = rd.IsDBNull(1) ? "" : rd.GetString(1);
            status   = rd.IsDBNull(2) ? "" : rd.GetString(2);
            expiresAt= rd.GetDateTime(3);
        }
    }

    if (licId is null || string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase))
        return null;

    // 2) Busca fingerprint já registrado
    const string fpSql = @"
        select fingerprint
          from public.activations
         where license_id = @lid
           and status = 'active'
         limit 1;";
    string? existingFp = null;

    await using (var fpCmd = new NpgsqlCommand(fpSql, conn))
    {
        fpCmd.Parameters.AddWithValue("@lid", licId.Value);
        existingFp = await fpCmd.ExecuteScalarAsync() as string;
    }

    // 3) Se existe fingerprint diferente → BLOQUEIA
    if (!string.IsNullOrWhiteSpace(existingFp) &&
        !string.Equals(existingFp, fingerprint, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    // OK → retorna licença válida
    return new SignedLicense
    {
        Payload = new LicensePayload
        {
            LicenseId          = licenseKey,
            Email              = email ?? "",
            SubscriptionStatus = status!,
            ExpiresAtUtc       = DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc),
            Fingerprint        = fingerprint ?? ""
        }
    };
}



    // ===============================
    // ISSUE / RENEW LICENSE (final)
    // ===============================
    public async Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
    {
        await using var con = new NpgsqlConnection(_cs);
        await con.OpenAsync();
        await using var tx = await con.BeginTransactionAsync();

        try
        {
            // 1) Localiza licença existente
            const string checkSql = @"
                select id, status, expires_at
                  from public.licenses
                 where license_key = @k
                 limit 1;";
            int? licId = null;
            string? status = null;
            DateTime expiresAt = DateTime.UtcNow.Date;

            await using (var check = new NpgsqlCommand(checkSql, con, tx))
            {
                check.Parameters.AddWithValue("@k", licenseKey);
                await using var rd = await check.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    licId    = rd.GetInt32(0);
                    status   = rd.IsDBNull(1) ? null : rd.GetString(1);
                    expiresAt= rd.GetDateTime(2);
                }
            }

            // 2) Se não existir ou estiver cancelada → rejeita
            if (licId is null || string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync();
                return null;
            }

            // 3) Bloqueia se tentar ativar em outro PC (caso já exista ativação ativa)
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                const string chkSql = @"
                    select fingerprint
                      from public.activations
                     where license_id = @lid and status = 'active'
                     limit 1;";
                await using (var chk = new NpgsqlCommand(chkSql, con, tx))
                {
                    chk.Parameters.AddWithValue("@lid", licId!.Value);
                    var existing = await chk.ExecuteScalarAsync() as string;
                    if (existing != null && existing != fingerprint)
                    {
                        await tx.RollbackAsync();
                        Console.Error.WriteLine($"[BLOCK] Licença {licenseKey} já está ativa em outro PC.");
                        return null;
                    }
                }
            }

            // 4) Atualiza validade (+30d) e, se informado, mantém o e-mail atual (só sobrescreve se @e != null)
            const string upSql = @"
                update public.licenses
                   set email      = coalesce(@e, email),
                       status     = 'active',
                       expires_at = CURRENT_DATE + 30,
                       updated_at = CURRENT_DATE
                 where id = @id
                 returning id, email, status, expires_at;";
            await using (var up = new NpgsqlCommand(upSql, con, tx))
            {
                up.Parameters.AddWithValue("@id", licId.Value);
                up.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);

                await using var rd = await up.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                    throw new InvalidOperationException("UPDATE em licenses não retornou linha.");

                licId    = rd.GetInt32(0);
                email    = rd.IsDBNull(1) ? email : rd.GetString(1);
                status   = rd.GetString(2);
                expiresAt= rd.GetDateTime(3);
            }

            // 5) Upsert da ativação (se tiver fingerprint)
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                const string actUpsert = @"
                    insert into public.activations
                        (license_id, fingerprint, first_seen_at, last_seen_at, status)
                    values
                        (@lid, @fp, now(), now(), 'active')
                    on conflict (license_id, fingerprint) do update
                      set last_seen_at = now(),
                          status = 'active';";
                await using var aCmd = new NpgsqlCommand(actUpsert, con, tx);
                aCmd.Parameters.AddWithValue("@lid", licId!.Value);
                aCmd.Parameters.AddWithValue("@fp", fingerprint!);
                await aCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            var expiresAtUtc = DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc);
            return new SignedLicense
            {
                Payload = new LicensePayload
                {
                    LicenseId          = licenseKey,
                    Email              = email ?? "",
                    Fingerprint        = fingerprint ?? "",
                    ExpiresAtUtc       = expiresAtUtc,
                    SubscriptionStatus = "active"
                }
            };
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { /* ignore */ }
            Console.Error.WriteLine($"[IssueOrRenewAsync] {ex}");
            throw;
        }
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
            values (
                now(),
                now(),
                coalesce(@e,'trial@user'),
                @k,
                'trial',
                now() + (@dias || ' days')::interval
            )
            on conflict (license_key) do update
              set updated_at = now(),
                  status     = 'trial'
              -- ATENÇÃO: NÃO ALTERA expires_at AQUI!
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

            licId      = rd.GetInt32(0);
            retEmail   = rd.GetString(1);
            retStatus  = rd.GetString(2);
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
        try { await tx.RollbackAsync(); } catch { /* ignore */ }
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

        var newKey = $"PLN-{Guid.NewGuid():N}".ToUpperInvariant();
        var days   = delta.Days; // pode ser negativo (cancelamento)

        const string sql = @"
with upd as (
    update public.licenses
       set updated_at = now(),
           expires_at = expires_at + (@d || ' days')::interval,
           status     = case when @d >= 0 then 'active' else 'canceled' end
     where lower(email) = lower(@e)
     returning id
)
insert into public.licenses (email, license_key, status, created_at, updated_at, expires_at)
select @e, @k, 'active', now(), now(), now() + (@d || ' days')::interval
where not exists (select 1 from upd);";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@e", email);
        cmd.Parameters.AddWithValue("@d", days);
        cmd.Parameters.AddWithValue("@k", newKey);
        await cmd.ExecuteNonQueryAsync();
    }

    // ======================================================
    // RECORD ACTIVATION (necessário pelo Program.cs)
    // ======================================================
   // ======================================================
// RECORD ACTIVATION (necessário pelo Program.cs)
// ======================================================
public async Task RecordActivationAsync(string licenseKey, string fingerprint, string status)
{
    if (string.IsNullOrWhiteSpace(licenseKey) || string.IsNullOrWhiteSpace(fingerprint))
        return;

    await using var conn = new NpgsqlConnection(_cs);
    await conn.OpenAsync();

    // 1) Verifica se já existe UM fingerprint ativo para essa licença
    const string checkSql = @"
        select a.fingerprint
          from public.activations a
          join public.licenses  l on l.id = a.license_id
         where l.license_key = @k
           and a.status      = 'active'
         limit 1;";

    string? existingFp = null;
    await using (var chk = new NpgsqlCommand(checkSql, conn))
    {
        chk.Parameters.AddWithValue("@k", licenseKey);
        var res = await chk.ExecuteScalarAsync();
        if (res != null && res != DBNull.Value)
            existingFp = res.ToString();
    }

    // 2) Se já existe fingerprint diferente → NÃO grava nada (mantém amarrado no PC original)
    if (!string.IsNullOrWhiteSpace(existingFp) &&
        !string.Equals(existingFp, fingerprint, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"[RecordActivation] BLOQUEADO: license {licenseKey} já está vinculada ao FP={existingFp}, recusa novo FP={fingerprint}.");
        return;
    }

    // 3) Se não existe (primeira vez) ou é o mesmo FP, faz o upsert normal
    const string upsertSql = @"
    with lic as (
      select id
        from public.licenses
       where license_key = @k
       limit 1
    )
    insert into public.activations(license_id, fingerprint, first_seen_at, last_seen_at, status)
    select lic.id, @fp, now(), now(), @st
      from lic
    on conflict (license_id, fingerprint) do update
      set last_seen_at = now(),
          status       = @st;";

    await using var cmd = new NpgsqlCommand(upsertSql, conn);
    cmd.Parameters.AddWithValue("@k",  licenseKey);
    cmd.Parameters.AddWithValue("@fp", fingerprint);
    cmd.Parameters.AddWithValue("@st", string.IsNullOrWhiteSpace(status) ? "active" : status);
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
    // WEBHOOK LOG (stdout)
    // ======================================================
    public async Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw)
{
    await Task.Yield();
    Console.Error.WriteLine(
        $"[Webhook] evt={evt ?? "-"} email={email ?? "-"} appliedDays={appliedDays} payload={raw.RootElement.GetRawText().Length}B");
}

}

