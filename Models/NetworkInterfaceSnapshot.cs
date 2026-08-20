namespace FortiScope.Models;

public sealed record NetworkInterfaceSnapshot(
    int Index,
    string Name,
    string Type,
    string? Alias,
    int? AdminStatus,
    int? OperStatus,
    string LinkStatus,
    long? SpeedMbps,
    double IncomingMbps,
    double OutgoingMbps,
    double TotalMbps,
    double? UtilizationPercent,
    bool IsMeasuring,
    string? ErrorMessage);
