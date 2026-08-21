using FortiScope.Models;

namespace FortiScope.Services;

public static class InterfaceTrafficAlertPolicy
{
    public static bool IsEligible(NetworkInterfaceSnapshot item, bool deviceEnabled, bool deviceOnline,
        bool alertsEnabled) => deviceEnabled && deviceOnline && alertsEnabled &&
        item.Type == "Physical" && item.OperStatus == 1 && item.SpeedMbps is > 0 &&
        !item.IsMeasuring && item.UtilizationPercent.HasValue;

    public static string GetSeverity(double utilizationPercent, int warning, int critical) =>
        utilizationPercent >= critical ? "critical" : utilizationPercent >= warning ? "warning" : "normal";
}
