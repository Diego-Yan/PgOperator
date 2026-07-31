using Microsoft.Data.Sqlite;

namespace PgOperator.Infra.Storage;

/// <summary>
/// Creates and migrates the SQLite database schema.
/// </summary>
public class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        // Ensure the directory exists
        var dataSource = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        var dir = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Enable foreign key enforcement (SQLite requires this per-connection)
        await using (var pragmaCmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            await pragmaCmd.ExecuteNonQueryAsync();

        var commands = new[]
        {
            // Server connections
            @"CREATE TABLE IF NOT EXISTS server_connections (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                host TEXT NOT NULL,
                port INTEGER NOT NULL DEFAULT 22,
                username TEXT NOT NULL DEFAULT 'root',
                ""group"" TEXT,
                tags TEXT,
                auth_method INTEGER NOT NULL DEFAULT 0,
                password TEXT,
                private_key_path TEXT,
                private_key_content TEXT,
                passphrase TEXT,
                created_at TEXT NOT NULL,
                last_connected_at TEXT,
                is_available INTEGER NOT NULL DEFAULT 0,
                os_info TEXT
            );",

            // PG instances
            @"CREATE TABLE IF NOT EXISTS pg_instances (
                id TEXT PRIMARY KEY,
                server_connection_id TEXT NOT NULL,
                name TEXT NOT NULL,
                host TEXT NOT NULL DEFAULT 'localhost',
                port INTEGER NOT NULL DEFAULT 5432,
                database_name TEXT NOT NULL DEFAULT 'postgres',
                username TEXT NOT NULL DEFAULT 'postgres',
                password TEXT,
                pg_version TEXT,
                data_directory TEXT,
                is_available INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                custom_tags TEXT
                -- (FK removed for personal tool)
            );",

            // Settings (key-value store)
            @"CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );",

            // Diagnostic reports (stored as JSON)
            @"CREATE TABLE IF NOT EXISTS diagnostic_reports (
                id TEXT PRIMARY KEY,
                server_id TEXT NOT NULL,
                report_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                report_type TEXT NOT NULL DEFAULT 'standard'
                -- (FK removed)
            );",

            // Scheduled tasks
            @"CREATE TABLE IF NOT EXISTS scheduled_tasks (
                id TEXT PRIMARY KEY,
                server_id TEXT NOT NULL,
                task_type TEXT NOT NULL,
                cron_expression TEXT NOT NULL,
                config_json TEXT,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                last_run_at TEXT,
                next_run_at TEXT,
                created_at TEXT NOT NULL -- (FK removed)
            );",

            // Task execution history
            @"CREATE TABLE IF NOT EXISTS task_history (
                id TEXT PRIMARY KEY,
                task_id TEXT NOT NULL,
                server_id TEXT NOT NULL,
                status TEXT NOT NULL,
                output TEXT,
                error TEXT,
                started_at TEXT NOT NULL,
                completed_at TEXT,
                duration_ms INTEGER -- (FK removed)
            );",

            // Alert rules
            @"CREATE TABLE IF NOT EXISTS alert_rules (
                id TEXT PRIMARY KEY,
                server_id TEXT NOT NULL,
                rule_name TEXT NOT NULL,
                metric_key TEXT NOT NULL,
                threshold_operator TEXT NOT NULL,
                threshold_value REAL NOT NULL,
                severity TEXT NOT NULL DEFAULT 'warning',
                is_enabled INTEGER NOT NULL DEFAULT 1,
                cooldown_minutes INTEGER NOT NULL DEFAULT 60,
                created_at TEXT NOT NULL -- (FK removed)
            );",

            // Alert history
            @"CREATE TABLE IF NOT EXISTS alert_history (
                id TEXT PRIMARY KEY,
                rule_id TEXT NOT NULL,
                server_id TEXT NOT NULL,
                severity TEXT NOT NULL,
                message TEXT NOT NULL,
                metric_value REAL,
                triggered_at TEXT NOT NULL,
                acknowledged_at TEXT -- (FK removed)
            );"
        };

        foreach (var cmdText in commands)
        {
            await using var cmd = new SqliteCommand(cmdText, connection);
            await cmd.ExecuteNonQueryAsync();
        }

        // Create indexes
        var indexes = new[]
        {
            "CREATE INDEX IF NOT EXISTS idx_pg_instances_server ON pg_instances(server_connection_id);",
            "CREATE INDEX IF NOT EXISTS idx_reports_server ON diagnostic_reports(server_id, created_at);",
            "CREATE INDEX IF NOT EXISTS idx_tasks_server ON scheduled_tasks(server_id);",
            "CREATE INDEX IF NOT EXISTS idx_alerts_server ON alert_rules(server_id);"
        };

        foreach (var idxText in indexes)
        {
            await using var cmd = new SqliteCommand(idxText, connection);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
