namespace FortiScope.Models;

public sealed record SnmpConnectionTestResult(
    bool Success,
    string IpAddress,
    string? DeviceDescription,
    string Message);
