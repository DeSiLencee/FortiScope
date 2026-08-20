namespace FortiScope.Configuration;

public sealed class SnmpOptions
{
    public const string SectionName = "Snmp";

    public string Host { get; init; } = "192.168.64.2";
    public int Port { get; init; } = 161;
    public string Version { get; init; } = "v3";
    public string Username { get; init; } = "fortiscope";
    public string SecurityLevel { get; init; } = "authPriv";
    public string AuthenticationProtocol { get; init; } = "SHA1";
    public string PrivacyProtocol { get; init; } = "AES128";
    public int TimeoutMilliseconds { get; init; } = 3000;
    public string? AuthPassword { get; init; }
    public string? PrivacyPassword { get; init; }
}
