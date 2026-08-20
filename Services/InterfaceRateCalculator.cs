namespace FortiScope.Services;

public sealed record InterfaceRateResult(
    double IncomingMbps,
    double OutgoingMbps,
    double TotalMbps,
    double? UtilizationPercent,
    bool IsMeasuring);

public static class InterfaceRateCalculator
{
    public static InterfaceRateResult Calculate(
        ulong currentInOctets,
        ulong currentOutOctets,
        ulong? previousInOctets,
        ulong? previousOutOctets,
        TimeSpan elapsed,
        long? speedMbps)
    {
        if (!previousInOctets.HasValue || !previousOutOctets.HasValue || elapsed <= TimeSpan.Zero)
            return new InterfaceRateResult(0, 0, 0, null, true);

        // Daha küçük sayaç cihaz yeniden başlatması veya sayaç resetidir; negatif rate üretilmez.
        if (currentInOctets < previousInOctets.Value || currentOutOctets < previousOutOctets.Value)
            return new InterfaceRateResult(0, 0, 0, null, true);

        var seconds = elapsed.TotalSeconds;
        var incoming = (currentInOctets - previousInOctets.Value) * 8d / seconds / 1_000_000d;
        var outgoing = (currentOutOctets - previousOutOctets.Value) * 8d / seconds / 1_000_000d;
        var total = incoming + outgoing;
        double? utilization = speedMbps is > 0
            ? Math.Max(incoming, outgoing) / speedMbps.Value * 100d
            : null;

        return new InterfaceRateResult(incoming, outgoing, total, utilization, false);
    }
}
