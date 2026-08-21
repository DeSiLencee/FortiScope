(() => {
    "use strict";

    const refreshIntervalMs = 2000;
    const maxHistoryPoints = 60;
    const thresholds = { cpu: 80, ram: 80, interfaceWarning: 60, interfaceCritical: 80 };
    const defaultAlertSettings = {
        cpuWarningPercent: 70, cpuCriticalPercent: 85,
        memoryWarningPercent: 75, memoryCriticalPercent: 90,
        interfaceUtilizationWarningPercent: 70, interfaceUtilizationCriticalPercent: 90,
        offlineTimeoutSeconds: 30, enabled: true
    };
    let currentAlertSettings = { ...defaultAlertSettings };
    const elements = {};
    const liveHistory = { system: [], network: [] };
    const chartHistory = { system: liveHistory.system, network: liveHistory.network };
    let refreshInProgress = false;
    let lastHistoryTimestamp = null;
    let activeInterfaceFilter = "physical";
    let latestInterfaces = [];
    let latestConnectionState = false;
    let selectedHistoryRange = "live";
    let selectedInterfaceIndex = null;
    let selectedDeviceId = null;
    let availableDevices = [];
    let availableDeviceSummaries = [];
    let fleetMonitoringAvailable = false;
    let monitoringRequestController = null;
    let monitoringRequestVersion = 0;
    let historyRequestVersion = 0;
    let deviceListRefreshCounter = 0;
    let alertHistoryRequestVersion = 0;
    let editingDeviceId = null;
    let deletingDeviceId = null;

    const chartDefinitions = {
        system: {
            canvasId: "systemChart", historyKey: "system", fixedMax: 100, unit: "%",
            series: [{ key: "cpu", color: "#4b82ff" }, { key: "ram", color: "#a574f5" }]
        },
        network: {
            canvasId: "networkChart", historyKey: "network", unit: " Mbps",
            series: [{ key: "incoming", color: "#2ac991" }, { key: "outgoing", color: "#f4a340" }]
        }
    };

    function cacheElements() {
        ["cpuValue", "ramValue", "sessionValue", "cpuBar", "ramBar", "cpuHint", "ramHint",
            "cpuCard", "ramCard", "busiestPort", "busiestTraffic", "portTableBody", "alertsList",
            "alertCount", "lastUpdated", "connectionMessage", "chartCpuValue", "chartRamValue",
            "chartIncomingValue", "chartOutgoingValue", "systemChartTime", "networkChartTime",
            "deviceName", "deviceIp", "deviceStatus", "interfaceDataStatus", "networkChartUnit",
            "historyInterfaceSelect", "systemChartEmpty", "networkChartEmpty", "openAddDeviceModal",
            "addDeviceModal", "closeAddDeviceModal", "cancelAddDevice", "addDeviceForm",
            "addDeviceSubmit", "deviceFormStatus", "deviceFormName", "deviceList", "deviceCount"]
            .concat(["overviewStatus", "overviewGrid", "fleetAlertCount", "fleetAlertsList",
                "openAlertSettings", "alertSettingsModal", "closeAlertSettings", "cancelAlertSettings",
                "alertSettingsForm", "alertSettingsStatus", "saveAlertSettings", "openEmailSettings",
                "emailSettingsModal", "closeEmailSettings", "cancelEmailSettings", "emailSettingsForm",
                "emailSettingsStatus", "emailSettingsState", "emailPasswordHint", "saveEmailSettings",
                "testEmailSettings", "alertHistoryBody", "alertHistoryDevice", "alertHistorySeverity",
                "alertHistoryEvent", "alertHistoryRange", "deviceManagementStatus", "editDeviceModal",
                "closeEditDeviceModal", "cancelEditDevice", "editDeviceForm", "editDeviceStatus",
                "saveEditDevice", "deleteDeviceModal", "closeDeleteDeviceModal", "cancelDeleteDevice",
                "confirmDeleteDevice", "deleteDeviceName", "deleteDeviceIp", "deleteDeviceStatus",
                "topInterfacesList"])
            .forEach(id => elements[id] = document.getElementById(id));
    }

    async function readApiResponse(response) {
        const contentType = response.headers.get("content-type") || "";
        const body = contentType.includes("application/json") ? await response.json() : await response.text();
        if (response.ok) return body;
        const message = typeof body === "object" && body
            ? body.error || body.message || body.title
            : body;
        throw new Error(message || `API isteği başarısız oldu (${response.status}).`);
    }

    function setDeviceFormStatus(message, type = "") {
        elements.deviceFormStatus.textContent = message;
        elements.deviceFormStatus.className = `device-form-status${type ? ` is-${type}` : ""}`;
    }

    function getUserFacingError(error, fallback) {
        if (error instanceof TypeError) return "Sunucuya ulaşılamadı. Ağ bağlantısını kontrol edip tekrar deneyin.";
        return error?.message || fallback;
    }

    function getMetricSeverity(value, warningThreshold, criticalThreshold) {
        if (!Number.isFinite(value)) return "normal";
        if (value >= criticalThreshold) return "critical";
        if (value >= warningThreshold) return "warning";
        return "normal";
    }

    function getCpuSeverity(value) {
        return getMetricSeverity(value, currentAlertSettings.cpuWarningPercent,
            currentAlertSettings.cpuCriticalPercent);
    }

    function getMemorySeverity(value) {
        return getMetricSeverity(value, currentAlertSettings.memoryWarningPercent,
            currentAlertSettings.memoryCriticalPercent);
    }

    function getInterfaceSeverity(item) {
        if (item.type !== "Fiziksel" || item.operStatus !== 1 || item.isMeasuring ||
            !Number.isFinite(item.speedMbps) || item.speedMbps <= 0 || !Number.isFinite(item.utilizationPercent))
            return "normal";
        return getMetricSeverity(item.utilizationPercent,
            currentAlertSettings.interfaceUtilizationWarningPercent,
            currentAlertSettings.interfaceUtilizationCriticalPercent);
    }

    function isDeviceOnline(device) {
        if (!device.enabled || device.connected !== true) return false;
        if (!device.lastUpdated) return true;
        const ageMilliseconds = Date.now() - new Date(device.lastUpdated).getTime();
        return !Number.isFinite(ageMilliseconds) || ageMilliseconds <= currentAlertSettings.offlineTimeoutSeconds * 1000;
    }

    function hasMeaningfulDownInterface(device) {
        return (device.interfaces || []).some(item =>
            item.type === "Fiziksel" && item.adminStatus === 1 && item.operStatus === 2 &&
            !String(item.name || "").toLowerCase().includes("fortilink"));
    }

    function getDeviceHealth(device) {
        if (!isDeviceOnline(device)) return "critical";
        if (!currentAlertSettings.enabled) return "normal";
        const severities = [getCpuSeverity(device.cpuUsage), getMemorySeverity(device.memoryUsage),
            ...(device.interfaces || []).map(getInterfaceSeverity)];
        if (severities.includes("critical")) return "critical";
        if (severities.includes("warning") || hasMeaningfulDownInterface(device)) return "warning";
        return "normal";
    }

    function buildFleetAlerts(devices) {
        const alerts = [];
        devices.forEach(device => {
            if (!device.enabled) return;
            if (!isDeviceOnline(device)) {
                alerts.push({ deviceId: device.id, severity: "critical", value: Number.MAX_SAFE_INTEGER,
                    name: device.name, ipAddress: device.ipAddress, message: "Device Offline" });
                return;
            }
            if (!currentAlertSettings.enabled) return;
            [["CPU", device.cpuUsage, getCpuSeverity(device.cpuUsage)],
                ["RAM", device.memoryUsage, getMemorySeverity(device.memoryUsage)]].forEach(([label, value, severity]) => {
                if (severity !== "normal") alerts.push({ deviceId: device.id, severity, value,
                    name: device.name, ipAddress: device.ipAddress, message: `${label} ${value}%` });
            });
            (device.interfaces || []).forEach(item => {
                const severity = getInterfaceSeverity(item);
                if (severity !== "normal") alerts.push({ deviceId: device.id, severity,
                    value: item.utilizationPercent, name: device.name, ipAddress: device.ipAddress,
                    message: `${item.name} Traffic ${formatPercent(item.utilizationPercent)}%` });
            });
            (device.interfaces || []).filter(item =>
                item.type === "Fiziksel" && item.adminStatus === 1 && item.operStatus === 2 &&
                !String(item.name || "").toLowerCase().includes("fortilink"))
                .forEach(item => alerts.push({ deviceId: device.id, severity: "warning", value: 0,
                    name: device.name, ipAddress: device.ipAddress, message: `${item.name} interface down` }));
        });
        const severityRank = { critical: 2, warning: 1 };
        return alerts.sort((a, b) => severityRank[b.severity] - severityRank[a.severity] || b.value - a.value);
    }

    function renderFleetOverview(devices, monitoringAvailable) {
        if (!monitoringAvailable) {
            elements.overviewStatus.textContent = "Monitoring unavailable";
            elements.overviewStatus.className = "is-unavailable";
            elements.overviewGrid.innerHTML = ["Total Devices", "Online", "Offline", "Warning", "Critical"]
                .map(label => `<article><span>${label}</span><strong>--</strong></article>`).join("");
            elements.fleetAlertCount.textContent = "--";
            elements.fleetAlertsList.innerHTML = '<p class="fleet-alerts-empty is-unavailable">Monitoring unavailable</p>';
            return;
        }

        const enabledDevices = devices.filter(device => device.enabled);
        const online = enabledDevices.filter(isDeviceOnline).length;
        const offline = enabledDevices.length - online;
        const health = enabledDevices.map(getDeviceHealth);
        const values = [
            ["Total Devices", devices.length, "total"], ["Online", online, "online"],
            ["Offline", offline, "offline"], ["Warning", health.filter(item => item === "warning").length, "warning"],
            ["Critical", health.filter(item => item === "critical").length, "critical"]
        ];
        elements.overviewStatus.textContent = "Live fleet status";
        elements.overviewStatus.className = "is-live";
        elements.overviewGrid.innerHTML = values.map(([label, value, type]) =>
            `<article class="is-${type}"><span>${label}</span><strong>${value}</strong></article>`).join("");

        const alerts = buildFleetAlerts(devices);
        elements.fleetAlertCount.textContent = alerts.length;
        elements.fleetAlertsList.innerHTML = alerts.length
            ? alerts.map(alert => `<button type="button" class="fleet-alert-item is-${alert.severity}" data-alert-device-id="${alert.deviceId}">
                <span class="fleet-alert-severity">${alert.severity}</span>
                <span class="fleet-alert-device"><strong>${escapeHtml(alert.name)}</strong><small>${escapeHtml(alert.ipAddress)}</small></span>
                <span class="fleet-alert-message">${escapeHtml(alert.message)}</span>
                <span class="fleet-alert-arrow" aria-hidden="true">›</span>
            </button>`).join("")
            : '<p class="fleet-alerts-empty"><span>✓</span>No active alerts</p>';
    }

    function renderFleetFromCache() {
        const fleetDevices = availableDevices.map(device => ({
            ...device,
            ...(availableDeviceSummaries.find(summary => summary.id === device.id) || {})
        }));
        renderFleetOverview(fleetDevices, fleetMonitoringAvailable);
        renderDevices(availableDevices, availableDeviceSummaries);
    }

    async function loadTopInterfaces() {
        try {
            const items = await readApiResponse(await fetch("/api/interfaces/top?limit=5", {
                headers: { "Accept": "application/json" }, cache: "no-store"
            }));
            elements.topInterfacesList.innerHTML = items.length ? items.map((item, index) =>
                `<button type="button" class="top-interface-item" data-top-interface-device="${item.deviceId}" data-top-interface-index="${item.interfaceIndex}">
                    <span class="top-interface-rank">${index + 1}</span>
                    <span class="top-interface-copy"><strong>${escapeHtml(item.deviceName)} / ${escapeHtml(item.interfaceName)}</strong><small>↓ ${escapeHtml(formatTraffic(item.incomingMbps))} · ↑ ${escapeHtml(formatTraffic(item.outgoingMbps))}</small></span>
                    <strong class="top-interface-utilization">${escapeHtml(formatPercent(item.utilizationPercent))}%</strong>
                    <span class="history-severity-badge is-${String(item.severity).toLowerCase()}">${escapeHtml(item.severity)}</span>
                </button>`).join("") : '<p class="fleet-alerts-empty">No measured physical interfaces available.</p>';
        } catch (error) {
            elements.topInterfacesList.innerHTML = `<p class="fleet-alerts-empty is-unavailable">${escapeHtml(getUserFacingError(error, "Top interfaces yüklenemedi."))}</p>`;
        }
    }

    function renderDevices(devices, summaries = availableDeviceSummaries) {
        const summariesById = new Map(summaries.map(summary => [summary.id, summary]));
        const healthRank = { critical: 2, warning: 1, normal: 0 };
        const orderedDevices = [...devices].sort((a, b) => {
            const aSummary = summariesById.get(a.id);
            const bSummary = summariesById.get(b.id);
            return healthRank[getDeviceHealth({ ...b, ...bSummary })] - healthRank[getDeviceHealth({ ...a, ...aSummary })];
        });
        elements.deviceCount.textContent = devices.length;
        elements.deviceList.innerHTML = orderedDevices.length
            ? orderedDevices.map(device => {
                const summary = summariesById.get(device.id);
                const online = isDeviceOnline({ ...device, ...summary });
                const waiting = device.enabled && (!summary || summary.errorMessage === "Waiting for first poll.");
                const disabled = !device.enabled;
                const statusLabel = disabled ? "Disabled" : online ? "Online" : waiting ? "Waiting" : "Offline";
                const statusClass = disabled ? "is-disabled" : online ? "is-online" : waiting ? "is-waiting" : "is-offline";
                const health = getDeviceHealth({ ...device, ...summary });
                const healthLabel = disabled ? "Disabled" : online ? (health === "normal" ? "Healthy" : health) : "Offline";
                return `<article class="registered-device${device.id === selectedDeviceId ? " is-selected" : ""}" data-device-id="${device.id}" role="button" tabindex="0" aria-pressed="${device.id === selectedDeviceId}">
                <span class="registered-device-header">
                    <span class="registered-device-identity">
                        <span class="registered-device-icon" aria-hidden="true">FG</span>
                        <span class="registered-device-copy"><strong>${escapeHtml(device.name)}</strong><span>${escapeHtml(device.ipAddress)} · SNMP ${escapeHtml(device.snmpVersion)}</span></span>
                    </span>
                    <span class="device-card-badges">
                        <button type="button" class="device-actions-toggle" data-device-actions-toggle="${device.id}" aria-label="${escapeHtml(device.name)} actions" aria-expanded="false">⋮</button>
                        <span class="device-status-badge ${statusClass}"><i></i>${statusLabel}</span>
                        <span class="device-health-badge is-${health}">${healthLabel}</span>
                    </span>
                </span>
                <span class="device-card-metrics">
                    <span><small>CPU</small><strong>${Number.isFinite(summary?.cpuUsage) ? `${summary.cpuUsage}%` : "-"}</strong></span>
                    <span><small>RAM</small><strong>${Number.isFinite(summary?.memoryUsage) ? `${summary.memoryUsage}%` : "-"}</strong></span>
                    <span><small>Sessions</small><strong>${Number.isFinite(summary?.sessionCount) ? Number(summary.sessionCount).toLocaleString("tr-TR") : "-"}</strong></span>
                </span>
                <span class="device-actions-menu" data-device-actions-menu="${device.id}" hidden>
                    <button type="button" data-device-action="edit">Edit</button>
                    <button type="button" data-device-action="test" ${disabled ? "disabled" : ""}>Test Connection</button>
                    <button type="button" data-device-action="toggle">${disabled ? "Enable" : "Disable"}</button>
                    <button type="button" class="is-danger" data-device-action="delete">Delete</button>
                </span>
            </article>`;
            }).join("")
            : `<div class="device-empty-state"><strong>No FortiGate devices configured.</strong>
                <p>Add your first FortiGate to start monitoring.</p>
                <button type="button" class="add-fortigate-button" data-empty-add-device><span aria-hidden="true">+</span> Add FortiGate</button>
            </div>`;
    }

    function updateAlertHistoryDeviceOptions() {
        const currentValue = elements.alertHistoryDevice.value;
        elements.alertHistoryDevice.innerHTML = '<option value="">All Devices</option>' +
            availableDevices.map(device => `<option value="${device.id}">${escapeHtml(device.name)}</option>`).join("");
        if (availableDevices.some(device => String(device.id) === currentValue))
            elements.alertHistoryDevice.value = currentValue;
    }

    function formatAlertMetric(item) {
        if (!Number.isFinite(item.metricValue)) return "-";
        return item.alertType === "CPU_HIGH" || item.alertType === "MEMORY_HIGH"
            ? `${formatPercent(item.metricValue)}%` : formatPercent(item.metricValue);
    }

    async function loadAlertHistory() {
        const requestVersion = ++alertHistoryRequestVersion;
        const params = new URLSearchParams({ range: elements.alertHistoryRange.value, limit: "100" });
        if (elements.alertHistoryDevice.value) params.set("deviceId", elements.alertHistoryDevice.value);
        if (elements.alertHistorySeverity.value) params.set("severity", elements.alertHistorySeverity.value);
        if (elements.alertHistoryEvent.value) params.set("eventType", elements.alertHistoryEvent.value);
        try {
            const response = await fetch(`/api/alerts/history?${params}`, {
                headers: { "Accept": "application/json" }, cache: "no-store"
            });
            const events = await readApiResponse(response);
            if (requestVersion !== alertHistoryRequestVersion) return;
            elements.alertHistoryBody.innerHTML = events.length ? events.map(item =>
                `<tr class="alert-history-row" data-history-device-id="${item.deviceId}" tabindex="0">
                    <td><time datetime="${escapeHtml(item.occurredAtUtc)}">${escapeHtml(new Date(item.occurredAtUtc).toLocaleString("tr-TR"))}</time></td>
                    <td><strong>${escapeHtml(item.deviceName)}</strong><small>${escapeHtml(item.deviceIp)}</small></td>
                    <td><span class="history-event-badge is-${String(item.eventType).toLowerCase()}">${escapeHtml(item.eventType)}</span></td>
                    <td>${escapeHtml(String(item.alertType).replaceAll("_", " "))}</td>
                    <td><span class="history-severity-badge is-${String(item.severity).toLowerCase()}">${escapeHtml(item.severity)}</span></td>
                    <td class="alert-history-value">${escapeHtml(formatAlertMetric(item))}</td>
                    <td class="alert-history-message">${escapeHtml(item.message)}</td>
                </tr>`).join("")
                : '<tr><td colspan="7" class="alert-history-empty">No alert events in the selected period.</td></tr>';
        } catch (error) {
            if (requestVersion !== alertHistoryRequestVersion) return;
            elements.alertHistoryBody.innerHTML = `<tr><td colspan="7" class="alert-history-empty is-error">${escapeHtml(getUserFacingError(error, "Alert history yüklenemedi."))}</td></tr>`;
        }
    }

    async function loadDevices(preferredDeviceId = null) {
        try {
            const options = { headers: { "Accept": "application/json" }, cache: "no-store" };
            const [devicesResponse, summariesResponse] = await Promise.all([
                fetch("/api/devices", options),
                fetch("/api/devices/monitoring/current", options).catch(() => null)
            ]);
            availableDevices = await readApiResponse(devicesResponse);
            const monitoringAvailable = summariesResponse?.ok === true;
            fleetMonitoringAvailable = monitoringAvailable;
            const summaries = monitoringAvailable ? await summariesResponse.json() : [];
            const details = monitoringAvailable ? await Promise.all(availableDevices.filter(device => device.enabled).map(async device => {
                try {
                    const response = await fetch(`/api/devices/${encodeURIComponent(device.id)}/monitoring/current`, options);
                    return response.ok ? { id: device.id, snapshot: await response.json() } : null;
                } catch { return null; }
            })) : [];
            const detailsById = new Map(details.filter(Boolean).map(item => [item.id, item.snapshot]));
            availableDeviceSummaries = summaries.map(summary => ({
                ...summary,
                interfaces: detailsById.get(summary.id)?.interfaces || [],
                lastUpdated: detailsById.get(summary.id)?.lastUpdated || null
            }));
            const requestedId = Number(preferredDeviceId);
            const currentStillExists = availableDevices.some(device => device.id === selectedDeviceId);
            const preferred = availableDevices.find(device => device.id === requestedId && device.enabled);
            const initial = availableDevices.find(device => device.enabled);

            if (preferred) selectedDeviceId = preferred.id;
            else if (!currentStillExists) selectedDeviceId = initial?.id ?? null;
            updateAlertHistoryDeviceOptions();
            renderFleetFromCache();
            return selectedDeviceId;
        } catch (error) {
            console.error("Cihaz listesi alınamadı.", error);
            fleetMonitoringAvailable = false;
            renderFleetOverview([], false);
            elements.deviceList.innerHTML = `<p class="device-list-message is-error">${escapeHtml(getUserFacingError(error, "Cihazlar yüklenemedi."))}</p>`;
            return selectedDeviceId;
        }
    }

    function resetDashboardForDevice() {
        monitoringRequestController?.abort();
        monitoringRequestController = null;
        monitoringRequestVersion++;
        refreshInProgress = false;
        lastHistoryTimestamp = null;
        selectedInterfaceIndex = null;
        latestInterfaces = [];
        latestConnectionState = false;
        liveHistory.system.length = 0;
        liveHistory.network.length = 0;
        chartHistory.system = liveHistory.system;
        chartHistory.network = liveHistory.network;
        updateMetric("cpu", Number.NaN, thresholds.cpu);
        updateMetric("ram", Number.NaN, thresholds.ram);
        elements.sessionValue.textContent = "--";
        elements.busiestPort.textContent = "--";
        elements.busiestTraffic.textContent = "--";
        elements.portTableBody.innerHTML = '<tr><td colspan="10" class="loading-cell">Cihaz verileri yükleniyor…</td></tr>';
        elements.historyInterfaceSelect.innerHTML = '<option value="">Interface seçin</option>';
        elements.chartCpuValue.textContent = "--%";
        elements.chartRamValue.textContent = "--%";
        elements.chartIncomingValue.textContent = "--";
        elements.chartOutgoingValue.textContent = "--";
        elements.systemChartTime.textContent = "--:--:--";
        elements.networkChartTime.textContent = "--:--:--";
        elements.lastUpdated.textContent = "--:--:--";
        const selected = availableDevices.find(device => device.id === selectedDeviceId);
        elements.connectionMessage.textContent = !selectedDeviceId ? "No FortiGate selected" :
            selected?.enabled === false ? "Device is disabled." : "Cihaz verileri yükleniyor…";
        elements.deviceName.textContent = selected?.name ?? "No FortiGate selected";
        elements.deviceIp.textContent = selected?.ipAddress ?? "--";
        elements.deviceStatus.innerHTML = `<i></i> ${!selectedDeviceId ? "Cihaz seçilmedi" : selected?.enabled === false ? "Disabled" : "Bağlantı bekleniyor"}`;
        elements.deviceStatus.classList.add("is-disconnected");
        updateAlerts([]);
        drawAllCharts();
    }

    async function selectDevice(deviceId) {
        const nextId = Number(deviceId);
        if (!availableDevices.some(device => device.id === nextId) || nextId === selectedDeviceId) return;
        selectedDeviceId = nextId;
        renderDevices(availableDevices);
        resetDashboardForDevice();
        await refreshDashboard();
        if (selectedHistoryRange !== "live") await selectHistoryRange(selectedHistoryRange);
    }

    function setManagementStatus(message, type = "") {
        elements.deviceManagementStatus.textContent = message;
        elements.deviceManagementStatus.className = `device-management-status${type ? ` is-${type}` : ""}`;
    }

    function deviceRequest(device, enabled = device.enabled) {
        return { name: device.name, ipAddress: device.ipAddress, snmpVersion: device.snmpVersion,
            snmpUsername: device.snmpUsername, authProtocol: device.authProtocol,
            privacyProtocol: device.privacyProtocol, enabled };
    }

    function closeDeviceMenus() {
        document.querySelectorAll("[data-device-actions-menu]").forEach(menu => menu.hidden = true);
        document.querySelectorAll("[data-device-actions-toggle]").forEach(button => button.setAttribute("aria-expanded", "false"));
    }

    function setModalStatus(element, message, type = "") {
        element.textContent = message;
        element.className = `device-form-status${type ? ` is-${type}` : ""}`;
    }

    function openEditDevice(deviceId) {
        const device = availableDevices.find(item => item.id === Number(deviceId));
        if (!device) return;
        editingDeviceId = device.id;
        Object.entries(deviceRequest(device)).forEach(([name, value]) => {
            const field = elements.editDeviceForm.elements.namedItem(name);
            if (!field) return;
            if (field.type === "checkbox") field.checked = Boolean(value);
            else field.value = value ?? "";
        });
        setModalStatus(elements.editDeviceStatus, "");
        elements.editDeviceModal.hidden = false;
        syncModalBodyState();
        window.setTimeout(() => elements.editDeviceForm.elements.namedItem("name").focus(), 0);
    }

    function closeEditDevice() {
        elements.editDeviceModal.hidden = true;
        editingDeviceId = null;
        syncModalBodyState();
    }

    async function saveEditedDevice(event) {
        event.preventDefault();
        if (!editingDeviceId || !elements.editDeviceForm.reportValidity()) return;
        const data = new FormData(elements.editDeviceForm);
        const request = { name: String(data.get("name") || "").trim(), ipAddress: String(data.get("ipAddress") || "").trim(),
            snmpVersion: String(data.get("snmpVersion")), snmpUsername: String(data.get("snmpUsername") || "").trim(),
            authProtocol: String(data.get("authProtocol")), privacyProtocol: String(data.get("privacyProtocol")),
            enabled: data.get("enabled") === "on" };
        const id = editingDeviceId;
        elements.saveEditDevice.disabled = true;
        elements.saveEditDevice.textContent = "Saving...";
        setModalStatus(elements.editDeviceStatus, "Cihaz güncelleniyor…");
        try {
            const response = await fetch(`/api/devices/${encodeURIComponent(id)}`, { method: "PUT",
                headers: { "Accept": "application/json", "Content-Type": "application/json" }, body: JSON.stringify(request) });
            await readApiResponse(response);
            closeEditDevice();
            await loadDevices(request.enabled ? id : null);
            if (selectedDeviceId === id) { resetDashboardForDevice(); await refreshDashboard(); }
            setManagementStatus("Device updated successfully.", "success");
        } catch (error) {
            setModalStatus(elements.editDeviceStatus, getUserFacingError(error, "Cihaz güncellenemedi."), "error");
        } finally {
            elements.saveEditDevice.disabled = false;
            elements.saveEditDevice.textContent = "Save Changes";
        }
    }

    async function testManagedDevice(deviceId) {
        setManagementStatus("Testing connection…");
        try {
            const result = await readApiResponse(await fetch(`/api/devices/${encodeURIComponent(deviceId)}/test`,
                { method: "POST", headers: { "Accept": "application/json" } }));
            setManagementStatus(`Connection successful${result.deviceDescription ? ` — ${result.deviceDescription}` : ""}`, "success");
        } catch (error) { setManagementStatus(getUserFacingError(error, "Connection test failed."), "error"); }
    }

    async function toggleManagedDevice(deviceId) {
        const device = availableDevices.find(item => item.id === Number(deviceId));
        if (!device) return;
        setManagementStatus(`${device.enabled ? "Disabling" : "Enabling"} device…`);
        try {
            await readApiResponse(await fetch(`/api/devices/${encodeURIComponent(device.id)}`, { method: "PUT",
                headers: { "Accept": "application/json", "Content-Type": "application/json" },
                body: JSON.stringify(deviceRequest(device, !device.enabled)) }));
            await loadDevices(!device.enabled ? device.id : null);
            if (selectedDeviceId === device.id && device.enabled) { resetDashboardForDevice(); await refreshDashboard(); }
            setManagementStatus(`Device ${device.enabled ? "disabled" : "enabled"} successfully.`, "success");
        } catch (error) { setManagementStatus(getUserFacingError(error, "Device state değiştirilemedi."), "error"); }
    }

    function openDeleteDevice(deviceId) {
        const device = availableDevices.find(item => item.id === Number(deviceId));
        if (!device) return;
        deletingDeviceId = device.id;
        elements.deleteDeviceName.textContent = device.name;
        elements.deleteDeviceIp.textContent = device.ipAddress;
        setModalStatus(elements.deleteDeviceStatus, "");
        elements.deleteDeviceModal.hidden = false;
        syncModalBodyState();
        window.setTimeout(() => elements.cancelDeleteDevice.focus(), 0);
    }

    function closeDeleteDevice() {
        elements.deleteDeviceModal.hidden = true;
        deletingDeviceId = null;
        syncModalBodyState();
    }

    async function deleteManagedDevice() {
        if (!deletingDeviceId) return;
        const id = deletingDeviceId;
        const wasSelected = selectedDeviceId === id;
        elements.confirmDeleteDevice.disabled = true;
        elements.confirmDeleteDevice.textContent = "Deleting...";
        try {
            await readApiResponse(await fetch(`/api/devices/${encodeURIComponent(id)}`, { method: "DELETE", headers: { "Accept": "application/json" } }));
            closeDeleteDevice();
            await loadDevices();
            if (wasSelected || !selectedDeviceId) { resetDashboardForDevice(); await refreshDashboard(); }
            await loadAlertHistory();
            setManagementStatus("Device deleted successfully. Historical data was retained.", "success");
        } catch (error) { setModalStatus(elements.deleteDeviceStatus, getUserFacingError(error, "Cihaz silinemedi."), "error"); }
        finally { elements.confirmDeleteDevice.disabled = false; elements.confirmDeleteDevice.textContent = "Delete Device"; }
    }

    function initializeAlertHistory() {
        [elements.alertHistoryDevice, elements.alertHistorySeverity, elements.alertHistoryEvent,
            elements.alertHistoryRange].forEach(select => select.addEventListener("change", loadAlertHistory));
        const chooseHistoryDevice = event => {
            const row = event.target.closest("[data-history-device-id]");
            if (row) selectDevice(row.dataset.historyDeviceId);
        };
        elements.alertHistoryBody.addEventListener("click", chooseHistoryDevice);
        elements.alertHistoryBody.addEventListener("keydown", event => {
            if (event.key === "Enter" || event.key === " ") chooseHistoryDevice(event);
        });
    }

    function setAlertSettingsStatus(message, type = "") {
        elements.alertSettingsStatus.textContent = message;
        elements.alertSettingsStatus.className = `device-form-status${type ? ` is-${type}` : ""}`;
    }

    function populateAlertSettingsForm(settings) {
        Object.entries(settings).forEach(([name, value]) => {
            const field = elements.alertSettingsForm.elements.namedItem(name);
            if (!field) return;
            if (field.type === "checkbox") field.checked = Boolean(value);
            else field.value = value;
        });
    }

    async function loadAlertSettings(populateForm = false) {
        try {
            const response = await fetch("/api/settings/alerts", {
                headers: { "Accept": "application/json" }, cache: "no-store"
            });
            currentAlertSettings = { ...defaultAlertSettings, ...await readApiResponse(response) };
            if (populateForm) populateAlertSettingsForm(currentAlertSettings);
            return true;
        } catch (error) {
            console.error("Alert settings alınamadı.", error);
            if (populateForm) setAlertSettingsStatus(getUserFacingError(error, "Alert settings yüklenemedi."), "error");
            return false;
        }
    }

    function syncModalBodyState() {
        const modalOpen = !elements.addDeviceModal.hidden || !elements.alertSettingsModal.hidden ||
            !elements.emailSettingsModal.hidden || !elements.editDeviceModal.hidden ||
            !elements.deleteDeviceModal.hidden;
        document.body.classList.toggle("modal-open", modalOpen);
    }

    async function openAlertSettingsModal() {
        closeDeviceModal();
        closeEmailSettingsModal();
        elements.alertSettingsModal.hidden = false;
        syncModalBodyState();
        setAlertSettingsStatus("Settings yükleniyor…");
        elements.saveAlertSettings.disabled = true;
        const loaded = await loadAlertSettings(true);
        elements.saveAlertSettings.disabled = false;
        if (loaded && !elements.alertSettingsModal.hidden) {
            setAlertSettingsStatus("");
            elements.alertSettingsForm.elements.namedItem("cpuWarningPercent").focus();
        }
    }

    function closeAlertSettingsModal() {
        elements.alertSettingsModal.hidden = true;
        syncModalBodyState();
    }

    function setEmailSettingsStatus(message, type = "") {
        elements.emailSettingsStatus.textContent = message;
        elements.emailSettingsStatus.className = `device-form-status${type ? ` is-${type}` : ""}`;
    }

    function populateEmailSettingsForm(settings) {
        Object.entries(settings).forEach(([name, value]) => {
            const field = elements.emailSettingsForm.elements.namedItem(name);
            if (!field || name === "hasPassword") return;
            if (field.type === "checkbox") field.checked = Boolean(value);
            else field.value = value ?? "";
        });
        elements.emailSettingsForm.elements.namedItem("password").value = "";
        elements.emailPasswordHint.textContent = settings.hasPassword ? "A password is securely stored." : "No password is stored.";
        elements.emailSettingsState.textContent = settings.enabled ? "Enabled" : "Disabled";
        elements.emailSettingsState.className = settings.enabled ? "is-enabled" : "is-disabled";
    }

    async function openEmailSettingsModal() {
        closeDeviceModal();
        closeAlertSettingsModal();
        elements.emailSettingsModal.hidden = false;
        syncModalBodyState();
        setEmailSettingsStatus("Email settings yükleniyor…");
        elements.saveEmailSettings.disabled = true;
        try {
            const response = await fetch("/api/settings/email", {
                headers: { "Accept": "application/json" }, cache: "no-store"
            });
            const settings = await readApiResponse(response);
            if (elements.emailSettingsModal.hidden) return;
            populateEmailSettingsForm(settings);
            setEmailSettingsStatus("");
            elements.emailSettingsForm.elements.namedItem("smtpHost").focus();
        } catch (error) {
            setEmailSettingsStatus(getUserFacingError(error, "Email settings yüklenemedi."), "error");
        } finally { elements.saveEmailSettings.disabled = false; }
    }

    function closeEmailSettingsModal() {
        elements.emailSettingsModal.hidden = true;
        syncModalBodyState();
    }

    function getEmailSettingsRequest() {
        const data = new FormData(elements.emailSettingsForm);
        return {
            enabled: data.get("enabled") === "on",
            smtpHost: String(data.get("smtpHost") || "").trim(),
            smtpPort: Number(data.get("smtpPort")),
            useSsl: data.get("useSsl") === "on",
            username: String(data.get("username") || "").trim(),
            password: String(data.get("password") || ""),
            fromAddress: String(data.get("fromAddress") || "").trim(),
            toAddress: String(data.get("toAddress") || "").trim(),
            sendWarningAlerts: data.get("sendWarningAlerts") === "on",
            sendCriticalAlerts: data.get("sendCriticalAlerts") === "on",
            sendRecoveryNotifications: data.get("sendRecoveryNotifications") === "on",
            cooldownMinutes: Number(data.get("cooldownMinutes"))
        };
    }

    async function submitEmailSettings(event) {
        event.preventDefault();
        if (!elements.emailSettingsForm.reportValidity()) return;
        elements.saveEmailSettings.disabled = true;
        elements.saveEmailSettings.textContent = "Saving...";
        setEmailSettingsStatus("Email settings kaydediliyor…");
        try {
            const response = await fetch("/api/settings/email", {
                method: "PUT", headers: { "Accept": "application/json", "Content-Type": "application/json" },
                body: JSON.stringify(getEmailSettingsRequest())
            });
            const settings = await readApiResponse(response);
            populateEmailSettingsForm(settings);
            setEmailSettingsStatus("Email settings saved successfully.", "success");
        } catch (error) {
            setEmailSettingsStatus(getUserFacingError(error, "Email settings kaydedilemedi."), "error");
        } finally {
            elements.saveEmailSettings.disabled = false;
            elements.saveEmailSettings.textContent = "Save Settings";
        }
    }

    async function sendTestEmail() {
        elements.testEmailSettings.disabled = true;
        elements.testEmailSettings.textContent = "Sending...";
        setEmailSettingsStatus("Test email gönderiliyor…");
        try {
            const response = await fetch("/api/settings/email/test", {
                method: "POST", headers: { "Accept": "application/json" }
            });
            const result = await readApiResponse(response);
            setEmailSettingsStatus(result.message || "Test email sent successfully.", "success");
        } catch (error) {
            setEmailSettingsStatus(getUserFacingError(error, "Test email gönderilemedi."), "error");
        } finally {
            elements.testEmailSettings.disabled = false;
            elements.testEmailSettings.textContent = "Test Email";
        }
    }

    async function submitAlertSettings(event) {
        event.preventDefault();
        if (!elements.alertSettingsForm.reportValidity()) return;
        const formData = new FormData(elements.alertSettingsForm);
        const request = {
            cpuWarningPercent: Number(formData.get("cpuWarningPercent")),
            cpuCriticalPercent: Number(formData.get("cpuCriticalPercent")),
            memoryWarningPercent: Number(formData.get("memoryWarningPercent")),
            memoryCriticalPercent: Number(formData.get("memoryCriticalPercent")),
            interfaceUtilizationWarningPercent: Number(formData.get("interfaceUtilizationWarningPercent")),
            interfaceUtilizationCriticalPercent: Number(formData.get("interfaceUtilizationCriticalPercent")),
            offlineTimeoutSeconds: Number(formData.get("offlineTimeoutSeconds")),
            enabled: formData.get("enabled") === "on"
        };
        elements.saveAlertSettings.disabled = true;
        elements.saveAlertSettings.textContent = "Saving...";
        setAlertSettingsStatus("Alert settings kaydediliyor…");
        try {
            const response = await fetch("/api/settings/alerts", {
                method: "PUT",
                headers: { "Accept": "application/json", "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            currentAlertSettings = { ...defaultAlertSettings, ...await readApiResponse(response) };
            renderFleetFromCache();
            setAlertSettingsStatus("Alert settings saved successfully.", "success");
        } catch (error) {
            setAlertSettingsStatus(getUserFacingError(error, "Alert settings kaydedilemedi."), "error");
        } finally {
            elements.saveAlertSettings.disabled = false;
            elements.saveAlertSettings.textContent = "Save Settings";
        }
    }

    function openDeviceModal() {
        closeAlertSettingsModal();
        closeEmailSettingsModal();
        closeEditDevice();
        closeDeleteDevice();
        elements.addDeviceModal.hidden = false;
        syncModalBodyState();
        setDeviceFormStatus("");
        window.setTimeout(() => elements.deviceFormName.focus(), 0);
    }

    function closeDeviceModal() {
        elements.addDeviceModal.hidden = true;
        syncModalBodyState();
    }

    async function submitDevice(event) {
        event.preventDefault();
        if (!elements.addDeviceForm.reportValidity()) return;

        const formData = new FormData(elements.addDeviceForm);
        const request = {
            name: String(formData.get("name") || "").trim(),
            ipAddress: String(formData.get("ipAddress") || "").trim(),
            snmpVersion: String(formData.get("snmpVersion")),
            snmpUsername: String(formData.get("snmpUsername") || "").trim(),
            authProtocol: String(formData.get("authProtocol")),
            privacyProtocol: String(formData.get("privacyProtocol")),
            enabled: formData.get("enabled") === "on"
        };

        elements.addDeviceSubmit.disabled = true;
        elements.addDeviceSubmit.textContent = "Adding...";
        setDeviceFormStatus("Cihaz kaydediliyor…");
        let created = false;

        try {
            const createResponse = await fetch("/api/devices", {
                method: "POST",
                headers: { "Accept": "application/json", "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            const device = await readApiResponse(createResponse);
            created = true;
            await loadDevices(device.id);
            if (device.enabled) {
                resetDashboardForDevice();
                await refreshDashboard();
            }

            elements.addDeviceSubmit.textContent = "Testing...";
            setDeviceFormStatus("SNMP bağlantısı test ediliyor…");
            const testResponse = await fetch(`/api/devices/${encodeURIComponent(device.id)}/test`, {
                method: "POST", headers: { "Accept": "application/json" }
            });
            const result = await readApiResponse(testResponse);
            const description = result.deviceDescription ? ` — ${result.deviceDescription}` : "";
            setDeviceFormStatus(`Connection successful${description}`, "success");
            elements.addDeviceForm.reset();
        } catch (error) {
            setDeviceFormStatus(getUserFacingError(error, "İşlem tamamlanamadı. Lütfen tekrar deneyin."), "error");
            if (created) await loadDevices();
        } finally {
            elements.addDeviceSubmit.disabled = false;
            elements.addDeviceSubmit.textContent = "Add Device";
        }
    }

    function initializeDeviceManagement() {
        // Render the overlay directly under body so layout containers can never affect fixed positioning.
        document.body.appendChild(elements.addDeviceModal);
        document.body.appendChild(elements.alertSettingsModal);
        document.body.appendChild(elements.emailSettingsModal);
        document.body.appendChild(elements.editDeviceModal);
        document.body.appendChild(elements.deleteDeviceModal);
        elements.openAddDeviceModal.addEventListener("click", openDeviceModal);
        elements.closeAddDeviceModal.addEventListener("click", closeDeviceModal);
        elements.cancelAddDevice.addEventListener("click", closeDeviceModal);
        elements.addDeviceModal.addEventListener("click", event => {
            if (event.target === elements.addDeviceModal) closeDeviceModal();
        });
        elements.addDeviceForm.addEventListener("submit", submitDevice);
        elements.closeEditDeviceModal.addEventListener("click", closeEditDevice);
        elements.cancelEditDevice.addEventListener("click", closeEditDevice);
        elements.editDeviceForm.addEventListener("submit", saveEditedDevice);
        elements.editDeviceModal.addEventListener("click", event => {
            if (event.target === elements.editDeviceModal) closeEditDevice();
        });
        elements.closeDeleteDeviceModal.addEventListener("click", closeDeleteDevice);
        elements.cancelDeleteDevice.addEventListener("click", closeDeleteDevice);
        elements.confirmDeleteDevice.addEventListener("click", deleteManagedDevice);
        elements.deleteDeviceModal.addEventListener("click", event => {
            if (event.target === elements.deleteDeviceModal) closeDeleteDevice();
        });
        elements.openAlertSettings.addEventListener("click", openAlertSettingsModal);
        elements.closeAlertSettings.addEventListener("click", closeAlertSettingsModal);
        elements.cancelAlertSettings.addEventListener("click", closeAlertSettingsModal);
        elements.alertSettingsForm.addEventListener("submit", submitAlertSettings);
        elements.alertSettingsModal.addEventListener("click", event => {
            if (event.target === elements.alertSettingsModal) closeAlertSettingsModal();
        });
        elements.openEmailSettings.addEventListener("click", openEmailSettingsModal);
        elements.closeEmailSettings.addEventListener("click", closeEmailSettingsModal);
        elements.cancelEmailSettings.addEventListener("click", closeEmailSettingsModal);
        elements.emailSettingsForm.addEventListener("submit", submitEmailSettings);
        elements.testEmailSettings.addEventListener("click", sendTestEmail);
        elements.emailSettingsForm.elements.namedItem("enabled").addEventListener("change", event => {
            elements.emailSettingsState.textContent = event.target.checked ? "Enabled" : "Disabled";
            elements.emailSettingsState.className = event.target.checked ? "is-enabled" : "is-disabled";
        });
        elements.emailSettingsModal.addEventListener("click", event => {
            if (event.target === elements.emailSettingsModal) closeEmailSettingsModal();
        });
        elements.deviceList.addEventListener("click", event => {
            if (event.target.closest("[data-empty-add-device]")) { openDeviceModal(); return; }
            const toggle = event.target.closest("[data-device-actions-toggle]");
            if (toggle) {
                const menu = elements.deviceList.querySelector(`[data-device-actions-menu="${toggle.dataset.deviceActionsToggle}"]`);
                const willOpen = menu.hidden;
                closeDeviceMenus();
                menu.hidden = !willOpen;
                toggle.setAttribute("aria-expanded", String(willOpen));
                return;
            }
            const action = event.target.closest("[data-device-action]");
            if (action) {
                const card = action.closest("[data-device-id]");
                const id = card.dataset.deviceId;
                closeDeviceMenus();
                if (action.dataset.deviceAction === "edit") openEditDevice(id);
                else if (action.dataset.deviceAction === "test") testManagedDevice(id);
                else if (action.dataset.deviceAction === "toggle") toggleManagedDevice(id);
                else if (action.dataset.deviceAction === "delete") openDeleteDevice(id);
                return;
            }
            const card = event.target.closest("[data-device-id]");
            if (card) selectDevice(card.dataset.deviceId);
        });
        elements.deviceList.addEventListener("keydown", event => {
            if ((event.key === "Enter" || event.key === " ") && event.target.matches(".registered-device")) {
                event.preventDefault();
                selectDevice(event.target.dataset.deviceId);
            }
        });
        document.addEventListener("click", event => {
            if (!event.target.closest(".device-card-badges")) closeDeviceMenus();
        });
        elements.fleetAlertsList.addEventListener("click", event => {
            const alert = event.target.closest("[data-alert-device-id]");
            if (alert) selectDevice(alert.dataset.alertDeviceId);
        });
        elements.topInterfacesList.addEventListener("click", async event => {
            const item = event.target.closest("[data-top-interface-device]");
            if (!item) return;
            await selectDevice(item.dataset.topInterfaceDevice);
            selectedInterfaceIndex = Number(item.dataset.topInterfaceIndex) || null;
            if (selectedInterfaceIndex) elements.historyInterfaceSelect.value = String(selectedInterfaceIndex);
        });
        document.addEventListener("keydown", event => {
            if (event.key !== "Escape") return;
            if (!elements.emailSettingsModal.hidden) closeEmailSettingsModal();
            else if (!elements.alertSettingsModal.hidden) closeAlertSettingsModal();
            else if (!elements.deleteDeviceModal.hidden) closeDeleteDevice();
            else if (!elements.editDeviceModal.hidden) closeEditDevice();
            else if (!elements.addDeviceModal.hidden) closeDeviceModal();
        });
    }

    async function getMonitoringData(deviceId, signal) {
        if (!deviceId) throw new Error("No FortiGate selected");
        const requestOptions = { headers: { "Accept": "application/json" }, cache: "no-store" };
        requestOptions.signal = signal;
        const response = await fetch(`/api/devices/${encodeURIComponent(deviceId)}/monitoring/current`, requestOptions);
        return readApiResponse(response);
    }

    function sortInterfacesByTraffic(interfaces) {
        const rank = { critical: 2, warning: 1, normal: 0 };
        return [...interfaces].sort((a, b) => rank[getInterfaceSeverity(b)] - rank[getInterfaceSeverity(a)] ||
            (Number(b.utilizationPercent) || 0) - (Number(a.utilizationPercent) || 0));
    }

    function checkAlarms(data) {
        const alarms = [];
        if (!data.connected) alarms.push({ title: "FortiGate bağlantısı kesildi", detail: data.errorMessage || "SNMP verisi alınamıyor." });
        if (data.cpuUsage >= thresholds.cpu) alarms.push({ title: "Yüksek CPU kullanımı", detail: `CPU kullanımı %${data.cpuUsage} seviyesine ulaştı.` });
        if (data.memoryUsage >= thresholds.ram) alarms.push({ title: "Yüksek RAM kullanımı", detail: `RAM kullanımı %${data.memoryUsage} seviyesine ulaştı.` });
        data.interfaces.filter(item => getInterfaceSeverity(item) !== "normal").forEach(item => {
            const critical = getInterfaceSeverity(item) === "critical";
            alarms.push({
                level: critical ? "critical" : "warning",
                title: `${item.name} ${critical ? "kritik kullanım" : "yüksek kullanım"}`,
                detail: `Interface kullanımı %${formatPercent(item.utilizationPercent)} seviyesinde.`
            });
        });
        data.interfaces.filter(item => item.errorMessage).forEach(item =>
            alarms.push({ level: "warning", title: `${item.name} verisi eksik`, detail: item.errorMessage }));
        return alarms;
    }

    function formatTraffic(value) {
        const mbps = Number(value);
        if (!Number.isFinite(mbps) || mbps === 0) return "0 bps";
        if (mbps >= 1) return `${mbps.toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Mbps`;
        const kbps = mbps * 1000;
        if (kbps >= 1) return `${kbps.toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kbps`;
        return `${Math.round(mbps * 1_000_000).toLocaleString("tr-TR")} bps`;
    }
    function formatPercent(value) { return Number(value).toLocaleString("tr-TR", { minimumFractionDigits: 0, maximumFractionDigits: 1 }); }
    function formatTime(value) { return new Date(value).toLocaleTimeString("tr-TR"); }
    function escapeHtml(value) {
        return String(value ?? "").replace(/[&<>'"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[character]);
    }

    function updateMetric(name, value, threshold) {
        const available = Number.isFinite(value);
        elements[`${name}Value`].textContent = available ? value : "--";
        elements[`${name}Bar`].style.width = available ? `${value}%` : "0%";
        elements[`${name}Hint`].textContent = !available ? "SNMP verisi alınamadı" : value >= threshold ? "Eşik değeri aşıldı" : "Normal çalışma aralığı";
        elements[`${name}Card`].classList.toggle("is-alert", available && value >= threshold);
        elements[`${name}Card`].classList.toggle("is-unavailable", !available);
    }

    function updatePortTable(interfaces, connected) {
        if (!interfaces.length) {
            elements.portTableBody.innerHTML = `<tr><td colspan="10" class="loading-cell">${connected ? "Bu filtreye uygun interface bulunamadı." : "SNMP interface verisi alınamadı."}</td></tr>`;
            return;
        }
        elements.portTableBody.innerHTML = interfaces.map(item => {
            const statusClass = item.operStatus === 1 ? "status-up" : item.operStatus === 2 ? "status-down" : "status-unknown";
            const traffic = item.isMeasuring ? "Ölçülüyor" : formatTraffic(item.totalMbps);
            const utilization = item.utilizationPercent == null ? "--" : `%${formatPercent(item.utilizationPercent)}`;
            const typeClass = item.type === "Fiziksel" ? "type-physical" : item.type === "Sanal" ? "type-virtual" : "";
            const trafficSeverity = getInterfaceSeverity(item);
            const trafficBadge = trafficSeverity === "normal" ? "" :
                `<span class="interface-traffic-badge is-${trafficSeverity}">${trafficSeverity}</span>`;
            return `<tr title="${escapeHtml(item.errorMessage)}">
                <td class="traffic-number">${item.index}</td>
                <td><button type="button" class="port-name-button" data-history-interface="${item.index}">${escapeHtml(item.name)}</button>${trafficBadge}</td>
                <td><span class="interface-type ${typeClass}">${escapeHtml(item.type)}</span></td>
                <td>${escapeHtml(item.alias) || "--"}</td>
                <td>${item.adminStatus === 1 ? "Açık" : item.adminStatus === 2 ? "Kapalı" : "Bilinmiyor"}</td>
                <td><span class="${statusClass}"><i></i>${escapeHtml(item.linkStatus)}</span></td>
                <td>${item.speedMbps == null ? "--" : `${Number(item.speedMbps).toLocaleString("tr-TR")} Mbps`}</td>
                <td class="traffic-number">${item.isMeasuring ? "Ölçülüyor" : `↓ ${formatTraffic(item.incomingMbps)}`}</td>
                <td class="traffic-number">${item.isMeasuring ? "Ölçülüyor" : `↑ ${formatTraffic(item.outgoingMbps)}`}</td>
                <td class="total-traffic">${traffic}<small class="utilization">${utilization}</small></td>
            </tr>`;
        }).join("");
    }

    function filterInterfaces(interfaces) {
        return interfaces.filter(item => {
            if (activeInterfaceFilter === "physical") return item.type === "Fiziksel";
            if (activeInterfaceFilter === "virtual") return item.type === "Sanal";
            if (activeInterfaceFilter === "up") return item.operStatus === 1;
            if (activeInterfaceFilter === "down") return item.operStatus === 2;
            return true;
        });
    }

    function renderFilteredInterfaces() {
        updatePortTable(filterInterfaces(latestInterfaces), latestConnectionState);
    }

    function updateHistoryInterfaceOptions(interfaces) {
        const currentValue = selectedInterfaceIndex?.toString() ?? "";
        elements.historyInterfaceSelect.innerHTML = '<option value="">Interface seçin</option>' +
            interfaces.map(item => `<option value="${item.index}">${escapeHtml(item.name)}</option>`).join("");
        if (currentValue && interfaces.some(item => item.index.toString() === currentValue)) {
            elements.historyInterfaceSelect.value = currentValue;
        } else if (interfaces.length) {
            const preferred = interfaces.find(item => item.type === "Fiziksel") ?? interfaces[0];
            selectedInterfaceIndex = preferred.index;
            elements.historyInterfaceSelect.value = preferred.index.toString();
        }
    }

    function initializeInterfaceFilters() {
        document.querySelectorAll("[data-interface-filter]").forEach(button => {
            button.addEventListener("click", () => {
                activeInterfaceFilter = button.dataset.interfaceFilter;
                document.querySelectorAll("[data-interface-filter]").forEach(item =>
                    item.classList.toggle("is-active", item === button));
                renderFilteredInterfaces();
            });
        });
    }

    function updateAlerts(alarms) {
        elements.alertCount.textContent = alarms.length;
        elements.alertsList.innerHTML = alarms.length
            ? alarms.map(alarm => `<article class="alert-item ${alarm.level === "warning" ? "is-warning" : ""}"><span class="alert-symbol">!</span><div><strong>${escapeHtml(alarm.title)}</strong><p>${escapeHtml(alarm.detail)}</p></div></article>`).join("")
            : '<p class="empty-alerts"><span>✓</span>Aktif uyarı bulunmuyor.</p>';
    }

    function createHistoryPoint(data, timestamp = data.lastUpdated) {
        return {
            time: new Date(timestamp || Date.now()), cpu: data.cpuUsage, ram: data.memoryUsage,
            incoming: data.interfaces.reduce((sum, item) => sum + item.incomingMbps, 0),
            outgoing: data.interfaces.reduce((sum, item) => sum + item.outgoingMbps, 0)
        };
    }

    function appendHistory(data, timestamp) {
        if (!data.connected || !data.lastUpdated || data.lastUpdated === lastHistoryTimestamp) return;
        const point = createHistoryPoint(data, timestamp);
        liveHistory.system.push({ time: point.time, cpu: point.cpu, ram: point.ram });
        liveHistory.network.push({ time: point.time, incoming: point.incoming, outgoing: point.outgoing });
        lastHistoryTimestamp = data.lastUpdated;
        Object.values(liveHistory).forEach(items => {
            if (items.length > maxHistoryPoints) items.splice(0, items.length - maxHistoryPoints);
        });
    }

    function prepareCanvas(canvas) {
        const ratio = Math.min(window.devicePixelRatio || 1, 2);
        const rect = canvas.getBoundingClientRect();
        const width = Math.max(1, Math.round(rect.width));
        const height = Math.max(1, Math.round(rect.height));
        if (canvas.width !== width * ratio || canvas.height !== height * ratio) {
            canvas.width = width * ratio;
            canvas.height = height * ratio;
        }
        const context = canvas.getContext("2d");
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        return { context, width, height };
    }

    function getChartScale(definition) {
        if (definition.fixedMax) return { min: 0, max: definition.fixedMax, multiplier: 1, unit: "%" };
        const values = chartHistory[definition.historyKey].flatMap(point => definition.series.map(series => point[series.key])).filter(Number.isFinite);
        const highestMbps = Math.max(0, ...values);
        const multiplier = highestMbps < 1 ? 1000 : 1;
        const unit = multiplier === 1000 ? "Kbps" : "Mbps";
        const displayedHighest = highestMbps * multiplier;
        const niceMaximum = getNiceMaximum(displayedHighest > 0 ? displayedHighest * 1.12 : 1);
        return { min: 0, max: niceMaximum / multiplier, multiplier, unit };
    }

    function getNiceMaximum(value) {
        const exponent = Math.floor(Math.log10(value));
        const magnitude = 10 ** exponent;
        const fraction = value / magnitude;
        const niceFraction = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return niceFraction * magnitude;
    }

    function drawGrid(context, area, scale) {
        context.lineWidth = 1;
        context.font = "9px Segoe UI, sans-serif";
        context.textBaseline = "middle";
        for (let index = 0; index <= 4; index++) {
            const y = area.top + area.height * index / 4;
            context.strokeStyle = "rgba(119, 139, 173, .15)";
            context.beginPath(); context.moveTo(area.left, y); context.lineTo(area.right, y); context.stroke();
            const value = (scale.max - (scale.max - scale.min) * index / 4) * scale.multiplier;
            context.fillStyle = "#65738e";
            context.textAlign = "right";
            const decimals = scale.unit === "%" || value >= 10 ? 0 : 2;
            context.fillText(`${value.toLocaleString("tr-TR", { maximumFractionDigits: decimals })}${scale.unit === "%" ? "%" : ""}`, area.left - 8, y);
        }
        const verticalLines = 5;
        for (let index = 0; index <= verticalLines; index++) {
            const x = area.left + area.width * index / verticalLines;
            context.strokeStyle = "rgba(119, 139, 173, .08)";
            context.beginPath(); context.moveTo(x, area.top); context.lineTo(x, area.bottom); context.stroke();
        }
    }

    function drawTimeLabels(context, area, seriesHistory) {
        if (!seriesHistory.length) return;
        const labelIndexes = [0, Math.floor((seriesHistory.length - 1) / 2), seriesHistory.length - 1];
        context.font = "9px Segoe UI, sans-serif";
        context.fillStyle = "#65738e";
        context.textBaseline = "bottom";
        labelIndexes.forEach((historyIndex, labelIndex) => {
            const x = area.left + area.width * historyIndex / Math.max(seriesHistory.length - 1, 1);
            context.textAlign = labelIndex === 0 ? "left" : labelIndex === 2 ? "right" : "center";
            context.fillText(formatTime(seriesHistory[historyIndex].time), x, area.bottom + 24);
        });
    }

    function drawSmoothSeries(context, points, color) {
        if (!points.length) return;
        context.save();
        context.strokeStyle = color;
        context.lineWidth = 2.2;
        context.lineJoin = "round";
        context.shadowColor = color;
        context.shadowBlur = 7;
        context.beginPath();
        context.moveTo(points[0].x, points[0].y);
        for (let index = 1; index < points.length; index++) {
            const previous = points[index - 1];
            const current = points[index];
            const midpoint = (previous.x + current.x) / 2;
            context.bezierCurveTo(midpoint, previous.y, midpoint, current.y, current.x, current.y);
        }
        context.stroke();
        const latest = points[points.length - 1];
        context.shadowBlur = 9;
        context.fillStyle = color;
        context.beginPath(); context.arc(latest.x, latest.y, 3.2, 0, Math.PI * 2); context.fill();
        context.restore();
    }

    function drawChart(definition) {
        const canvas = document.getElementById(definition.canvasId);
        const { context, width, height } = prepareCanvas(canvas);
        context.clearRect(0, 0, width, height);
        const area = { left: 43, top: 12, right: width - 13, bottom: height - 31 };
        area.width = Math.max(1, area.right - area.left);
        area.height = Math.max(1, area.bottom - area.top);
        const scale = getChartScale(definition);
        const seriesHistory = chartHistory[definition.historyKey];
        drawGrid(context, area, scale);
        if (definition.canvasId === "networkChart") elements.networkChartUnit.textContent = scale.unit;
        drawTimeLabels(context, area, seriesHistory);
        definition.series.forEach(series => {
            const points = seriesHistory.map((point, index) => ({
                x: area.left + area.width * index / Math.max(seriesHistory.length - 1, 1),
                y: area.bottom - (point[series.key] - scale.min) / (scale.max - scale.min) * area.height
            })).filter((point, index) => Number.isFinite(seriesHistory[index][series.key]));
            drawSmoothSeries(context, points, series.color);
        });
    }

    function drawAllCharts() {
        drawChart(chartDefinitions.system);
        drawChart(chartDefinitions.network);
    }

    function updateChartValues(point) {
        elements.chartCpuValue.textContent = Number.isFinite(point.cpu) ? `${point.cpu}%` : "--%";
        elements.chartRamValue.textContent = Number.isFinite(point.ram) ? `${point.ram}%` : "--%";
        elements.chartIncomingValue.textContent = formatTraffic(point.incoming);
        elements.chartOutgoingValue.textContent = formatTraffic(point.outgoing);
        elements.systemChartTime.textContent = formatTime(point.time);
        elements.networkChartTime.textContent = formatTime(point.time);
    }

    function updateDashboard(data, addToHistory = true) {
        data = { ...data, interfaces: Array.isArray(data.interfaces) ? data.interfaces : [] };
        const sortedInterfaces = sortInterfacesByTraffic(data.interfaces || []);
        const busiest = sortedInterfaces[0];
        updateMetric("cpu", data.cpuUsage, thresholds.cpu);
        updateMetric("ram", data.memoryUsage, thresholds.ram);
        elements.sessionValue.textContent = Number.isFinite(data.sessionCount) ? Number(data.sessionCount).toLocaleString("tr-TR") : "--";
        elements.busiestPort.textContent = busiest?.name ?? "--";
        elements.busiestTraffic.textContent = busiest ? (busiest.isMeasuring ? "Ölçülüyor" : formatTraffic(busiest.totalMbps)) : "--";
        latestInterfaces = sortedInterfaces;
        latestConnectionState = data.connected;
        renderFilteredInterfaces();
        updateAlerts(checkAlarms(data));
        elements.deviceName.textContent = data.deviceName;
        elements.deviceIp.textContent = data.deviceIp;
        elements.deviceStatus.innerHTML = `<i></i> ${data.connected ? "Bağlı" : "Bağlantı Kesildi"}`;
        elements.deviceStatus.classList.toggle("is-disconnected", !data.connected);
        elements.interfaceDataStatus.innerHTML = `<i></i> ${data.connected ? "Canlı" : "Son başarılı ölçüm"}`;
        elements.interfaceDataStatus.classList.toggle("is-disconnected", !data.connected);
        elements.lastUpdated.textContent = data.lastUpdated ? formatTime(data.lastUpdated) : "Henüz başarılı sorgu yok";
        elements.connectionMessage.textContent = data.errorMessage || "";
        updateHistoryInterfaceOptions(sortedInterfaces);
        if (addToHistory) appendHistory(data);
        if (selectedHistoryRange === "live") {
            const systemLatest = chartHistory.system.at(-1);
            const networkLatest = chartHistory.network.at(-1);
            if (systemLatest && networkLatest) updateChartValues({ ...systemLatest, ...networkLatest });
            updateChartEmptyStates();
            drawAllCharts();
        }
    }

    async function buildInitialHistory() {
        resetDashboardForDevice();
        if (selectedDeviceId) await refreshDashboard();
    }

    async function refreshDashboard() {
        const deviceId = selectedDeviceId;
        const selected = availableDevices.find(device => device.id === deviceId);
        if (!deviceId || selected?.enabled === false || refreshInProgress) return;
        refreshInProgress = true;
        const requestVersion = ++monitoringRequestVersion;
        monitoringRequestController?.abort();
        const controller = new AbortController();
        monitoringRequestController = controller;
        try {
            const data = await getMonitoringData(deviceId, controller.signal);
            if (deviceId !== selectedDeviceId || requestVersion !== monitoringRequestVersion) return;
            updateDashboard(data);
            deviceListRefreshCounter++;
            if (deviceListRefreshCounter >= 5) {
                deviceListRefreshCounter = 0;
                loadDevices();
            }
        }
        catch (error) {
            if (error.name === "AbortError" || deviceId !== selectedDeviceId) return;
            console.error("İzleme verileri alınamadı.", error);
            elements.connectionMessage.textContent = getUserFacingError(error, "Monitoring verisi alınamadı.");
            elements.deviceStatus.innerHTML = "<i></i> Bağlantı Kesildi";
            elements.deviceStatus.classList.add("is-disconnected");
        } finally {
            if (requestVersion === monitoringRequestVersion) {
                refreshInProgress = false;
                monitoringRequestController = null;
            }
        }
    }

    function updateChartEmptyStates() {
        const historical = selectedHistoryRange !== "live";
        elements.systemChartEmpty.classList.toggle("is-visible", historical && chartHistory.system.length < 2);
        elements.networkChartEmpty.classList.toggle("is-visible", historical && chartHistory.network.length < 2);
    }

    async function getHistoryData(url) {
        const response = await fetch(url, { headers: { "Accept": "application/json" }, cache: "no-store" });
        if (!response.ok) throw new Error(`Geçmiş API isteği başarısız oldu: ${response.status}`);
        return response.json();
    }

    async function loadInterfaceHistory(deviceId, requestVersion) {
        if (selectedHistoryRange === "live" || !selectedInterfaceIndex) {
            chartHistory.network = liveHistory.network;
            return;
        }
        const samples = await getHistoryData(`/api/history/interfaces/${selectedInterfaceIndex}?deviceId=${encodeURIComponent(deviceId)}&range=${encodeURIComponent(selectedHistoryRange)}`);
        if (deviceId !== selectedDeviceId || requestVersion !== historyRequestVersion) return;
        chartHistory.network = samples.map(item => ({
            time: new Date(item.timestampUtc), incoming: item.incomingMbps, outgoing: item.outgoingMbps,
            maxTotal: item.maxTotalMbps, utilization: item.utilizationPercent
        }));
    }

    async function selectHistoryRange(range) {
        const deviceId = selectedDeviceId;
        const requestVersion = ++historyRequestVersion;
        selectedHistoryRange = range;
        document.querySelectorAll("[data-history-range]").forEach(button =>
            button.classList.toggle("is-active", button.dataset.historyRange === range));

        try {
            if (!deviceId) throw new Error("No FortiGate selected");
            if (range === "live") {
                chartHistory.system = liveHistory.system;
                chartHistory.network = liveHistory.network;
            } else {
                const samples = await getHistoryData(`/api/history/system?deviceId=${encodeURIComponent(deviceId)}&range=${encodeURIComponent(range)}`);
                if (deviceId !== selectedDeviceId || requestVersion !== historyRequestVersion) return;
                chartHistory.system = samples.map(item => ({
                    time: new Date(item.timestampUtc), cpu: item.cpuUsage, ram: item.memoryUsage
                }));
                await loadInterfaceHistory(deviceId, requestVersion);
            }
            if (deviceId !== selectedDeviceId || requestVersion !== historyRequestVersion) return;
            const systemLatest = chartHistory.system.at(-1);
            const networkLatest = chartHistory.network.at(-1);
            if (systemLatest) {
                elements.chartCpuValue.textContent = Number.isFinite(systemLatest.cpu) ? `${systemLatest.cpu.toFixed(2)}%` : "--%";
                elements.chartRamValue.textContent = Number.isFinite(systemLatest.ram) ? `${systemLatest.ram.toFixed(2)}%` : "--%";
                elements.systemChartTime.textContent = formatTime(systemLatest.time);
            }
            if (networkLatest) {
                elements.chartIncomingValue.textContent = formatTraffic(networkLatest.incoming);
                elements.chartOutgoingValue.textContent = formatTraffic(networkLatest.outgoing);
                elements.networkChartTime.textContent = formatTime(networkLatest.time);
            }
            updateChartEmptyStates();
            drawAllCharts();
        } catch (error) {
            if (deviceId !== selectedDeviceId || requestVersion !== historyRequestVersion) return;
            console.error("Geçmiş ölçümler alınamadı.", error);
            if (range !== "live") { chartHistory.system = []; chartHistory.network = []; }
            updateChartEmptyStates();
            drawAllCharts();
        }
    }

    function initializeHistoryControls() {
        document.querySelectorAll("[data-history-range]").forEach(button =>
            button.addEventListener("click", () => selectHistoryRange(button.dataset.historyRange)));
        elements.historyInterfaceSelect.addEventListener("change", async event => {
            selectedInterfaceIndex = Number(event.target.value) || null;
            if (selectedHistoryRange !== "live") await selectHistoryRange(selectedHistoryRange);
        });
        elements.portTableBody.addEventListener("click", async event => {
            const button = event.target.closest("[data-history-interface]");
            if (!button) return;
            selectedInterfaceIndex = Number(button.dataset.historyInterface);
            elements.historyInterfaceSelect.value = selectedInterfaceIndex.toString();
            if (selectedHistoryRange === "live") await selectHistoryRange("1h");
            else await selectHistoryRange(selectedHistoryRange);
        });
    }

    function debounce(callback, delay) {
        let timer;
        return () => { window.clearTimeout(timer); timer = window.setTimeout(callback, delay); };
    }

    document.addEventListener("DOMContentLoaded", async () => {
        cacheElements();
        initializeInterfaceFilters();
        initializeHistoryControls();
        initializeDeviceManagement();
        initializeAlertHistory();
        await loadAlertSettings();
        await loadDevices();
        await loadAlertHistory();
        await loadTopInterfaces();
        try { await buildInitialHistory(); }
        catch (error) {
            console.error("Başlangıç geçmişi oluşturulamadı.", error);
            await refreshDashboard();
        }
        window.addEventListener("resize", debounce(drawAllCharts, 120));
        window.setInterval(refreshDashboard, refreshIntervalMs);
        window.setInterval(loadAlertHistory, 30000);
        window.setInterval(loadTopInterfaces, 30000);
    });
})();
