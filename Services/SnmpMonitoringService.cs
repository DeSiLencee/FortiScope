using System.Globalization;
using System.Net;
using System.Net.Sockets;
using FortiScope.Configuration;
using FortiScope.Models;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Microsoft.Extensions.Options;

namespace FortiScope.Services;

public sealed class SnmpMonitoringService : ISnmpMonitoringService
{
    private const string CpuOid = "1.3.6.1.4.1.12356.101.4.1.3.0";
    private const string MemoryOid = "1.3.6.1.4.1.12356.101.4.1.4.0";
    private const string SessionOid = "1.3.6.1.4.1.12356.101.4.1.8.0";
    private const string DescriptionOid = "1.3.6.1.2.1.1.1.0";
    private const string IfNameOid = "1.3.6.1.2.1.31.1.1.1.1";
    private const string IfAliasOid = "1.3.6.1.2.1.31.1.1.1.18";
    private const string IfOperStatusOid = "1.3.6.1.2.1.2.2.1.8";
    private const string IfAdminStatusOid = "1.3.6.1.2.1.2.2.1.7";
    private const string IfHighSpeedOid = "1.3.6.1.2.1.31.1.1.1.15";
    private const string IfHcInOctetsOid = "1.3.6.1.2.1.31.1.1.1.6";
    private const string IfHcOutOctetsOid = "1.3.6.1.2.1.31.1.1.1.10";

    private readonly SnmpOptions _options;
    private readonly ILogger<SnmpMonitoringService> _logger;
    private readonly Dictionary<int, CounterSample> _previousCounters = new();
    private MonitoringSnapshot _current;

    public SnmpMonitoringService(IOptions<SnmpOptions> options, ILogger<SnmpMonitoringService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _current = DisconnectedSnapshot(GetConfigurationError() ?? "İlk SNMP sorgusu bekleniyor.");
    }

    public MonitoringSnapshot GetCurrent() => Volatile.Read(ref _current);

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        var configurationError = GetConfigurationError();
        if (configurationError is not null)
        {
            Volatile.Write(ref _current, DisconnectedSnapshot(configurationError));
            _logger.LogWarning("SNMP sorgusu yapılandırma eksik olduğu için atlandı: {Reason}", configurationError);
            return;
        }

        try
        {
            var snapshot = await Task.Run(QueryDevice, cancellationToken);
            Volatile.Write(ref _current, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var previous = GetCurrent();
            Volatile.Write(ref _current, previous with { Connected = false, ErrorMessage = GetSafeErrorMessage(exception) });
            _logger.LogWarning("SNMP sorgusu başarısız oldu ({ExceptionType}).", exception.GetType().Name);
        }
    }

    private MonitoringSnapshot QueryDevice()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(_options.Host), _options.Port);
#pragma warning disable CS0618 // FortiGate kullanıcısı SHA1 ile yapılandırılmıştır.
        var authentication = new SHA1AuthenticationProvider(new OctetString(_options.AuthPassword!));
#pragma warning restore CS0618
        var privacy = new AESPrivacyProvider(new OctetString(_options.PrivacyPassword!), authentication);
        var variables = new List<Variable>
        {
            new(new ObjectIdentifier(CpuOid)), new(new ObjectIdentifier(MemoryOid)),
            new(new ObjectIdentifier(SessionOid)), new(new ObjectIdentifier(DescriptionOid))
        };

        var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        var report = discovery.GetResponse(_options.TimeoutMilliseconds, endpoint);
        var response = SendSystemRequest(endpoint, variables, privacy, report);
        var values = response.Pdu().Variables;
        if (values.Count != variables.Count)
            throw new SnmpException("FortiGate eksik sistem SNMP değişkeni döndürdü.");

        var interfaceResult = QueryInterfaces(endpoint, privacy, report);
        if (interfaceResult.Interfaces.Count == 0)
            throw new SnmpException("FortiGate IF-MIB interface tablosu alınamadı.");

        return new MonitoringSnapshot(
            CleanDeviceName(values[3].Data.ToString()), _options.Host, true,
            ParseInt(values[0]), ParseInt(values[1]), ParseInt(values[2]),
            interfaceResult.Interfaces, interfaceResult.CollectedAt,
            interfaceResult.HasPartialErrors ? "Bazı interface alanları SNMP üzerinden alınamadı." : null);
    }

    private ISnmpMessage SendSystemRequest(IPEndPoint endpoint, IList<Variable> variables,
        IPrivacyProvider privacy, ISnmpMessage report)
    {
        var request = CreateRequest(variables, privacy, report);
        var response = request.GetResponse(_options.TimeoutMilliseconds, endpoint);
        if (response is ReportMessage)
        {
            if (response.Pdu().Variables.Count == 0 || response.Pdu().Variables[0].Id != Messenger.NotInTimeWindow)
                throw new SnmpException("FortiGate SNMPv3 güvenlik raporu döndürdü.");
            request = CreateRequest(variables, privacy, response);
            response = request.GetResponse(_options.TimeoutMilliseconds, endpoint);
        }
        if (response.Pdu().ErrorStatus.ToInt32() != 0)
            throw ErrorException.Create("SNMP yanıt hatası", endpoint.Address, response);
        return response;
    }

    private InterfaceQueryResult QueryInterfaces(IPEndPoint endpoint, IPrivacyProvider privacy, ISnmpMessage report)
    {
        var columns = new Dictionary<string, IReadOnlyDictionary<int, Variable>>();
        var failedColumns = new HashSet<string>();
        foreach (var oid in new[] { IfNameOid, IfAliasOid, IfOperStatusOid, IfAdminStatusOid,
                     IfHighSpeedOid, IfHcInOctetsOid, IfHcOutOctetsOid })
        {
            try { columns[oid] = WalkColumn(endpoint, privacy, report, oid); }
            catch (Exception exception) when (exception is SnmpException or SocketException)
            {
                columns[oid] = new Dictionary<int, Variable>();
                failedColumns.Add(oid);
                _logger.LogWarning("IF-MIB sütunu alınamadı: {Oid} ({ExceptionType}).", oid, exception.GetType().Name);
            }
        }

        var indexes = columns.Values.SelectMany(column => column.Keys).Distinct().Order().ToArray();
        var collectedAt = DateTimeOffset.UtcNow;
        var interfaces = new List<NetworkInterfaceSnapshot>(indexes.Length);
        foreach (var index in indexes)
            interfaces.Add(CreateInterfaceSnapshot(index, columns, failedColumns, collectedAt));

        var activeIndexes = indexes.ToHashSet();
        foreach (var removedIndex in _previousCounters.Keys.Where(index => !activeIndexes.Contains(index)).ToArray())
            _previousCounters.Remove(removedIndex);

        return new InterfaceQueryResult(interfaces, collectedAt, failedColumns.Count > 0);
    }

    private IReadOnlyDictionary<int, Variable> WalkColumn(IPEndPoint endpoint, IPrivacyProvider privacy,
        ISnmpMessage report, string rootOid)
    {
        var variables = new List<Variable>();
        Messenger.BulkWalk(VersionCode.V3, endpoint, new OctetString(_options.Username), OctetString.Empty,
            new ObjectIdentifier(rootOid), variables, _options.TimeoutMilliseconds, 20,
            WalkMode.WithinSubtree, privacy, report);
        return variables.Select(variable => (Index: GetIndex(variable.Id, rootOid), Variable: variable))
            .Where(item => item.Index.HasValue)
            .ToDictionary(item => item.Index!.Value, item => item.Variable);
    }

    private NetworkInterfaceSnapshot CreateInterfaceSnapshot(int index,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, Variable>> columns,
        IReadOnlySet<string> failedColumns, DateTimeOffset collectedAt)
    {
        var errors = new List<string>();
        string? name = ReadString(columns, IfNameOid, index);
        var alias = ReadString(columns, IfAliasOid, index);
        var adminStatus = ReadInt(columns, IfAdminStatusOid, index);
        var operStatus = ReadInt(columns, IfOperStatusOid, index);
        var speedMbps = ReadLong(columns, IfHighSpeedOid, index);
        var inOctets = ReadCounter64(columns, IfHcInOctetsOid, index);
        var outOctets = ReadCounter64(columns, IfHcOutOctetsOid, index);

        if (string.IsNullOrWhiteSpace(name)) errors.Add("Interface adı alınamadı");
        if (!inOctets.HasValue || !outOctets.HasValue) errors.Add("64-bit trafik sayacı alınamadı");
        if (failedColumns.Count > 0) errors.Add("Eksik IF-MIB alanı var");

        InterfaceRateResult rate;
        if (inOctets.HasValue && outOctets.HasValue)
        {
            _previousCounters.TryGetValue(index, out var previous);
            rate = InterfaceRateCalculator.Calculate(inOctets.Value, outOctets.Value,
                previous?.InOctets, previous?.OutOctets,
                previous is null ? TimeSpan.Zero : collectedAt - previous.CollectedAt, speedMbps);
            _previousCounters[index] = new CounterSample(inOctets.Value, outOctets.Value, collectedAt);
        }
        else
        {
            rate = new InterfaceRateResult(0, 0, 0, null, true);
        }

        var interfaceName = name ?? $"ifIndex {index}";
        return new NetworkInterfaceSnapshot(index, interfaceName, GetInterfaceType(interfaceName), alias, adminStatus,
            operStatus, GetLinkStatus(operStatus), speedMbps, rate.IncomingMbps, rate.OutgoingMbps,
            rate.TotalMbps, rate.UtilizationPercent, rate.IsMeasuring,
            errors.Count == 0 ? null : string.Join(". ", errors));
    }

    private GetRequestMessage CreateRequest(IList<Variable> variables, IPrivacyProvider privacy, ISnmpMessage report) =>
        new(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
            new OctetString(_options.Username), OctetString.Empty, variables, privacy,
            Messenger.MaxMessageSize, report);

    private string? GetConfigurationError()
    {
        if (string.IsNullOrWhiteSpace(_options.AuthPassword) || string.IsNullOrWhiteSpace(_options.PrivacyPassword))
            return "SNMPv3 kimlik bilgileri eksik. User Secrets içinde Snmp:AuthPassword ve Snmp:PrivacyPassword ayarlanmalıdır.";
        if (!IPAddress.TryParse(_options.Host, out _)) return "SNMP host ayarı geçerli bir IP adresi değil.";
        if (_options.Port is < 1 or > 65535) return "SNMP port ayarı geçersiz.";
        if (!string.Equals(_options.Version, "v3", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_options.SecurityLevel, "authPriv", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_options.AuthenticationProtocol, "SHA1", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_options.PrivacyProtocol, "AES128", StringComparison.OrdinalIgnoreCase))
            return "SNMP protokol ayarları v3/authPriv/SHA1/AES128 olmalıdır.";
        return null;
    }

    private MonitoringSnapshot DisconnectedSnapshot(string error) =>
        new("FortiGate-ARM64-KVM", _options.Host, false, null, null, null, [], null, error);

    private static int ParseInt(Variable variable)
    {
        if (!int.TryParse(variable.Data.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new SnmpException($"{variable.Id} için sayısal olmayan SNMP değeri alındı.");
        return value;
    }

    private static int? GetIndex(ObjectIdentifier identifier, string rootOid)
    {
        var value = identifier.ToString();
        return value.StartsWith(rootOid + ".", StringComparison.Ordinal) &&
               int.TryParse(value[(rootOid.Length + 1)..], out var index) ? index : null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, IReadOnlyDictionary<int, Variable>> columns,
        string oid, int index) => columns[oid].TryGetValue(index, out var value) ? value.Data.ToString().Trim() : null;

    private static int? ReadInt(IReadOnlyDictionary<string, IReadOnlyDictionary<int, Variable>> columns,
        string oid, int index) => columns[oid].TryGetValue(index, out var value) &&
                                  int.TryParse(value.Data.ToString(), out var result) ? result : null;

    private static long? ReadLong(IReadOnlyDictionary<string, IReadOnlyDictionary<int, Variable>> columns,
        string oid, int index) => columns[oid].TryGetValue(index, out var value) &&
                                   long.TryParse(value.Data.ToString(), out var result) && result > 0 ? result : null;

    private static ulong? ReadCounter64(IReadOnlyDictionary<string, IReadOnlyDictionary<int, Variable>> columns,
        string oid, int index) => columns[oid].TryGetValue(index, out var value) && value.Data is Counter64 counter
            ? counter.ToUInt64() : null;

    private static string GetLinkStatus(int? status) => status switch { 1 => "Aktif", 2 => "Kapalı", _ => "Bilinmiyor" };

    private static string GetInterfaceType(string name)
    {
        if (name.StartsWith("port", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("fortilink", StringComparison.OrdinalIgnoreCase)) return "Fiziksel";
        if (name.EndsWith(".root", StringComparison.OrdinalIgnoreCase)) return "Sanal";
        return "Diğer";
    }

    private static string CleanDeviceName(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "FortiGate-ARM64-KVM";
        var firstLine = description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return firstLine.Length > 80 ? firstLine[..80] : firstLine;
    }

    private static string GetSafeErrorMessage(Exception exception) => exception switch
    {
        Lextm.SharpSnmpLib.Messaging.TimeoutException => "FortiGate SNMP sorgusu zaman aşımına uğradı.",
        SocketException => "FortiGate SNMP adresine ulaşılamadı.",
        SnmpException => "FortiGate SNMPv3 verileri alınamadı veya doğrulanamadı.",
        _ => "SNMP verileri alınırken beklenmeyen bir hata oluştu."
    };

    private sealed record CounterSample(ulong InOctets, ulong OutOctets, DateTimeOffset CollectedAt);
    private sealed record InterfaceQueryResult(IReadOnlyList<NetworkInterfaceSnapshot> Interfaces,
        DateTimeOffset CollectedAt, bool HasPartialErrors);
}
