using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.Diagnostics;

/// <summary>
/// Context passed to each diagnostic check, providing SSH access and server info.
/// </summary>
public class DiagnosticContext
{
    public ISshService SshService { get; }
    public ServerConnection Server { get; }
    public PgInstance? PgInstance { get; }
    public CancellationToken CancellationToken { get; }

    public DiagnosticContext(ISshService sshService, ServerConnection server,
        PgInstance? pgInstance = null, CancellationToken ct = default)
    {
        SshService = sshService;
        Server = server;
        PgInstance = pgInstance;
        CancellationToken = ct;
    }

    /// <summary>
    /// Execute a shell command on the remote server.
    /// </summary>
    public async Task<SshResult> ExecAsync(string command)
    {
        return await SshService.ExecuteCommandAsync(Server, command, CancellationToken);
    }

    /// <summary>
    /// Execute a SQL query via psql on the PG instance.
    /// </summary>
    public async Task<SshResult> QueryAsync(string sql)
    {
        if (PgInstance == null)
            throw new InvalidOperationException("No PG instance configured for SQL queries");
        return await SshService.ExecutePsqlAsync(Server, PgInstance, sql, CancellationToken);
    }
}
