using System.Diagnostics;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;
using Renci.SshNet;
using Serilog;

namespace PgOperator.Infra.Ssh;

/// <summary>
/// SSH service implementation using SSH.NET library.
/// </summary>
public class SshService : ISshService, IDisposable
{
    private readonly Dictionary<Guid, SshClient> _activeClients = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const int DefaultTimeout = 10; // seconds

    public SshService() { }

    public async Task<SshResult> TestConnectionAsync(ServerConnection server, CancellationToken ct = default)
    {
        return await ExecuteCommandAsync(server, "echo 'SSH_CONNECTION_OK' && uname -a", ct);
    }

    public async Task<SshResult> ExecuteCommandAsync(ServerConnection server, string command, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new SshResult();

        try
        {
            var client = await GetOrCreateClientAsync(server);
            if (!client.IsConnected)
            {
                client.Connect();
            }

            using var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = TimeSpan.FromSeconds(120);

            var asyncResult = cmd.BeginExecute();
            try
            {
                // [REVIEW-FIX] 回退为 Task.Run 轮询：直接在 UI 线程 await Task.Delay 可能与
                // SSH.NET 的内部回调产生线程调度问题，Task.Run 隔离更安全
                await Task.Run(() =>
                {
                    while (!asyncResult.IsCompleted && !ct.IsCancellationRequested)
                        Task.Delay(50, ct).Wait();
                }, ct);
            }
            catch (OperationCanceledException) { cmd.CancelAsync(); }

            cmd.EndExecute(asyncResult);
            result.Output = cmd.Result?.Trim() ?? string.Empty;
            result.Error = cmd.Error?.Trim() ?? string.Empty;
            result.ExitCode = cmd.ExitStatus;
            result.Success = cmd.ExitStatus == 0; // stderr may contain NOTICE/WARNING even on success
            result.Duration = sw.Elapsed;

            if (!result.Success && !string.IsNullOrEmpty(result.Error))
            {
                Log.Warning("SSH command failed on {Host}: {Error}. Command: {Command}",
                    server.Host, result.Error, command[..Math.Min(command.Length, 200)]);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Duration = sw.Elapsed;
            Log.Error(ex, "SSH command execution failed on {Host}", server.Host);
            await RemoveClientAsync(server.Id);
        }

        return result;
    }

    public async Task<SshResult> ExecuteCommandWithProgressAsync(
        ServerConnection server, string command,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new SshResult();
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        try
        {
            var client = await GetOrCreateClientAsync(server);
            if (!client.IsConnected)
            {
                client.Connect();
            }

            using var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = TimeSpan.FromSeconds(300); // 5 min for long ops

            // [REVIEW-FIX] 回退流式改动：StreamReader.Read() 是阻塞调用，在无输出时会卡住线程
            // 恢复原始实现（命令完成后统一读取输出），保证稳定性优先
            var asyncResult = cmd.BeginExecute();
            using var outputReader = new StreamReader(cmd.OutputStream);
            using var errorReader = new StreamReader(cmd.ExtendedOutputStream);

            // Wait for command to complete
            while (!asyncResult.IsCompleted && !ct.IsCancellationRequested)
                await Task.Delay(200, ct);

            // Read all output after completion
            var outStr = outputReader.ReadToEnd();
            if (!string.IsNullOrEmpty(outStr)) { outputBuilder.Append(outStr); onOutput?.Invoke(outStr); }
            var errStr = errorReader.ReadToEnd();
            if (!string.IsNullOrEmpty(errStr)) { errorBuilder.Append(errStr); onError?.Invoke(errStr); }

            cmd.EndExecute(asyncResult);

            result.Output = outputBuilder.ToString().Trim();
            result.Error = errorBuilder.ToString().Trim();
            result.ExitCode = cmd.ExitStatus;
            result.Success = cmd.ExitStatus == 0;
            result.Duration = sw.Elapsed;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Duration = sw.Elapsed;
            Log.Error(ex, "SSH progress command failed on {Host}", server.Host);
            await RemoveClientAsync(server.Id);
        }

        return result;
    }

    public async Task<SshResult> ExecutePsqlAsync(
        ServerConnection server, PgInstance instance, string sql, CancellationToken ct = default)
    {
        // Build psql connection string and execute SQL
        var escapedSql = sql.Replace("'", "'\\''");
        var safePwd = (instance.Password ?? "").Replace("'", "'\\''");
        var psqlCmd = $"PGPASSWORD='{safePwd}' " +
                      $"psql -h {instance.Host} -p {instance.Port} -U {instance.Username} " +
                      $"-d {instance.Database} -t -A -c '{escapedSql}'";

        return await ExecuteCommandAsync(server, psqlCmd, ct);
    }

    private async Task<SshClient> GetOrCreateClientAsync(ServerConnection server)
    {
        await _lock.WaitAsync();
        try
        {
            if (_activeClients.TryGetValue(server.Id, out var existingClient) && existingClient.IsConnected)
            {
                return existingClient;
            }

            // Dispose old client if exists
            if (_activeClients.TryGetValue(server.Id, out var oldClient))
            {
                oldClient.Dispose();
                _activeClients.Remove(server.Id);
            }

            var connectionInfo = BuildConnectionInfo(server);
            var client = new SshClient(connectionInfo);
            client.KeepAliveInterval = TimeSpan.FromSeconds(30);
            _activeClients[server.Id] = client;
            return client;
        }
        finally
        {
            _lock.Release();
        }
    }

    private ConnectionInfo BuildConnectionInfo(ServerConnection server)
    {
        AuthenticationMethod authMethod;

        switch (server.AuthMethod)
        {
            case SshAuthMethod.Password:
                var password = server.Password!;
                authMethod = new PasswordAuthenticationMethod(server.Username, password);
                break;

            case SshAuthMethod.PrivateKeyFile:
                var keyPath = server.PrivateKeyPath!;
                if (!string.IsNullOrEmpty(server.Passphrase))
                {
                    var passphrase = server.Passphrase;
                    authMethod = new PrivateKeyAuthenticationMethod(server.Username,
                        new PrivateKeyFile(keyPath, passphrase));
                }
                else
                {
                    authMethod = new PrivateKeyAuthenticationMethod(server.Username,
                        new PrivateKeyFile(keyPath));
                }
                break;

            case SshAuthMethod.PrivateKeyContent:
            {
                var keyContent = server.PrivateKeyContent!;
                using var keyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(keyContent));
                if (!string.IsNullOrEmpty(server.Passphrase))
                {
                    var passphrase = server.Passphrase;
                    authMethod = new PrivateKeyAuthenticationMethod(server.Username,
                        new PrivateKeyFile(keyStream, passphrase));
                }
                else
                {
                    authMethod = new PrivateKeyAuthenticationMethod(server.Username,
                        new PrivateKeyFile(keyStream, string.Empty));
                }
                break;
            }

            default:
                throw new ArgumentException($"Unsupported auth method: {server.AuthMethod}");
        }

        return new ConnectionInfo(server.Host, server.Port, server.Username, authMethod)
        {
            Timeout = TimeSpan.FromSeconds(DefaultTimeout)
        };
    }

    private async Task RemoveClientAsync(Guid serverId)
    {
        await _lock.WaitAsync();
        try
        {
            if (_activeClients.TryGetValue(serverId, out var client))
            {
                client.Dispose();
                _activeClients.Remove(serverId);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Wait();
        try
        {
            foreach (var client in _activeClients.Values)
            {
                try { client.Dispose(); } catch { /* ignore */ }
            }
            _activeClients.Clear();
        }
        finally { _lock.Release(); }
        _lock.Dispose();
    }
}
