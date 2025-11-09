using Npgsql;
using System.Text.Json;

public sealed class PgRepo : ILicenseRepo
{
    private readonly string _cs;
    public PgRepo(string cs) => _cs = cs;

    // --- registra download ---
    public async Task LogDownloadAsync(string? ip, string? ua, string? referer)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            var sql = @"insert into downloads(ts, ip, ua, source, referer)
                        values (now(), @ip, @ua, 'api', @rf);";
            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ua", (object?)ua ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rf", (object?)referer ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }


    // --- cria ou renova licença ---
    public async Task<LicenseResponse?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
        {
            var expires = DateTime.UtcNow.AddDays(30);

            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            var upsert = @"
insert into licenses (license_key, email, status, expires_at, created_at, updated_at)
values (@k,@e,'active',@x,now(),now())
on conflict (license_key) do update
   set email=@e, status='active', expires_at=@x, updated_at=now();";
            await using (var cmd = new NpgsqlCommand(upsert, con))
            {
                cmd.Parameters.AddWithValue("@k", licenseKey);
                cmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@x", expires);
                await cmd.ExecuteNonQueryAsync();
            }

            var insAct = @"insert into activations (license_id,fingerprint,first_seen_at,last_seen_at,status)
                           values (@k,@f,now(),now(),'active');";
            await using (var cmd = new NpgsqlCommand(insAct, con))
            {
                cmd.Parameters.AddWithValue("@k", licenseKey);
                cmd.Parameters.AddWithValue("@f", (object?)fingerprint ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            return new LicenseResponse
            {
                Payload = new LicensePayload
                {
                    LicenseId = licenseKey,
                    Email = email ?? "",
                    Fingerprint = fingerprint ?? "",
                    ExpiresAtUtc = expires,
                    SubscriptionStatus = "active"
                }
            };
        }

    // --- prolonga via webhook ---
    public async Task ProlongByEmailAsync(string email, TimeSpan delta)
    {
        await using var con = new NpgsqlConnection(_cs);
        await con.OpenAsync();

        var sql = @"update licenses
                       set expires_at = coalesce(expires_at, now()) + @d, updated_at=now()
                     where email=@e;";
        await using var cmd = new NpgsqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@e", email);
        cmd.Parameters.AddWithValue("@d", delta);
        await cmd.ExecuteNonQueryAsync();
    }

    // --- placeholder para logs de webhook ---
    public async Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw)
{
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();

    var sql = @"insert into downloads(ts, ip, ua, source, referer)
                values (now(), 'webhook', @evt, 'hotmart', @em);";
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("@evt", (object?)evt ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@em", (object?)email ?? DBNull.Value);
    await cmd.ExecuteNonQueryAsync();
}
public async Task<LicenseResponse?> TryGetByKeyAsync(string licenseKey)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            var sql = @"select email, status, expires_at
                        from licenses
                        where license_key=@k
                        limit 1;";
            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@k", licenseKey);

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            var email   = rd.IsDBNull(0) ? "" : rd.GetString(0);
            var status  = rd.IsDBNull(1) ? "inactive" : rd.GetString(1);
            var expires = rd.IsDBNull(2) ? DateTime.UtcNow.AddDays(-1) : rd.GetDateTime(2);

            return new LicenseResponse
            {
                Payload = new LicensePayload
                {
                    LicenseId = licenseKey,
                    Email = email,
                    Fingerprint = "", // opcional aqui
                    ExpiresAtUtc = expires,
                    SubscriptionStatus = status
                }
            };
        }

        // Trial por fingerprint (cria se não existir)
        public async Task<LicenseResponse> GetOrStartTrialAsync(string fingerprint, string? email, int trialDays)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            // Verifica se já existe ativação 'trial' para essa máquina
            var find = @"select min(first_seen_at) as started
                         from activations
                         where fingerprint=@f and status='trial';";
            DateTime? started = null;
            await using (var cmd = new NpgsqlCommand(find, con))
            {
                cmd.Parameters.AddWithValue("@f", fingerprint);
                var o = await cmd.ExecuteScalarAsync();
                if (o != null && o != DBNull.Value) started = (DateTime)o;
            }

            if (started is null)
            {
                // cria um registro de trial
                var ins = @"insert into activations(license_id,fingerprint,machine_name,client_version,ip,first_seen_at,last_seen_at,status)
                            values ('TRIAL', @f, null, null, null, now(), now(), 'trial');";
                await using var cmd = new NpgsqlCommand(ins, con);
                cmd.Parameters.AddWithValue("@f", fingerprint);
                await cmd.ExecuteNonQueryAsync();
                started = DateTime.UtcNow;
            }
            else
            {
                // atualiza "last_seen_at"
                var upd = @"update activations set last_seen_at=now()
                            where fingerprint=@f and status='trial';";
                await using var cmd = new NpgsqlCommand(upd, con);
                cmd.Parameters.AddWithValue("@f", fingerprint);
                await cmd.ExecuteNonQueryAsync();
            }

            var expires = started.Value.AddDays(trialDays);

            return new LicenseResponse
            {
                Payload = new LicensePayload
                {
                    LicenseId = "TRIAL",
                    Email = email ?? "",
                    Fingerprint = fingerprint,
                    ExpiresAtUtc = expires,
                    SubscriptionStatus = "trial"
                }
            };
        }

        // Cancelar licença
        public async Task DeactivateAsync(string licenseKey)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();
            var sql = @"update licenses
                        set status='canceled', updated_at=now()
                        where license_key=@k;";
            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@k", licenseKey);
            await cmd.ExecuteNonQueryAsync();
        }

        // Hotmart: prolongamento
        public async Task ProlongByEmailAsync(string email, TimeSpan delta)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            var sql = @"update licenses
                           set expires_at = coalesce(expires_at, now()) + @d, updated_at=now()
                         where email=@e;";
            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@e", email);
            cmd.Parameters.AddWithValue("@d", delta);
            await cmd.ExecuteNonQueryAsync();
        }

        // Log bruto do webhook (aqui reutilizo downloads como “log leve”)
        public async Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();
            var sql = @"insert into downloads(ts, ip, ua, source, referer)
                        values (now(), 'webhook', @evt, 'hotmart', @em);";
            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@evt", (object?)evt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@em", (object?)email ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    

}
