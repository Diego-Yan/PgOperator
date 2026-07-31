namespace PgOperator.Core.Models;

public class ServerConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    public string? Group { get; set; }
    public string? Tags { get; set; }

    // SSH Authentication (plaintext — personal tool)
    public SshAuthMethod AuthMethod { get; set; } = SshAuthMethod.Password;
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyContent { get; set; }
    public string? Passphrase { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastConnectedAt { get; set; }
    public bool IsAvailable { get; set; }
    public string? OsInfo { get; set; }

    public List<PgInstance> PgInstances { get; set; } = new();
}

public enum SshAuthMethod { Password, PrivateKeyFile, PrivateKeyContent }
