(() => {
    "use strict";

    const refreshIntervalMs = 2000;
    const maxHistoryPoints = 60;
    const thresholds = { cpu: 80, ram: 80, interfaceWarning: 60, interfaceCritical: 80 };
    const monitoringEndpoint = "/api/monitoring/current";
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
            "historyInterfaceSelect", "systemChartEmpty", "networkChartEmpty"]
            .forEach(id => elements[id] = document.getElementById(id));
    }

    async function getMonitoringData() {
        const requestOptions = { headers: { "Accept": "application/json" }, cache: "no-store" };
        const response = await fetch(monitoringEndpoint, requestOptions);
        if (!response.ok) throw new Error(`İzleme API isteği başarısız oldu: ${response.status}`);
        return response.json();
    }

    function sortInterfacesByTraffic(interfaces) { return [...interfaces].sort((a, b) => b.totalMbps - a.totalMbps); }

    function checkAlarms(data) {
        const alarms = [];
        if (!data.connected) alarms.push({ title: "FortiGate bağlantısı kesildi", detail: data.errorMessage || "SNMP verisi alınamıyor." });
        if (data.cpuUsage >= thresholds.cpu) alarms.push({ title: "Yüksek CPU kullanımı", detail: `CPU kullanımı %${data.cpuUsage} seviyesine ulaştı.` });
        if (data.memoryUsage >= thresholds.ram) alarms.push({ title: "Yüksek RAM kullanımı", detail: `RAM kullanımı %${data.memoryUsage} seviyesine ulaştı.` });
        data.interfaces.filter(item => item.utilizationPercent >= thresholds.interfaceWarning).forEach(item => {
            const critical = item.utilizationPercent >= thresholds.interfaceCritical;
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
            return `<tr title="${escapeHtml(item.errorMessage)}">
                <td class="traffic-number">${item.index}</td>
                <td><button type="button" class="port-name-button" data-history-interface="${item.index}">${escapeHtml(item.name)}</button></td>
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
        updateDashboard(await getMonitoringData());
    }

    async function refreshDashboard() {
        if (refreshInProgress) return;
        refreshInProgress = true;
        try { updateDashboard(await getMonitoringData()); }
        catch (error) {
            console.error("İzleme verileri alınamadı.", error);
            elements.connectionMessage.textContent = "Veri bağlantısı kesildi";
        } finally { refreshInProgress = false; }
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

    async function loadInterfaceHistory() {
        if (selectedHistoryRange === "live" || !selectedInterfaceIndex) {
            chartHistory.network = liveHistory.network;
            return;
        }
        const samples = await getHistoryData(`/api/history/interfaces/${selectedInterfaceIndex}?range=${selectedHistoryRange}`);
        chartHistory.network = samples.map(item => ({
            time: new Date(item.timestampUtc), incoming: item.incomingMbps, outgoing: item.outgoingMbps,
            maxTotal: item.maxTotalMbps, utilization: item.utilizationPercent
        }));
    }

    async function selectHistoryRange(range) {
        selectedHistoryRange = range;
        document.querySelectorAll("[data-history-range]").forEach(button =>
            button.classList.toggle("is-active", button.dataset.historyRange === range));

        try {
            if (range === "live") {
                chartHistory.system = liveHistory.system;
                chartHistory.network = liveHistory.network;
            } else {
                const samples = await getHistoryData(`/api/history/system?range=${range}`);
                chartHistory.system = samples.map(item => ({
                    time: new Date(item.timestampUtc), cpu: item.cpuUsage, ram: item.memoryUsage
                }));
                await loadInterfaceHistory();
            }
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
        try { await buildInitialHistory(); }
        catch (error) {
            console.error("Başlangıç geçmişi oluşturulamadı.", error);
            await refreshDashboard();
        }
        window.addEventListener("resize", debounce(drawAllCharts, 120));
        window.setInterval(refreshDashboard, refreshIntervalMs);
    });
})();
