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

        // ---------- helpers ----------
        private static object DbVal(string? s) => (object?)s ?? DBNull.Value;
        private static string? NormEmail(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToLowerInvariant();

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // ---------- logs: downloads ----------
        public async Task LogDownloadAsync(string? ip, string? ua, string? referer)
        {
            try
            {
                await using var con = new NpgsqlConnection(_cs);
                await con.OpenAsync();

                // Protege contra varchar curto (se sua coluna não for TEXT)
                var uaSafe = Truncate(ua, 512);
                var rfSafe = Truncate(referer, 512);

                var sql = @"insert into downloads(ts, ip, ua, source, referer)
                            values (now(), @ip, @ua, 'api', @rf);";
                await using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ua", (object?)uaSafe ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rf", (object?)rfSafe ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PgRepo] LogDownloadAsync falhou: {ex.Message}");
                // não propaga — log não pode derrubar request
            }
        }

        // ---------- cria/renova licença ----------
        public async Task<SignedLicense?> IssueOrRenewAsync(string licenseKey, string? email, string? fingerprint)
        {
            // Se quiser validar key antes, consulte uma tabela/whitelist aqui
            if (string.IsNullOrWhiteSpace(licenseKey))
                return null;

            var expires = DateTime.UtcNow.AddDays(30);
            var emailNorm = NormEmail(email);

            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            // UPSERT em licenses
            var upsert = @"
insert into licenses (license_key, email, status, expires_at, created_at, updated_at)
values (@k,@e,'active',@x,now(),now())
on conflict (license_key) do update
   set email = excluded.email,
       status = 'active',
       expires_at = excluded.expires_at,
       updated_at = now();";
            await using (var cmd = new NpgsqlCommand(upsert, con))
            {
                cmd.Parameters.AddWithValue("@k", licenseKey);
                cmd.Parameters.AddWithValue("@e", DbVal(emailNorm));
                cmd.Parameters.AddWithValue("@x", expires);
                await cmd.ExecuteNonQueryAsync();
            }

            // UPSERT “leve” em activations por (license_id,fingerprint) se você tiver unique;
            // se não tiver unique, ainda assim evitamos duplicar com on conflict do id artificial.
            var insAct = @"
insert into activations (license_id, fingerprint, first_seen_at, last_seen_at, status)
values (@k, @f, now(), now(), 'active')
on conflict do nothing;";
            await using (var cmd = new NpgsqlCommand(insAct, con))
            {
                cmd.Parameters.AddWithValue("@k", licenseKey);
                cmd.Parameters.AddWithValue("@f", DbVal(fingerprint));
                await cmd.ExecuteNonQueryAsync();
            }

            // Sempre atualiza last_seen
            var touch = @"update activations
                            set last_seen_at = now(), status='active'
                          where license_id=@k and (@f is null or fingerprint=@f);";
            await using (var cmd = new NpgsqlCommand(touch, con))
            {
                cmd.Parameters.AddWithValue("@k", licenseKey);
                cmd.Parameters.AddWithValue("@f", DbVal(fingerprint));
                await cmd.ExecuteNonQueryAsync();
            }

            return new SignedLicense
            {
                Payload = new LicensePayload
                {
                    LicenseId = licenseKey,
                    Email = emailNorm ?? "",
                    Fingerprint = fingerprint ?? "",
                    ExpiresAtUtc = expires,
                    SubscriptionStatus = "active"
                }
            };
        }

        public async Task<SignedLicense?> TryGetByKeyAsync(string licenseKey)
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

            return new SignedLicense
            {
                Payload = new LicensePayload
                {
                    LicenseId = licenseKey,
                    Email = email,
                    Fingerprint = "",
                    ExpiresAtUtc = expires,
                    SubscriptionStatus = status
                }
            };
        }

        // ---------- trial ----------
        public async Task<SignedLicense> GetOrStartTrialAsync(string fingerprint, string? email, int trialDays)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
                throw new ArgumentException("fingerprint required", nameof(fingerprint));

            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            DateTime? started = null;
            var find = @"select min(first_seen_at) from activations
                         where fingerprint=@f and status='trial';";
            await using (var cmd = new NpgsqlCommand(find, con))
            {
                cmd.Parameters.AddWithValue("@f", fingerprint);
                var o = await cmd.ExecuteScalarAsync();
                if (o is DateTime dt) started = dt;
            }

            if (started is null)
            {
                var ins = @"insert into activations
                            (license_id, fingerprint, machine_name, client_version, ip, first_seen_at, last_seen_at, status)
                            values ('TRIAL', @f, null, null, null, now(), now(), 'trial')
                            on conflict do nothing;";
                await using var cmd = new NpgsqlCommand(ins, con)
                {
                    Parameters =
                    {
                        new("@f", fingerprint)
                    }
                };
                await cmd.ExecuteNonQueryAsync();
                started = DateTime.UtcNow;
            }
            else
            {
                var upd = @"update activations
                            set last_seen_at=now()
                            where fingerprint=@f and status='trial';";
                await using var cmd = new NpgsqlCommand(upd, con);
                cmd.Parameters.AddWithValue("@f", fingerprint);
                await cmd.ExecuteNonQueryAsync();
            }

            var expires = started.Value.AddDays(trialDays);

            return new SignedLicense
            {
                Payload = new LicensePayload
                {
                    LicenseId = "TRIAL",
                    Email = NormEmail(email) ?? "",
                    Fingerprint = fingerprint,
                    ExpiresAtUtc = expires,
                    SubscriptionStatus = "trial"
                }
            };
        }

        public async Task ProlongByEmailAsync(string email, TimeSpan delta)
        {
            await using var con = new NpgsqlConnection(_cs);
            await con.OpenAsync();

            var sql = @"update licenses
                           set expires_at = coalesce(expires_at, now()) + @d, updated_at=now()
                         where email=@e;";
            await using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@e", NormEmail(email) ?? "");
            cmd.Parameters.Add("@d", NpgsqlDbType.Interval).Value = delta;
            await cmd.ExecuteNonQueryAsync();
        }

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

        // ---------- logs: webhook ----------
        public async Task LogWebhookAsync(string? evt, string? email, int appliedDays, JsonDocument raw)
        {
            try
            {
                await using var con = new NpgsqlConnection(_cs);
                await con.OpenAsync();

                // Reuso da tabela downloads para “ping” do webhook (como você fez)
                var sql = @"insert into downloads(ts, ip, ua, source, referer)
                            values (now(), 'webhook', @evt, 'hotmart', @em);";
                await using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@evt", DbVal(Truncate(evt, 128)));
                cmd.Parameters.AddWithValue("@em", DbVal(Truncate(email, 256)));
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PgRepo] LogWebhookAsync falhou: {ex.Message}");
                // não propaga
            }
        }
    }
}



