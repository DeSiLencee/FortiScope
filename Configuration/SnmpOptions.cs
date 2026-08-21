namespace FortiScope.Configuration;

public sealed class SnmpOptions
{
    public const string SectionName = "Snmp";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 161;
    public string Version { get; init; } = "v3";
    public string Username { get; init; } = string.Empty;
    public string SecurityLevel { get; init; } = "authPriv";
    public string AuthenticationProtocol { get; init; } = "SHA1";
    public string PrivacyProtocol { get; init; } = "AES128";
    public int TimeoutMilliseconds { get; init; } = 3000;
    public string? AuthPassword { get; init; }
    public string? PrivacyPassword { get; init; }
}
