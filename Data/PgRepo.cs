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

        var sql = "insert into downloads(ts, ip, ua, referer) values (now(), @ip, @ua, @rf);";
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

        // registra ativação
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
        var sql = @"insert into downloads(ts, ip, ua, referer)
                    values (now(), 'webhook', @evt, @em);";
        await using var cmd = new NpgsqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@evt", (object?)evt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@em", (object?)email ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
