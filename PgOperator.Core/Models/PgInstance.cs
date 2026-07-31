namespace PgOperator.Core.Models;

public class PgInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerConnectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "postgres";
    public string Username { get; set; } = "postgres";
    public string? Password { get; set; }
    public string? PgVersion { get; set; }
    public string? DataDirectory { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string>? CustomTags { get; set; }
}
