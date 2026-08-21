using FortiScope.Models;
using System.Net;

namespace FortiScope.Services;

public static class DeviceValidator
{
    public static string? Validate(DeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Device name is required.";
        if (string.IsNullOrWhiteSpace(request.IpAddress)) return "IP address is required.";
        if (!IPAddress.TryParse(request.IpAddress.Trim(), out _)) return "Invalid IP address.";
        if (!string.Equals(request.SnmpVersion, "v3", StringComparison.OrdinalIgnoreCase))
            return "Only SNMPv3 is currently supported.";
        if (string.IsNullOrWhiteSpace(request.SnmpUsername)) return "SNMPv3 username is required.";
        if (!string.Equals(request.AuthProtocol, "SHA1", StringComparison.OrdinalIgnoreCase))
            return "Only the SHA1 authentication protocol is currently supported.";
        if (!string.Equals(request.PrivacyProtocol, "AES128", StringComparison.OrdinalIgnoreCase))
            return "Only the AES128 privacy protocol is currently supported.";
        return null;
    }
}
