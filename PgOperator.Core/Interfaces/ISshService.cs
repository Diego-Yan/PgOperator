using PgOperator.Core.Models;

namespace PgOperator.Core.Interfaces;

/// <summary>
/// Service for executing commands on remote servers via SSH.
/// </summary>
public interface ISshService
{
    /// <summary>
    /// Test SSH connectivity to the server.
    /// </summary>
    Task<SshResult> TestConnectionAsync(ServerConnection server, CancellationToken ct = default);

    /// <summary>
    /// Execute a command on the remote server and return the result.
    /// </summary>
    Task<SshResult> ExecuteCommandAsync(ServerConnection server, string command, CancellationToken ct = default);

    /// <summary>
    /// Execute a command and stream output in real-time.
    /// </summary>
    Task<SshResult> ExecuteCommandWithProgressAsync(
        ServerConnection server, string command,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a SQL query through psql on the remote server.
    /// </summary>
    Task<SshResult> ExecutePsqlAsync(ServerConnection server, PgInstance instance, string sql, CancellationToken ct = default);
}

public class SshResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public TimeSpan Duration { get; set; }
}
