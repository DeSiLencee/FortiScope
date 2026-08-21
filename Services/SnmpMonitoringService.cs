using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<(int DeviceId, int InterfaceIndex), CounterSample> _previousCounters = new();
    private readonly ConcurrentDictionary<int, MonitoringSnapshot> _currentByDevice = new();
    private MonitoringSnapshot _current;
    private int _defaultDeviceId;

    public SnmpMonitoringService(IOptions<SnmpOptions> options, ILogger<SnmpMonitoringService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _current = DisconnectedSnapshot(GetConfigurationError() ?? "Waiting for the first SNMP poll.");
    }

    public MonitoringSnapshot GetCurrent() => Volatile.Read(ref _current);

    public MonitoringSnapshot? GetCurrent(int deviceId) =>
        _currentByDevice.TryGetValue(deviceId, out var snapshot) ? snapshot : null;

    public IReadOnlyDictionary<int, MonitoringSnapshot> GetAllCurrent() =>
        new Dictionary<int, MonitoringSnapshot>(_currentByDevice);

    public void SetActiveDevices(IReadOnlySet<int> deviceIds)
    {
        foreach (var deviceId in _currentByDevice.Keys.Where(id => !deviceIds.Contains(id)))
            _currentByDevice.TryRemove(deviceId, out _);
        foreach (var key in _previousCounters.Keys.Where(key => !deviceIds.Contains(key.DeviceId)))
            _previousCounters.TryRemove(key, out _);

        var defaultDeviceId = deviceIds.Count == 0 ? 0 : deviceIds.Min();
        Volatile.Write(ref _defaultDeviceId, defaultDeviceId);
        if (defaultDeviceId == 0)
            Volatile.Write(ref _current, DisconnectedSnapshot("No enabled FortiGate device was found."));
        else if (_currentByDevice.TryGetValue(defaultDeviceId, out var snapshot))
            Volatile.Write(ref _current, snapshot);
    }

    public void RemoveDevice(int deviceId)
    {
        _currentByDevice.TryRemove(deviceId, out _);
        foreach (var key in _previousCounters.Keys.Where(item => item.DeviceId == deviceId))
            _previousCounters.TryRemove(key, out _);
        if (Volatile.Read(ref _defaultDeviceId) == deviceId)
            SetActiveDevices(_currentByDevice.Keys.ToHashSet());
    }

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        var configurationError = GetConfigurationError();
        if (configurationError is not null)
        {
            Volatile.Write(ref _current, DisconnectedSnapshot(configurationError));
            _logger.LogWarning("SNMP poll skipped because the configuration is incomplete: {Reason}", configurationError);
            return;
        }

        try
        {
            var snapshot = await Task.Run(() => QueryDevice(0, _options.Host, _options.Username), cancellationToken);
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
            _logger.LogWarning("SNMP poll failed ({ExceptionType}).", exception.GetType().Name);
        }
    }

    public async Task PollAsync(int deviceId, string host, string username, string deviceName,
        CancellationToken cancellationToken = default)
    {
        var configurationError = GetSharedConfigurationError();
        if (configurationError is null && !IPAddress.TryParse(host, out _))
            configurationError = "The device IP address is invalid.";
        if (configurationError is null && string.IsNullOrWhiteSpace(username))
            configurationError = "SNMPv3 username is required.";

        if (configurationError is not null)
        {
            SetDeviceSnapshot(deviceId, DisconnectedSnapshot(deviceName, host, configurationError));
            _logger.LogWarning("FortiGate polling skipped: {DeviceName} ({Host}) - {Reason}",
                deviceName, host, configurationError);
            return;
        }

        try
        {
            var snapshot = await Task.Run(() => QueryDevice(deviceId, host, username), cancellationToken);
            SetDeviceSnapshot(deviceId, snapshot);
            _logger.LogInformation("FortiGate polling succeeded: {DeviceName} ({Host})", deviceName, host);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = GetSafeErrorMessage(exception);
            var previous = GetCurrent(deviceId) ?? DisconnectedSnapshot(deviceName, host, error);
            SetDeviceSnapshot(deviceId, previous with
            {
                DeviceName = deviceName,
                DeviceIp = host,
                Connected = false,
                ErrorMessage = error
            });
            _logger.LogWarning("FortiGate polling failed: {DeviceName} ({Host}) ({ExceptionType})",
                deviceName, host, exception.GetType().Name);
        }
    }

    private void SetDeviceSnapshot(int deviceId, MonitoringSnapshot snapshot)
    {
        _currentByDevice[deviceId] = snapshot;
        _logger.LogInformation("Snapshot stored for device {DeviceId}. Connected: {Connected}",
            deviceId, snapshot.Connected);
        var defaultDeviceId = Volatile.Read(ref _defaultDeviceId);
        if (defaultDeviceId == 0)
            defaultDeviceId = Interlocked.CompareExchange(ref _defaultDeviceId, deviceId, 0) == 0
                ? deviceId
                : Volatile.Read(ref _defaultDeviceId);
        if (deviceId == defaultDeviceId)
            Volatile.Write(ref _current, snapshot);
    }


    public async Task<SnmpConnectionTestResult> TestConnectionAsync(
        string host,
        string username,
        CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(host, out var ipAddress))
        {
            return new SnmpConnectionTestResult(
                false,
                host,
                null,
                "Invalid IP address.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return new SnmpConnectionTestResult(
                false,
                host,
                null,
                "SNMPv3 username is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.AuthPassword) ||
            string.IsNullOrWhiteSpace(_options.PrivacyPassword))
        {
            return new SnmpConnectionTestResult(
                false,
                host,
                null,
                "SNMPv3 authentication/privacy password is not configured.");
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var endpoint = new IPEndPoint(ipAddress, _options.Port);

#pragma warning disable CS0618
                var authentication =
                    new SHA1AuthenticationProvider(
                        new OctetString(_options.AuthPassword));
#pragma warning restore CS0618

                var privacy =
                    new AESPrivacyProvider(
                        new OctetString(_options.PrivacyPassword),
                        authentication);

                var variables = new List<Variable>
                {
                    new(new ObjectIdentifier(DescriptionOid))
                };

                var discovery =
                    Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);

                var report = discovery.GetResponse(
                    _options.TimeoutMilliseconds,
                    endpoint);

                var request = new GetRequestMessage(
                    VersionCode.V3,
                    Messenger.NextMessageId,
                    Messenger.NextRequestId,
                    new OctetString(username),
                    OctetString.Empty,
                    variables,
                    privacy,
                    Messenger.MaxMessageSize,
                    report);

                var response = request.GetResponse(
                    _options.TimeoutMilliseconds,
                    endpoint);

                if (response is ReportMessage)
                {
                    if (response.Pdu().Variables.Count == 0 ||
                        response.Pdu().Variables[0].Id != Messenger.NotInTimeWindow)
                    {
                        throw new SnmpException(
                            "FortiGate returned an SNMPv3 security report.");
                    }

                    request = new GetRequestMessage(
                        VersionCode.V3,
                        Messenger.NextMessageId,
                        Messenger.NextRequestId,
                        new OctetString(username),
                        OctetString.Empty,
                        variables,
                        privacy,
                        Messenger.MaxMessageSize,
                        response);

                    response = request.GetResponse(
                        _options.TimeoutMilliseconds,
                        endpoint);
                }

                if (response.Pdu().ErrorStatus.ToInt32() != 0)
                {
                    throw ErrorException.Create(
                        "SNMP response error",
                        endpoint.Address,
                        response);
                }

                var description =
                    response.Pdu().Variables.FirstOrDefault()?.Data.ToString();

                return new SnmpConnectionTestResult(
                    true,
                    host,
                    description,
                    "SNMP connection successful.");
            }, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "SNMP connection test failed: {Host} ({ExceptionType})",
                host,
                exception.GetType().Name);

            return new SnmpConnectionTestResult(
                false,
                host,
                null,
                GetSafeErrorMessage(exception));
        }
    }

    private MonitoringSnapshot QueryDevice(int deviceId, string host, string username)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(host), _options.Port);
#pragma warning disable CS0618 // The FortiGate user is configured with SHA1.
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
        var response = SendSystemRequest(endpoint, variables, privacy, report, username);
        var values = response.Pdu().Variables;
        if (values.Count != variables.Count)
            throw new SnmpException("FortiGate returned an incomplete system SNMP variable set.");

        var interfaceResult = QueryInterfaces(deviceId, endpoint, privacy, report, username);
        if (interfaceResult.Interfaces.Count == 0)
            throw new SnmpException("The FortiGate IF-MIB interface table could not be retrieved.");

        return new MonitoringSnapshot(
            CleanDeviceName(values[3].Data.ToString()), host, true,
            ParseInt(values[0]), ParseInt(values[1]), ParseInt(values[2]),
            interfaceResult.Interfaces, interfaceResult.CollectedAt,
            interfaceResult.HasPartialErrors ? "Some interface fields could not be retrieved through SNMP." : null);
    }

    private ISnmpMessage SendSystemRequest(IPEndPoint endpoint, IList<Variable> variables,
        IPrivacyProvider privacy, ISnmpMessage report, string username)
    {
        var request = CreateRequest(variables, privacy, report, username);
        var response = request.GetResponse(_options.TimeoutMilliseconds, endpoint);
        if (response is ReportMessage)
        {
            if (response.Pdu().Variables.Count == 0 || response.Pdu().Variables[0].Id != Messenger.NotInTimeWindow)
                throw new SnmpException("FortiGate returned an SNMPv3 security report.");
            request = CreateRequest(variables, privacy, response, username);
            response = request.GetResponse(_options.TimeoutMilliseconds, endpoint);
        }
        if (response.Pdu().ErrorStatus.ToInt32() != 0)
            throw ErrorException.Create("SNMP response error", endpoint.Address, response);
        return response;
    }

    private InterfaceQueryResult QueryInterfaces(int deviceId, IPEndPoint endpoint, IPrivacyProvider privacy,
        ISnmpMessage report, string username)
    {
        var columns = new Dictionary<string, IReadOnlyDictionary<int, Variable>>();
        var failedColumns = new HashSet<string>();
        foreach (var oid in new[] { IfNameOid, IfAliasOid, IfOperStatusOid, IfAdminStatusOid,
                     IfHighSpeedOid, IfHcInOctetsOid, IfHcOutOctetsOid })
        {
            try { columns[oid] = WalkColumn(endpoint, privacy, report, username, oid); }
            catch (Exception exception) when (exception is SnmpException or SocketException)
            {
                columns[oid] = new Dictionary<int, Variable>();
                failedColumns.Add(oid);
                _logger.LogWarning("IF-MIB column could not be retrieved: {Oid} ({ExceptionType}).", oid, exception.GetType().Name);
            }
        }

        var indexes = columns.Values.SelectMany(column => column.Keys).Distinct().Order().ToArray();
        var collectedAt = DateTimeOffset.UtcNow;
        var interfaces = new List<NetworkInterfaceSnapshot>(indexes.Length);
        foreach (var index in indexes)
            interfaces.Add(CreateInterfaceSnapshot(deviceId, index, columns, failedColumns, collectedAt));

        var activeIndexes = indexes.ToHashSet();
        foreach (var key in _previousCounters.Keys
                     .Where(key => key.DeviceId == deviceId && !activeIndexes.Contains(key.InterfaceIndex)).ToArray())
            _previousCounters.TryRemove(key, out _);

        return new InterfaceQueryResult(interfaces, collectedAt, failedColumns.Count > 0);
    }

    private IReadOnlyDictionary<int, Variable> WalkColumn(IPEndPoint endpoint, IPrivacyProvider privacy,
        ISnmpMessage report, string username, string rootOid)
    {
        var variables = new List<Variable>();
        Messenger.BulkWalk(VersionCode.V3, endpoint, new OctetString(username), OctetString.Empty,
            new ObjectIdentifier(rootOid), variables, _options.TimeoutMilliseconds, 20,
            WalkMode.WithinSubtree, privacy, report);
        return variables.Select(variable => (Index: GetIndex(variable.Id, rootOid), Variable: variable))
            .Where(item => item.Index.HasValue)
            .ToDictionary(item => item.Index!.Value, item => item.Variable);
    }

    private NetworkInterfaceSnapshot CreateInterfaceSnapshot(int deviceId, int index,
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

        if (string.IsNullOrWhiteSpace(name)) errors.Add("Interface name could not be retrieved");
        if (!inOctets.HasValue || !outOctets.HasValue) errors.Add("64-bit traffic counter could not be retrieved");
        if (failedColumns.Count > 0) errors.Add("One or more IF-MIB fields are missing");

        InterfaceRateResult rate;
        if (inOctets.HasValue && outOctets.HasValue)
        {
            var counterKey = (deviceId, index);
            _previousCounters.TryGetValue(counterKey, out var previous);
            rate = InterfaceRateCalculator.Calculate(inOctets.Value, outOctets.Value,
                previous?.InOctets, previous?.OutOctets,
                previous is null ? TimeSpan.Zero : collectedAt - previous.CollectedAt, speedMbps);
            _previousCounters[counterKey] = new CounterSample(inOctets.Value, outOctets.Value, collectedAt);
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

    private static GetRequestMessage CreateRequest(IList<Variable> variables, IPrivacyProvider privacy,
        ISnmpMessage report, string username) =>
        new(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
            new OctetString(username), OctetString.Empty, variables, privacy,
            Messenger.MaxMessageSize, report);

    private string? GetConfigurationError()
    {
        var sharedError = GetSharedConfigurationError();
        if (sharedError is not null) return sharedError;
        if (string.IsNullOrWhiteSpace(_options.Username)) return "SNMPv3 username is required.";
        if (!IPAddress.TryParse(_options.Host, out _)) return "The configured SNMP host is not a valid IP address.";
        return null;
    }

    private string? GetSharedConfigurationError()
    {
        if (string.IsNullOrWhiteSpace(_options.AuthPassword) || string.IsNullOrWhiteSpace(_options.PrivacyPassword))
            return "SNMPv3 credentials are incomplete. Configure Snmp:AuthPassword and Snmp:PrivacyPassword in User Secrets.";
        if (_options.Port is < 1 or > 65535) return "The configured SNMP port is invalid.";
        if (!string.Equals(_options.Version, "v3", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_options.SecurityLevel, "authPriv", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_options.AuthenticationProtocol, "SHA1", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_options.PrivacyProtocol, "AES128", StringComparison.OrdinalIgnoreCase))
            return "SNMP protocol settings must be v3/authPriv/SHA1/AES128.";
        return null;
    }

    private MonitoringSnapshot DisconnectedSnapshot(string error) =>
        new("FortiGate-ARM64-KVM", _options.Host, false, null, null, null, [], null, error);

    private static MonitoringSnapshot DisconnectedSnapshot(string deviceName, string host, string error) =>
        new(deviceName, host, false, null, null, null, [], null, error);

    private static int ParseInt(Variable variable)
    {
        if (!int.TryParse(variable.Data.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new SnmpException($"A non-numeric SNMP value was received for {variable.Id}.");
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

    private static string GetLinkStatus(int? status) => status switch { 1 => "Up", 2 => "Down", _ => "Unknown" };

    private static string GetInterfaceType(string name)
    {
        if (name.StartsWith("port", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("fortilink", StringComparison.OrdinalIgnoreCase)) return "Physical";
        if (name.EndsWith(".root", StringComparison.OrdinalIgnoreCase)) return "Virtual";
        return "Other";
    }

    private static string CleanDeviceName(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "FortiGate-ARM64-KVM";
        var firstLine = description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return firstLine.Length > 80 ? firstLine[..80] : firstLine;
    }

    private static string GetSafeErrorMessage(Exception exception) => exception switch
    {
        Lextm.SharpSnmpLib.Messaging.TimeoutException => "The FortiGate SNMP poll timed out.",
        SocketException => "The FortiGate SNMP endpoint could not be reached.",
        SnmpException => "FortiGate SNMPv3 data could not be retrieved or validated.",
        _ => "An unexpected error occurred while retrieving SNMP data."
    };

    private sealed record CounterSample(ulong InOctets, ulong OutOctets, DateTimeOffset CollectedAt);
    private sealed record InterfaceQueryResult(IReadOnlyList<NetworkInterfaceSnapshot> Interfaces,
        DateTimeOffset CollectedAt, bool HasPartialErrors);
}
