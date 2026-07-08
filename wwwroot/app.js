class RequestInterceptApp {
    constructor() {
        this.requests = [];
        this.selectedId = null;
        this.knownIds = new Set();
        this.pollInterval = null;

        this.tableBody = document.getElementById('requestList');
        this.emptyState = document.getElementById('emptyState');
        this.detailPanel = document.getElementById('detailPanel');
        this.detailBody = document.getElementById('detailBody');
        this.countBadge = document.getElementById('countBadge');
        this.statusBadge = document.getElementById('statusBadge');
        this.btnPause = document.getElementById('btnPause');
        this.btnClear = document.getElementById('btnClear');
        this.btnCloseDetail = document.getElementById('btnCloseDetail');

        this.btnPause.addEventListener('click', () => this.togglePause());
        this.btnClear.addEventListener('click', () => this.clearRequests());
        this.btnCloseDetail.addEventListener('click', () => this.closeDetail());

        this.startPolling();
    }

    startPolling() {
        this.poll();
        this.pollInterval = setInterval(() => this.poll(), 1500);
    }

    async poll() {
        try {
            const [reqRes, statusRes] = await Promise.all([
                fetch('/api/requests'),
                fetch('/api/status')
            ]);
            const requests = await reqRes.json();
            const status = await statusRes.json();
            this.requests = requests;
            this.render(status);
        } catch (e) {
            console.error('Poll failed', e);
        }
    }

    render(status) {
        const newCount = this.requests.filter(r => !this.knownIds.has(r.id)).length;
        this.requests.forEach(r => this.knownIds.add(r.id));

        this.countBadge.textContent = this.requests.length;
        this.statusBadge.textContent = status.paused ? 'Pausado' : 'Ativo';
        this.statusBadge.className = `status-badge ${status.paused ? 'paused' : 'active'}`;
        this.btnPause.textContent = status.paused ? 'Retomar' : 'Pausar';

        this.emptyState.style.display = this.requests.length === 0 ? 'block' : 'none';

        if (this.requests.length === 0) {
            this.tableBody.innerHTML = '';
            return;
        }

        const frag = document.createDocumentFragment();
        for (const r of this.requests) {
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

            if (newCount > 0 && r.id === this.requests[0]?.id) {
                tr.style.animation = 'highlight 1s ease-out';
            }

            frag.appendChild(tr);
        }

        this.tableBody.innerHTML = '';
        this.tableBody.appendChild(frag);

        if (this.selectedId) {
            const detail = this.requests.find(r => r.id === this.selectedId);
            if (detail) this.renderDetail(detail);
        }
    }

    async showDetail(id) {
        this.selectedId = id;
        const res = await fetch(`/api/requests/${id}`);
        const detail = await res.json();
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
        document.querySelectorAll('#requestList tr').forEach(tr => tr.classList.remove('selected'));
    }

    async togglePause() {
        const paused = this.statusBadge.textContent === 'Ativo';
        const url = paused ? '/api/pause' : '/api/resume';
        await fetch(url, { method: 'POST' });
    }

    async clearRequests() {
        await fetch('/api/requests', { method: 'DELETE' });
        this.knownIds.clear();
        this.closeDetail();
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

// CSS animation for new items
const style = document.createElement('style');
style.textContent = `
    @keyframes highlight {
        0% { background: rgba(122, 162, 247, 0.2); }
        100% { background: transparent; }
    }
`;
document.head.appendChild(style);

new RequestInterceptApp();
