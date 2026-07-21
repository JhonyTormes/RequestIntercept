class RequestInterceptApp {
    constructor() {
        this.requests = [];
        this.selectedId = null;
        this.currentDetail = null;
        this.knownIds = new Set();
        this.pollInterval = null;
        this.proxyEnabled = false;

        this.tableBody = document.getElementById('requestList');
        this.requestTable = document.getElementById('requestTable');
        this.emptyState = document.getElementById('emptyState');
        this.setupBanner = document.getElementById('setupBanner');
        this.detailPanel = document.getElementById('detailPanel');
        this.detailBody = document.getElementById('detailBody');
        this.countBadge = document.getElementById('countBadge');
        this.statusBadge = document.getElementById('statusBadge');
        this.proxyBadge = document.getElementById('proxyBadge');
        this.btnPause = document.getElementById('btnPause');
        this.btnClear = document.getElementById('btnClear');
        this.btnProxy = document.getElementById('btnProxy');
        this.btnCloseDetail = document.getElementById('btnCloseDetail');
        this.btnInstallCert = document.getElementById('btnInstallCert');
        this.btnCopyCurlCmd = document.getElementById('btnCopyCurlCmd');
        this.btnCopyCurlPs = document.getElementById('btnCopyCurlPs');
        this.btnExport = document.getElementById('btnExport');
        this.btnReplay = document.getElementById('btnReplay');
        this.btnBreakpoints = document.getElementById('btnBreakpoints');
        this.bpPatternInput = document.getElementById('bpPatternInput');
        this.breakpointPanel = document.getElementById('breakpointPanel');
        this.btnBlocklist = document.getElementById('btnBlocklist');
        this.blPatternInput = document.getElementById('blPatternInput');
        this.blEnabled = false;
        this.bpList = document.getElementById('bpList');
        this.bpCount = document.getElementById('bpCount');
        this.btnBpContinueAll = document.getElementById('btnBpContinueAll');
        this.btnBpDropAll = document.getElementById('btnBpDropAll');
        this.bpEnabled = false;
        this.bpPaused = [];
        this.filterInput = document.getElementById('filterInput');
        this.filterText = '';

        this.filterInput.addEventListener('input', (e) => {
            this.filterText = e.target.value.toLowerCase();
            this.renderRequests();
        });

        this.btnProxy.addEventListener('click', () => this.toggleProxy());
        this.btnInstallCert.addEventListener('click', () => this.installCert());
        this.btnPause.addEventListener('click', () => this.togglePause());
        this.btnClear.addEventListener('click', () => this.clearRequests());
        this.btnCloseDetail.addEventListener('click', () => this.closeDetail());
        this.btnCopyCurlCmd.addEventListener('click', () => this.copyAsCurl('cmd'));
        this.btnCopyCurlPs.addEventListener('click', () => this.copyAsCurl('powershell'));
        this.btnExport.addEventListener('click', () => this.exportHar());
        this.btnReplay.addEventListener('click', () => this.replayRequest());
        this.btnBreakpoints.addEventListener('click', () => this.toggleBreakpoints());
        this.btnBpContinueAll.addEventListener('click', () => this.bpContinueAll());
        this.btnBpDropAll.addEventListener('click', () => this.bpDropAll());
        this.bpPatternInput.addEventListener('change', () => this.bpSetPatterns());
        this.btnBlocklist.addEventListener('click', () => this.toggleBlocklist());
        this.blPatternInput.addEventListener('change', () => this.blSetPatterns());

        this.startPolling();
    }

    startPolling() {
        this.poll();
        this.pollInterval = setInterval(() => this.poll(), 1500);
    }

    async poll() {
        try {
            const [reqRes, statusRes, proxyRes, bpRes, blRes] = await Promise.all([
                fetch('/api/requests'),
                fetch('/api/status'),
                fetch('/api/proxy'),
                fetch('/api/breakpoints'),
                fetch('/api/blocklist')
            ]);
            const requests = await reqRes.json();
            const status = await statusRes.json();
            const proxy = await proxyRes.json();
            const bp = await bpRes.json();
            const bl = await blRes.json();
            this.requests = requests;
            this.proxyEnabled = proxy.enabled;
            this.bpEnabled = bp.enabled;
            this.bpPaused = bp.paused || [];
            this.blEnabled = bl.enabled;
            this.render(status);
            this.renderBreakpoints();
            this.renderBlocklist();
        } catch (e) {
            console.error('Poll failed', e);
        }
    }

    render(status) {
        this.requests.forEach(r => this.knownIds.add(r.id));

        this.countBadge.textContent = this.requests.length;

        this.proxyBadge.textContent = this.proxyEnabled ? 'Proxy ON' : 'Proxy OFF';
        this.proxyBadge.className = `proxy-badge ${this.proxyEnabled ? 'on' : 'off'}`;
        this.btnProxy.textContent = this.proxyEnabled ? 'Desativar Proxy' : 'Ativar Proxy';
        this.btnProxy.className = `btn ${this.proxyEnabled ? 'btn-primary active' : 'btn-primary'}`;

        this.statusBadge.textContent = status.paused ? 'Pausado' : 'Capturando';
        this.statusBadge.className = `status-badge ${status.paused ? 'paused' : 'active'}`;
        this.btnPause.textContent = status.paused ? 'Retomar' : 'Pausar';

        this.renderRequests();

        if (this.selectedId && this.currentDetail) {
            this.renderDetail(this.currentDetail);
        }
    }

    renderRequests() {
        const filtered = this.filterText
            ? this.requests.filter(r =>
                (r.url && r.url.toLowerCase().includes(this.filterText)) ||
                (r.host && r.host.toLowerCase().includes(this.filterText))
              )
            : this.requests;

        const hasRequests = filtered.length > 0;
        this.setupBanner.style.display = this.requests.length === 0 && !this.proxyEnabled ? 'flex' : 'none';
        this.requestTable.style.display = hasRequests ? '' : 'none';
        this.emptyState.style.display = hasRequests ? 'none' : 'block';
        this.emptyState.textContent = hasRequests ? '' :
            (this.filterText ? 'Nenhuma requisicao encontrada para este filtro.' : 'Nenhuma requisicao interceptada ainda.');

        if (!hasRequests) {
            this.tableBody.innerHTML = '';
            return;
        }

        const frag = document.createDocumentFragment();
        for (const r of filtered) {
            const tr = document.createElement('tr');
            tr.dataset.id = r.id;
            if (r.error) tr.classList.add('error');
            if (r.id === this.selectedId) tr.classList.add('selected');

            const time = new Date(r.timestamp).toLocaleTimeString('pt-BR');
            const method = r.method.toUpperCase();
            const statusClass = r.statusCode ? this.statusClass(r.statusCode) : '';
            const durClass = this.durationClass(r.durationMs);

            tr.innerHTML = `
                <td class="col-time">${time}</td>
                <td class="col-method"><span class="method-tag method-${method}">${method}</span></td>
                <td class="col-status"><span class="status-code ${statusClass}">${r.statusCode || '-'}</span></td>
                <td class="col-host" title="${this.escapeHtml(r.host)}">${this.escapeHtml(r.host)}</td>
                <td class="col-url" title="${this.escapeHtml(r.url)}">${this.escapeHtml(r.url)}</td>
                <td class="col-duration"><span class="duration ${durClass}">${r.durationMs}ms</span></td>
            `;

            if (r.statusCode) {
                tr.addEventListener('click', () => this.showDetail(r.id));
            }

            frag.appendChild(tr);
        }

        this.tableBody.innerHTML = '';
        this.tableBody.appendChild(frag);
    }

    async toggleProxy() {
        const url = this.proxyEnabled ? '/api/proxy/disable' : '/api/proxy/enable';
        try {
            const res = await fetch(url, { method: 'POST' });
            const data = await res.json();
            if (data.enabled !== undefined) {
                this.proxyEnabled = data.enabled;
            }
        } catch (e) {
            console.error('Proxy toggle failed', e);
        }
    }

    async showDetail(id) {
        this.selectedId = id;
        const res = await fetch(`/api/requests/${id}`);
        const detail = await res.json();
        this.currentDetail = detail;
        this.detailPanel.classList.remove('hidden');
        this.renderDetail(detail);

        document.querySelectorAll('#requestList tr').forEach(tr => {
            tr.classList.toggle('selected', tr.dataset.id === id);
        });
    }

    renderDetail(r) {
        const time = new Date(r.timestamp).toLocaleString('pt-BR');
        const method = r.method.toUpperCase();
        const statusClass = r.statusCode ? this.statusClass(r.statusCode) : '';

        let bodyHtml = '';
        if (r.responseBody) {
            bodyHtml = `<div class="detail-section">
                <h4>Response Body</h4>
                <div class="body-preview">${this.syntaxHighlight(r.responseBody)}</div>
            </div>`;
        } else if (r.error) {
            bodyHtml = `<div class="detail-section">
                <h4>Error</h4>
                <div class="body-preview" style="color: var(--red)">${this.escapeHtml(r.error)}</div>
            </div>`;
        }

        let reqBodyHtml = '';
        if (r.requestBody) {
            reqBodyHtml = `<div class="detail-section">
                <h4>Request Body</h4>
                <div class="body-preview">${this.syntaxHighlight(r.requestBody)}</div>
            </div>`;
        }

        this.detailBody.innerHTML = `
            <div class="detail-section">
                <h4>General</h4>
                <div class="detail-row"><span class="detail-label">Method:</span><span class="detail-value"><span class="method-tag method-${method}">${method}</span></span></div>
                <div class="detail-row"><span class="detail-label">URL:</span><span class="detail-value">${this.escapeHtml(r.url)}</span></div>
                <div class="detail-row"><span class="detail-label">Status:</span><span class="detail-value"><span class="status-code ${statusClass}">${r.statusCode || '-'} ${r.statusCode ? this.statusText(r.statusCode) : ''}</span></span></div>
                <div class="detail-row"><span class="detail-label">Time:</span><span class="detail-value">${time}</span></div>
                <div class="detail-row"><span class="detail-label">Duration:</span><span class="detail-value">${r.durationMs}ms</span></div>
                <div class="detail-row"><span class="detail-label">Protocol:</span><span class="detail-value">${r.isHttps ? 'HTTPS' : 'HTTP'}</span></div>
            </div>
            <div class="detail-section">
                <h4>Request Headers</h4>
                ${this.renderHeaders(r.requestHeaders)}
            </div>
            ${reqBodyHtml}
            <div class="detail-section">
                <h4>Response Headers</h4>
                ${this.renderHeaders(r.responseHeaders)}
            </div>
            ${bodyHtml}
        `;
    }

    renderHeaders(headers) {
        if (!headers || Object.keys(headers).length === 0) {
            return '<p style="opacity:0.5;font-size:0.85em">(nenhum header)</p>';
        }
        let html = '<table class="headers-table">';
        for (const [name, values] of Object.entries(headers)) {
            const val = Array.isArray(values) ? values.join(', ') : values;
            html += `<tr><td class="h-name">${this.escapeHtml(name)}</td><td class="h-value">${this.escapeHtml(val)}</td></tr>`;
        }
        html += '</table>';
        return html;
    }

    closeDetail() {
        this.detailPanel.classList.add('hidden');
        this.selectedId = null;
        this.currentDetail = null;
        document.querySelectorAll('#requestList tr').forEach(tr => tr.classList.remove('selected'));
    }

    async togglePause() {
        const paused = this.statusBadge.textContent === 'Capturando';
        const url = paused ? '/api/pause' : '/api/resume';
        await fetch(url, { method: 'POST' });
    }

    async clearRequests() {
        await fetch('/api/requests', { method: 'DELETE' });
        this.knownIds.clear();
        this.closeDetail();
    }

    copyAsCurl(shell) {
        const r = this.currentDetail;
        if (!r) return;
        const curlBin = shell === 'powershell' ? 'curl.exe' : 'curl';
        const q = '"';
        const parts = [`${curlBin} -X ${r.method} ${q}${r.url}${q}`];
        if (r.requestHeaders) {
            const skip = ['host', 'content-length', 'proxy-connection'];
            for (const [name, values] of Object.entries(r.requestHeaders)) {
                if (skip.includes(name.toLowerCase())) continue;
                const val = Array.isArray(values) ? values.join(', ') : values;
                const escaped = val.replace(/"/g, '""');
                parts.push(`-H ${q}${name}: ${escaped}${q}`);
            }
        }
        if (r.requestBody && !r.requestBody.startsWith('[Binary')) {
            const escaped = r.requestBody.replace(/"/g, '""');
            parts.push(`-d ${q}${escaped}${q}`);
        }
        const text = parts.join(' ^\n  ');
        navigator.clipboard.writeText(text).then(() => {
            const prev = this.btnCopyCurlCmd.textContent;
            if (shell === 'cmd') {
                this.btnCopyCurlCmd.textContent = 'Copiado!';
                setTimeout(() => this.btnCopyCurlCmd.textContent = prev, 2000);
            } else {
                this.btnCopyCurlPs.textContent = 'Copiado!';
                setTimeout(() => this.btnCopyCurlPs.textContent = prev, 2000);
            }
        }).catch(e => alert('Erro ao copiar: ' + e.message));
    }

    async exportHar() {
        window.open('/api/requests/export', '_blank');
    }

    async replayRequest() {
        if (!this.selectedId) return;
        this.btnReplay.disabled = true;
        this.btnReplay.textContent = 'Reenviando...';
        try {
            const res = await fetch(`/api/requests/${this.selectedId}/replay`, { method: 'POST' });
            if (!res.ok) {
                const err = await res.text();
                alert('Erro ao reenviar: ' + err);
                return;
            }
            const data = await res.json();
            this.selectedId = data.id;
            this.currentDetail = data;
            this.renderDetail(data);
        } catch (e) {
            alert('Erro ao reenviar: ' + e.message);
        } finally {
            this.btnReplay.disabled = false;
            this.btnReplay.textContent = 'Reenviar';
        }
    }

    renderBreakpoints() {
        const count = this.bpPaused.length;
        this.bpCount.textContent = count;
        this.breakpointPanel.classList.toggle('hidden', count === 0);
        this.btnBreakpoints.className = `btn ${this.bpEnabled ? 'btn-primary active' : 'btn-secondary'}`;
        this.btnBreakpoints.textContent = this.bpEnabled ? 'Breakpoints ON' : 'Breakpoints OFF';

        if (count === 0) return;

        this.bpList.innerHTML = '';
        for (const p of this.bpPaused) {
            const div = document.createElement('div');
            div.className = 'bp-item';
            const methodClass = `method-tag method-${p.method.toUpperCase()}`;
            div.innerHTML = `
                <span class="${methodClass}">${p.method}</span>
                <span class="bp-host">${this.escapeHtml(p.host)}</span>
                <span class="bp-url">${this.escapeHtml(p.url)}</span>
                <span class="bp-time">${new Date(p.timestamp).toLocaleTimeString('pt-BR')}</span>
                <button class="btn btn-small btn-primary bp-continue" data-id="${p.id}">Continuar</button>
                <button class="btn btn-small btn-danger bp-drop" data-id="${p.id}">Descartar</button>
            `;
            div.querySelector('.bp-continue').addEventListener('click', () => this.bpContinue(p.id));
            div.querySelector('.bp-drop').addEventListener('click', () => this.bpDrop(p.id));
            this.bpList.appendChild(div);
        }
    }

    async toggleBreakpoints() {
        const url = this.bpEnabled ? '/api/breakpoints/disable' : '/api/breakpoints/enable';
        await fetch(url, { method: 'POST' });
    }

    async bpSetPatterns() {
        const patterns = this.bpPatternInput.value.split(',').map(s => s.trim()).filter(s => s);
        await fetch('/api/breakpoints/patterns', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(patterns)
        });
    }

    async bpContinue(id) {
        await fetch(`/api/breakpoints/${id}/continue`, { method: 'POST' });
    }

    async bpDrop(id) {
        await fetch(`/api/breakpoints/${id}/drop`, { method: 'POST' });
    }

    async bpContinueAll() {
        await Promise.all(this.bpPaused.map(p =>
            fetch(`/api/breakpoints/${p.id}/continue`, { method: 'POST' })
        ));
    }

    async bpDropAll() {
        await Promise.all(this.bpPaused.map(p =>
            fetch(`/api/breakpoints/${p.id}/drop`, { method: 'POST' })
        ));
    }

    renderBlocklist() {
        this.btnBlocklist.className = `btn ${this.blEnabled ? 'btn-blocklist active' : 'btn-secondary'}`;
        this.btnBlocklist.textContent = this.blEnabled ? 'Blocklist ON' : 'Blocklist OFF';
    }

    async toggleBlocklist() {
        const url = this.blEnabled ? '/api/blocklist/disable' : '/api/blocklist/enable';
        await fetch(url, { method: 'POST' });
    }

    async blSetPatterns() {
        const patterns = this.blPatternInput.value.split(',').map(s => s.trim()).filter(s => s);
        await fetch('/api/blocklist/patterns', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(patterns)
        });
    }

    async installCert() {
        this.btnInstallCert.disabled = true;
        this.btnInstallCert.textContent = 'Instalando...';
        try {
            const res = await fetch('/api/certificate/install', { method: 'POST' });
            const data = await res.json();
            alert(data.message);
        } catch (e) {
            alert('Erro ao instalar certificado: ' + e.message);
        } finally {
            this.btnInstallCert.disabled = false;
            this.btnInstallCert.textContent = 'Instalar Certificado CA';
        }
    }

    statusClass(code) {
        if (!code) return '';
        if (code < 200) return '';
        if (code < 300) return 'status-2xx';
        if (code < 400) return 'status-3xx';
        if (code < 500) return 'status-4xx';
        return 'status-5xx';
    }

    statusText(code) {
        const texts = {
            200: 'OK', 201: 'Created', 204: 'No Content',
            301: 'Moved Permanently', 302: 'Found', 304: 'Not Modified',
            400: 'Bad Request', 401: 'Unauthorized', 403: 'Forbidden',
            404: 'Not Found', 405: 'Method Not Allowed',
            500: 'Internal Server Error', 502: 'Bad Gateway', 503: 'Service Unavailable'
        };
        return texts[code] || '';
    }

    durationClass(ms) {
        if (ms < 100) return 'fast';
        if (ms < 500) return 'medium';
        return 'slow';
    }

    syntaxHighlight(text) {
        if (!text) return '';
        if (this.isJson(text)) {
            try {
                const obj = JSON.parse(text);
                return this.highlightJson(JSON.stringify(obj, null, 2));
            } catch { }
        }
        return this.escapeHtml(text);
    }

    isJson(str) {
        str = str.trim();
        return (str.startsWith('{') && str.endsWith('}')) ||
               (str.startsWith('[') && str.endsWith(']'));
    }

    highlightJson(json) {
        return json.replace(/("(?:\\.|[^"\\])*")\s*:/g, '<span class="json-key">$1</span>:')
            .replace(/:\s*("(?:\\.|[^"\\])*")/g, ': <span class="json-string">$1</span>')
            .replace(/:\s*(\d+(?:\.\d+)?)/g, ': <span class="json-number">$1</span>')
            .replace(/:\s*(true|false)/g, ': <span class="json-bool">$1</span>')
            .replace(/:\s*(null)/g, ': <span class="json-null">$1</span>');
    }

    escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }
}

const style = document.createElement('style');
style.textContent = `
    @keyframes highlight {
        0% { background: rgba(122, 162, 247, 0.2); }
        100% { background: transparent; }
    }
`;
document.head.appendChild(style);

new RequestInterceptApp();
