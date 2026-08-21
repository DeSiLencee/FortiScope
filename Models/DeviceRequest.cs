namespace FortiScope.Models;

public sealed record DeviceRequest(string? Name, string? IpAddress, string? SnmpVersion,
    string? SnmpUsername, string? AuthProtocol, string? PrivacyProtocol, bool Enabled);
