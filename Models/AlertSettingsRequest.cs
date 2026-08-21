namespace FortiScope.Models;

public sealed record AlertSettingsRequest(
    int CpuWarningPercent,
    int CpuCriticalPercent,
    int MemoryWarningPercent,
    int MemoryCriticalPercent,
    int InterfaceUtilizationWarningPercent,
    int InterfaceUtilizationCriticalPercent,
    int OfflineTimeoutSeconds,
    bool Enabled);
