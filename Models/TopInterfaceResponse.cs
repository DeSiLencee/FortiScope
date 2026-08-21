namespace FortiScope.Models;

public sealed record TopInterfaceResponse(int DeviceId, string DeviceName, string DeviceIp,
    int InterfaceIndex, string InterfaceName, double IncomingMbps, double OutgoingMbps,
    double TotalMbps, double UtilizationPercent, string Severity);
