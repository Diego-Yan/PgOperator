using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;
using Serilog;

namespace PgOperator.Infra.Storage;

/// <summary>
/// SQLite database service using Dapper for data access.
/// </summary>
public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly DatabaseInitializer _initializer;

    public DatabaseService(string connectionString)
    {
        _connectionString = connectionString;
        _initializer = new DatabaseInitializer(connectionString);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
        Log.Information("Database initialized at {ConnectionString}", _connectionString);
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    // ─── Server Connections ──────────────────────────────────────

    public async Task<List<ServerConnection>> GetAllServersAsync()
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<ServerConnectionRow>(
            @"SELECT id, name, host, port, username, ""group"", tags, auth_method,
              password, private_key_path, private_key_content,
              passphrase, created_at, last_connected_at, is_available, os_info
              FROM server_connections ORDER BY created_at DESC");

        var servers = rows.Select(MapToServerConnection).ToList();
        foreach (var s in servers)
            s.PgInstances = await GetPgInstancesForServerAsync(s.Id);
        return servers;
    }

    public async Task<ServerConnection?> GetServerByIdAsync(Guid id)
    {
        await using var conn = CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<ServerConnectionRow>(
            @"SELECT id, name, host, port, username, ""group"", tags, auth_method,
              password, private_key_path, private_key_content,
              passphrase, created_at, last_connected_at, is_available, os_info
              FROM server_connections WHERE id = @Id", new { Id = id.ToString() });

        return row == null ? null : MapToServerConnection(row);
    }

    public async Task SaveServerAsync(ServerConnection server)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO server_connections (id, name, host, port, username, ""group"", tags, auth_method,
              password, private_key_path, private_key_content,
              passphrase, created_at, last_connected_at, is_available, os_info)
              VALUES (@Id, @Name, @Host, @Port, @Username, @Group, @Tags, @AuthMethod,
              @Password, @PrivateKeyPath, @PrivateKeyContent,
              @Passphrase, @CreatedAt, @LastConnectedAt, @IsAvailable, @OsInfo)
              ON CONFLICT(id) DO UPDATE SET
              name=excluded.name, host=excluded.host, port=excluded.port, username=excluded.username,
              ""group""=excluded.""group"", tags=excluded.tags, auth_method=excluded.auth_method,
              password=excluded.password,
              private_key_path=excluded.private_key_path,
              private_key_content=excluded.private_key_content,
              passphrase=excluded.passphrase,
              last_connected_at=excluded.last_connected_at,
              is_available=excluded.is_available, os_info=excluded.os_info",
            MapToRow(server));
    }

    public async Task DeleteServerAsync(Guid id)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM server_connections WHERE id = @Id", new { Id = id.ToString() });
    }

    // ─── PG Instances ───────────────────────────────────────────

    public async Task<List<PgInstance>> GetPgInstancesForServerAsync(Guid serverId)
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<PgInstanceRow>(
            @"SELECT id, server_connection_id, name, host, port, database_name, username,
              password, pg_version, data_directory, is_available, created_at, custom_tags
              FROM pg_instances WHERE server_connection_id = @ServerId
              ORDER BY created_at ASC",
            new { ServerId = serverId.ToString() });

        return rows.Select(MapToPgInstance).ToList();
    }

    public async Task SavePgInstanceAsync(PgInstance instance)
    {
        await using var conn = CreateConnection();
        var customTags = instance.CustomTags != null
            ? JsonSerializer.Serialize(instance.CustomTags)
            : null;

        await conn.ExecuteAsync(
            @"INSERT INTO pg_instances (id, server_connection_id, name, host, port, database_name, username,
              password, pg_version, data_directory, is_available, created_at, custom_tags)
              VALUES (@Id, @ServerConnectionId, @Name, @Host, @Port, @Database, @Username,
              @Password, @PgVersion, @DataDirectory, @IsAvailable, @CreatedAt, @CustomTags)
              ON CONFLICT(id) DO UPDATE SET
              name=excluded.name, host=excluded.host, port=excluded.port, database_name=excluded.database_name,
              username=excluded.username, password=excluded.password,
              pg_version=excluded.pg_version, data_directory=excluded.data_directory,
              is_available=excluded.is_available, custom_tags=excluded.custom_tags",
            new
            {
                instance.Id, instance.ServerConnectionId, instance.Name, instance.Host, instance.Port,
                Database = instance.Database, instance.Username, instance.Password,
                instance.PgVersion, instance.DataDirectory, IsAvailable = instance.IsAvailable ? 1 : 0,
                CreatedAt = instance.CreatedAt.ToString("O"), CustomTags = customTags
            });
    }

    public async Task DeletePgInstanceAsync(Guid id)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM pg_instances WHERE id = @Id", new { Id = id.ToString() });
    }

    // ─── Diagnostic Reports ──────────────────────────────────────

    public async Task SaveDiagnosticReportAsync(DiagnosticReport report)
    {
        await using var conn = CreateConnection();
        var json = JsonSerializer.Serialize(report);
        await conn.ExecuteAsync(
            @"INSERT INTO diagnostic_reports (id, server_id, report_json, created_at, report_type)
              VALUES (@Id, @ServerId, @Json, @CreatedAt, 'standard')",
            new
            {
                Id = report.ReportMeta.ReportId,
                ServerId = report.ReportMeta.Server,
                Json = json,
                CreatedAt = report.ReportMeta.Timestamp.ToString("O")
            });
    }

    public async Task<List<DiagnosticReport>> GetRecentReportsAsync(Guid serverId, int limit = 10)
    {
        await using var conn = CreateConnection();
        var jsons = await conn.QueryAsync<string>(
            @"SELECT report_json FROM diagnostic_reports
              WHERE server_id = @ServerId
              ORDER BY created_at DESC LIMIT @Limit",
            new { ServerId = serverId.ToString(), Limit = limit });

        // [REVIEW-FIX] 修复潜在 NRE：原 ! 抑制了 null 警告但 Where 无法过滤（类型非 nullable）
        // 改为先反序列化为 DiagnosticReport? 再过滤
        return jsons.Select(j => JsonSerializer.Deserialize<DiagnosticReport>(j))
                    .Where(r => r != null)
                    .Cast<DiagnosticReport>()
                    .ToList();
    }

    public async Task SaveSettingAsync(string key, string value)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO settings (key, value, updated_at) VALUES (@Key, @Value, @UpdatedAt)
              ON CONFLICT(key) DO UPDATE SET value=excluded.value, updated_at=excluded.updated_at",
            new { Key = key, Value = value, UpdatedAt = DateTime.UtcNow.ToString("O") });
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT value FROM settings WHERE key = @Key", new { Key = key });
    }

    // ─── Row Mappers ─────────────────────────────────────────────

    private static ServerConnection MapToServerConnection(ServerConnectionRow r) => new()
    {
        Id = Guid.Parse(r.id),
        Name = r.name,
        Host = r.host,
        Port = r.port,
        Username = r.username,
        Group = r.group,
        Tags = r.tags,
        AuthMethod = (SshAuthMethod)r.auth_method,
        Password = r.password,
        PrivateKeyPath = r.private_key_path,
        PrivateKeyContent = r.private_key_content,
        Passphrase = r.passphrase,
        CreatedAt = DateTime.Parse(r.created_at),
        LastConnectedAt = r.last_connected_at != null ? DateTime.Parse(r.last_connected_at) : null,
        IsAvailable = r.is_available == 1,
        OsInfo = r.os_info
    };

    private static object MapToRow(ServerConnection s) => new
    {
        Id = s.Id.ToString(),
        s.Name, s.Host, s.Port, s.Username,
        Group = s.Group,
        s.Tags,
        AuthMethod = (int)s.AuthMethod,
        s.Password,
        s.PrivateKeyPath,
        s.PrivateKeyContent,
        s.Passphrase,
        CreatedAt = s.CreatedAt.ToString("O"),
        LastConnectedAt = s.LastConnectedAt?.ToString("O"),
        IsAvailable = s.IsAvailable ? 1 : 0,
        s.OsInfo
    };

    private static PgInstance MapToPgInstance(PgInstanceRow r) => new()
    {
        Id = Guid.Parse(r.id),
        ServerConnectionId = Guid.Parse(r.server_connection_id),
        Name = r.name,
        Host = r.host,
        Port = r.port,
        Database = r.database_name,
        Username = r.username,
        Password = r.password,
        PgVersion = r.pg_version,
        DataDirectory = r.data_directory,
        IsAvailable = r.is_available == 1,
        CreatedAt = DateTime.Parse(r.created_at),
        CustomTags = r.custom_tags != null
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(r.custom_tags)
            : null
    };

    // ─── Row DTOs for Dapper ─────────────────────────────────────

    private class ServerConnectionRow
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string host { get; set; } = string.Empty;
        public int port { get; set; }
        public string username { get; set; } = string.Empty;
        public string? group { get; set; }
        public string? tags { get; set; }
        public int auth_method { get; set; }
        public string? password { get; set; }
        public string? private_key_path { get; set; }
        public string? private_key_content { get; set; }
        public string? passphrase { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string? last_connected_at { get; set; }
        public int is_available { get; set; }
        public string? os_info { get; set; }
    }

    private class PgInstanceRow
    {
        public string id { get; set; } = string.Empty;
        public string server_connection_id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string host { get; set; } = string.Empty;
        public int port { get; set; }
        public string database_name { get; set; } = string.Empty;
        public string username { get; set; } = string.Empty;
        public string? password { get; set; }
        public string? pg_version { get; set; }
        public string? data_directory { get; set; }
        public int is_available { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string? custom_tags { get; set; }
    }
}
