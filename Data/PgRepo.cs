using Npgsql;
using ProjetoDePlanejamento.LicensingServer.Contracts;

namespace ProjetoDePlanejamento.LicensingServer.Data;

public sealed class PgRepo : ILicenseRepo
{
    private readonly string _cs;
    public PgRepo(string cs) => _cs = cs;

    private static LicenseResponse MapLicenseRow(int id, string email, string status, DateTime expiresAtUtc, string? fp) =>
        new LicenseResponse
        {
            Payload = new LicensePayload
            {
                Email = email,
                SubscriptionStatus = status,
                ExpiresAtUtc = expiresAtUtc,
                Fingerprint = fp
            }
        };

    public async Task<LicenseResponse?> TryGetByKeyAsync(string licenseKey)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            select id, email, status, expires_at
            from public.licenses
            where license_key = @k
            limit 1;";

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@k", licenseKey);
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            var id        = rd.GetInt32(0);
            var email     = rd.GetString(1);
            var status    = rd.GetString(2);
            var expiresAt = rd.GetDateTime(3);

            await rd.CloseAsync();

            return MapLicenseRow(id, email, status, expiresAt, null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PgRepo.TryGetByKeyAsync] {ex}");
            throw;
        }
    }

    public async Task<LicenseResponse?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            // 1) UPSERT em licenses
            const string upLic = @"
                insert into public.licenses (created_at, updated_at, email, license_key, status, expires_at)
                values (now()::date, now()::date, coalesce(@e, 'cliente@desconhecido'), @k, 'active', now() + interval '30 days')
                on conflict (license_key) do update
                  set updated_at = excluded.updated_at,
                      email      = coalesce(@e, public.licenses.email),
                      status     = 'active',
                      expires_at = case
                                     when public.licenses.expires_at < now() + interval '30 days'
                                       then now() + interval '30 days'
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

                licId     = rd.GetInt32(0);
                retEmail  = rd.GetString(1);
                retStatus = rd.GetString(2);
                retExpires= rd.GetDateTime(3);

                // ✅ FECHA O READER ANTES DA PRÓXIMA QUERY
                await rd.CloseAsync();
            }

            // 2) UPSERT em activations (se tiver fingerprint)
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                const string upAct = @"
                    insert into public.activations
                        (license_id, fingerprint, machine_name, client_version, ip, first_seen_at, last_seen_at, status)
                    values
                        (@lid, @fp, null, null, null, now()::date, now()::date, 'active')
                    on conflict (license_id, fingerprint) do update
                      set last_seen_at = now()::date,
                          status      = 'active';";

                await using var cmd2 = new NpgsqlCommand(upAct, conn, tx);
                cmd2.Parameters.AddWithValue("@lid", licId);
                cmd2.Parameters.AddWithValue("@fp", fingerprint!);
                await cmd2.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return MapLicenseRow(licId, retEmail, retStatus, retExpires, fingerprint);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine($"[PgRepo.IssueOrRenewAsync] {ex}");
            throw;
        }
    }

    public async Task ProlongByEmailAsync(string email, TimeSpan delta)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            update public.licenses
               set updated_at = now()::date,
                   expires_at = (expires_at + @d)
             where lower(email) = lower(@e);";

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@e", email);
            cmd.Parameters.AddWithValue("@d", delta);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PgRepo.ProlongByEmailAsync] {ex}");
            throw;
        }
    }

    public async Task<LicenseResponse> GetOrStartTrialAsync(string fingerprint, string? email, int trialDays)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            // 1) Cria licença TRIAL “sintética” vinculada ao fingerprint (1 por fp)
            const string upLic = @"
                insert into public.licenses (created_at, updated_at, email, license_key, status, expires_at)
                values (now()::date, now()::date, coalesce(@e,'trial@user'), @k, 'trial', now() + (@dias || ' days')::interval)
                on conflict (license_key) do update
                  set updated_at = now()::date,
                      status     = 'trial',
                      expires_at = greatest(public.licenses.expires_at, now() + (@dias || ' days')::interval)
                returning id, email, status, expires_at;";

            var trialKey = $"TRIAL-{fingerprint}";
            int licId;
            string retEmail, retStatus;
            DateTime retExpires;

            await using (var cmd = new NpgsqlCommand(upLic, conn, tx))
            {
                cmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@k", trialKey);
                cmd.Parameters.AddWithValue("@dias", trialDays);

                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                    throw new InvalidOperationException("UPSERT trial não retornou linha.");

                licId     = rd.GetInt32(0);
                retEmail  = rd.GetString(1);
                retStatus = rd.GetString(2);
                retExpires= rd.GetDateTime(3);
                await rd.CloseAsync();
            }

            // 2) UPSERT activations
            const string upAct = @"
                insert into public.activations
                    (license_id, fingerprint, machine_name, client_version, ip, first_seen_at, last_seen_at, status)
                values
                    (@lid, @fp, null, null, null, now()::date, now()::date, 'active')
                on conflict (license_id, fingerprint) do update
                  set last_seen_at = now()::date,
                      status      = 'active';";

            await using (var cmd2 = new NpgsqlCommand(upAct, conn, tx))
            {
                cmd2.Parameters.AddWithValue("@lid", licId);
                cmd2.Parameters.AddWithValue("@fp", fingerprint);
                await cmd2.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return MapLicenseRow(licId, retEmail, retStatus, retExpires, fingerprint);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.Error.WriteLine($"[PgRepo.GetOrStartTrialAsync] {ex}");
            throw;
        }
    }

    public async Task DeactivateAsync(string licenseKey)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            update public.licenses
               set updated_at = now()::date,
                   status     = 'canceled'
             where license_key = @k;";

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@k", licenseKey);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PgRepo.DeactivateAsync] {ex}");
            throw;
        }
    }

    public async Task LogDownloadAsync(string ip, string ua, string? referer)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = @"
            insert into public.downloads (ts, ip, ua, source, referer)
            values (now()::date, @ip, @ua, 'web', @ref);";

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ip", ip);
            cmd.Parameters.AddWithValue("@ua", ua);
            cmd.Parameters.AddWithValue("@ref", (object?)referer ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PgRepo.LogDownloadAsync] {ex}");
            throw;
        }
    }
}




