using FortiScope.Models;
using System.Net;

namespace FortiScope.Services;

public static class DeviceValidator
{
    public static string? Validate(DeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Device name zorunludur.";
        if (string.IsNullOrWhiteSpace(request.IpAddress)) return "IP address zorunludur.";
        if (!IPAddress.TryParse(request.IpAddress.Trim(), out _)) return "Geçerli bir IP address girilmelidir.";
        if (!string.Equals(request.SnmpVersion, "v3", StringComparison.OrdinalIgnoreCase))
            return "Şimdilik yalnızca SNMPv3 destekleniyor.";
        if (string.IsNullOrWhiteSpace(request.SnmpUsername)) return "SNMPv3 username zorunludur.";
        if (!string.Equals(request.AuthProtocol, "SHA1", StringComparison.OrdinalIgnoreCase))
            return "Şimdilik yalnızca SHA1 authentication protocol destekleniyor.";
        if (!string.Equals(request.PrivacyProtocol, "AES128", StringComparison.OrdinalIgnoreCase))
            return "Şimdilik yalnızca AES128 privacy protocol destekleniyor.";
        return null;
    }
}
