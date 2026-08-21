namespace FortiScope.Data.Entities;

public sealed class Device
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string IpAddress { get; set; }

    public string SnmpVersion { get; set; } = "v3";

    public string? SnmpUsername { get; set; }

    public string? AuthProtocol { get; set; }

    public string? PrivacyProtocol { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
