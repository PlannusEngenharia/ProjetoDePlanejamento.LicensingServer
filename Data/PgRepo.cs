using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using ProjetoDePlanejamento.LicensingServer.Contracts;

namespace ProjetoDePlanejamento.LicensingServer.Data
{
    public sealed class PgRepo : ILicenseRepo
    {
        private readonly string _cs;
        public PgRepo(string cs) => _cs = cs;

        // --- logs de download ---
        public async Task LogDownloadAsync(string? ip, string? ua, string? referer)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            const string sql = @"
insert into downloads(ts, ip, ua, source, referer)
values (now(), @ip, @ua, 'api', @rf);";

            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ua", (object?)ua ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rf", (object?)referer ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // --- cria ou renova licença ---
        public async Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
        {
            var expires = DateTime.UtcNow.AddDays(30);

            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            // UPSERT em licenses + retorna o id sempre
            const string upsert = @"
insert into licenses (license_key, email, status, expires_at, created_at, updated_at)
values (@k, @e, 'active', @x, now(), now())
on conflict (license_key) do update
   set email = excluded.email,
       status = 'active',
       expires_at = @x,
       updated_at = now()
returning id, coalesce(email,''), coalesce(status,'active'), expires_at;";

            int licId;
            string retEmail, retStatus;
            DateTime retExpires;

            await using (var cmd = new NpgsqlCommand(upsert, con))
            {
                cmd.Parameters.AddWithValue("@k", licenseKey);
                cmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@x", expires);

               await using var rd = await cmd.ExecuteReaderAsync();
if (!await rd.ReadAsync()) return null;

licId     = rd.GetInt32(0);
retEmail  = rd.GetString(1);
retStatus = rd.GetString(2);
retExpires= rd.GetDateTime(3);

// ✅ fecha o reader antes de usar a conexão novamente
await rd.CloseAsync();

            }

            // UPSERT em activations: se já existir (license_id,fingerprint) atualiza last_seen_at/status
            const string upsertAct = @"
insert into activations (license_id, fingerprint, first_seen_at, last_seen_at, status)
values (@lid, @fp, now(), now(), 'active')
on conflict (license_id, fingerprint) do update
   set last_seen_at = now(),
       status      = 'active';";

            await using (var cmd = new NpgsqlCommand(upsertAct, con))
            {
                cmd.Parameters.AddWithValue("@lid", licId);
                cmd.Parameters.AddWithValue("@fp", (object?)fingerprint ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            // monta o objeto de retorno
            return new SignedLicense
            {
                Payload = new LicensePayload
                {
                    LicenseId          = licenseKey,
                    Email              = retEmail,
                    Fingerprint        = fingerprint ?? "",
                    ExpiresAtUtc       = retExpires,
                    SubscriptionStatus = retStatus
                }
            };
        }

        public async Task<SignedLicense?> TryGetByKeyAsync(string licenseKey)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            const string sql = @"
select coalesce(email,''), coalesce(status,'inactive'), coalesce(expires_at, now() - interval '1 day')
from licenses
where license_key=@k
limit 1;";

            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@k", licenseKey);

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            var email   = rd.GetString(0);
            var status  = rd.GetString(1);
            var expires = rd.GetDateTime(2);

            return new SignedLicense
            {
                Payload = new LicensePayload
                {
                    LicenseId          = licenseKey,
                    Email              = email,
                    Fingerprint        = "",
                    ExpiresAtUtc       = expires,
                    SubscriptionStatus = status
                }
            };
        }

        public async Task<SignedLicense> GetOrStartTrialAsync(string fingerprint, string? email, int trialDays)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            // Se já houver ativação trial para esse fingerprint, pega o primeiro visto
            DateTime? started = null;
            const string find = @"select min(first_seen_at) from activations where fingerprint=@f and status='trial';";
            await using (var cmd = new NpgsqlCommand(find, con))
            {
                cmd.Parameters.AddWithValue("@f", fingerprint);
                var o = await cmd.ExecuteScalarAsync();
                if (o != null && o != DBNull.Value) started = (DateTime)o;
            }

            if (started is null)
            {
                const string ins = @"
insert into activations(license_id,fingerprint,machine_name,client_version,ip,first_seen_at,last_seen_at,status)
values (0, @f, null, null, null, now(), now(), 'trial');";
                await using var cmd = new NpgsqlCommand(ins, con);
                cmd.Parameters.AddWithValue("@f", fingerprint);
                await cmd.ExecuteNonQueryAsync();
                started = DateTime.UtcNow;
            }
            else
            {
                const string upd = @"update activations set last_seen_at=now() where fingerprint=@f and status='trial';";
                await using var cmd = new NpgsqlCommand(upd, con);
                cmd.Parameters.AddWithValue("@f", fingerprint);
                await cmd.ExecuteNonQueryAsync();
            }

            var expires = started.Value.AddDays(trialDays);

            return new SignedLicense
            {
                Payload = new LicensePayload
                {
                    LicenseId          = "TRIAL",
                    Email              = email ?? "",
                    Fingerprint        = fingerprint,
                    ExpiresAtUtc       = expires,
                    SubscriptionStatus = "trial"
                }
            };
        }

        public async Task ProlongByEmailAsync(string email, TimeSpan delta)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            const string sql = @"
update licenses
   set expires_at = coalesce(expires_at, now()) + @d, updated_at = now()
 where email = @e;";

            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@e", email);
            // tipa como interval para o Postgres
            cmd.Parameters.Add("@d", NpgsqlDbType.Interval).Value = delta;
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeactivateAsync(string licenseKey)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            const string sql = @"update licenses set status='canceled', updated_at=now() where license_key=@k;";
            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@k", licenseKey);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            const string sql = @"
insert into downloads(ts, ip, ua, source, referer)
values (now(), 'webhook', @evt, 'hotmart', @em);";

            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@evt", (object?)evt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@em",  (object?)email ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}




