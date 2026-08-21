using FortiScope.Models;

namespace FortiScope.Services;

public static class AlertSettingsValidator
{
    public static string? Validate(AlertSettingsRequest request)
    {
        if (request.CpuWarningPercent is < 1 or > 99)
            return "CPU Warning threshold must be between 1 and 99.";
        if (request.CpuCriticalPercent is < 1 or > 100)
            return "CPU Critical threshold must be between 1 and 100.";
        if (request.CpuCriticalPercent <= request.CpuWarningPercent)
            return "CPU Critical threshold must be greater than CPU Warning threshold.";
        if (request.MemoryWarningPercent is < 1 or > 99)
            return "Memory Warning threshold must be between 1 and 99.";
        if (request.MemoryCriticalPercent is < 1 or > 100)
            return "Memory Critical threshold must be between 1 and 100.";
        if (request.MemoryCriticalPercent <= request.MemoryWarningPercent)
            return "Memory Critical threshold must be greater than Memory Warning threshold.";
        if (request.InterfaceUtilizationWarningPercent is < 1 or > 99)
            return "Interface Warning threshold must be between 1 and 99.";
        if (request.InterfaceUtilizationCriticalPercent is < 1 or > 100)
            return "Interface Critical threshold must be between 1 and 100.";
        if (request.InterfaceUtilizationCriticalPercent <= request.InterfaceUtilizationWarningPercent)
            return "Interface Critical threshold must be greater than Interface Warning threshold.";
        if (request.OfflineTimeoutSeconds is < 10 or > 3600)
            return "Offline timeout must be between 10 and 3600 seconds.";
        return null;
    }
}
