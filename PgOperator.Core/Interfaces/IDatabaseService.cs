using PgOperator.Core.Models;

namespace PgOperator.Core.Interfaces;

public interface IDatabaseService
{
    Task InitializeAsync();
    Task<List<ServerConnection>> GetAllServersAsync();
    Task<ServerConnection?> GetServerByIdAsync(Guid id);
    Task SaveServerAsync(ServerConnection server);
    Task DeleteServerAsync(Guid id);
    Task<List<PgInstance>> GetPgInstancesForServerAsync(Guid serverId);
    Task SavePgInstanceAsync(PgInstance instance);
    Task DeletePgInstanceAsync(Guid id);
    Task SaveDiagnosticReportAsync(DiagnosticReport report);
    Task<List<DiagnosticReport>> GetRecentReportsAsync(Guid serverId, int limit = 10);
    Task SaveSettingAsync(string key, string value);
    Task<string?> GetSettingAsync(string key);
}
