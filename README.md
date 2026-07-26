# RequestIntercept

Standalone HTTP/HTTPS MITM (Man-in-the-Middle) intercepting proxy for inspecting application traffic — similar to Postman's proxy — **no installation** required. Single `.exe` file, run it anywhere.

## Features

- ✅ **HTTP** and **HTTPS** interception with MITM (TLS 1.2)
- ✅ Auto-generated per-host TLS certificates signed by a local CA root
- ✅ Web-based UI for real-time traffic inspection (auto-refresh every 1.5s)
- ✅ Full **request/response headers and body** capture
- ✅ JSON, XML, forms, chunked encoding, and binary body support
- ✅ Pause/resume capture at any time
- ✅ **Enable Proxy** button — toggles Windows system proxy directly from the UI
- ✅ **Install CA Certificate** one-click install into the Windows Trusted Root store
- ✅ **URL filter** — type text to filter requests by host, URL, headers, or body
- ✅ **Copy as cURL** — generates curl commands for CMD or PowerShell
- ✅ **Replay requests** — one-click resend from the detail panel
- ✅ **Breakpoints** — pause matched requests, inspect/edit headers and body, then forward or drop
- ✅ **Blocklist** — automatically drop requests matching URL patterns
- ✅ **Export HAR** — download all captured requests in HTTP Archive format
- ✅ Zero installation — download and run
- ✅ Single-file publish — portable `.exe` (~95 MB)

## Getting Started

### Development (with .NET SDK)

```bash
dotnet run
```

### Published single-file (no .NET SDK required)

```bash
dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

This produces `publish\RequestIntercept.exe` — copy it anywhere and run.

### After launching

```
=============================================
  RequestIntercept Proxy
=============================================
  Web UI:    http://localhost:4000
  Proxy:     0.0.0.0:8888

  1. Open the Web UI:  http://localhost:4000
  2. Click "Enable Proxy" at the top
  3. Requests will be captured automatically

  For HTTPS, install the CA certificate:
     http://localhost:4000/api/certificate
=============================================
```

## Web UI

### Toolbar

| Element | Description |
|---|---|
| **Enable/Disable Proxy** | Toggles the Windows system proxy (`HKCU\Internet Settings`) |
| **Install CA Certificate** | Installs the root CA into the Windows Trusted Root store (requires admin) |
| **Filter URL** | Text input to filter requests by host, URL, headers, or body content |
| **Pause/Resume** | Pauses or resumes capture of new requests |
| **Clear** | Removes all captured requests from the list |
| **Export HAR** | Downloads all captured requests as a HAR file |
| **Breakpoints ON/OFF** | Enables or disables breakpoint mode |
| **Breakpoint patterns** | Comma-separated URL patterns for breakpoint matching (e.g. `api.stripe.com, /v2/`) |
| **Blocklist ON/OFF** | Enables or disables URL blocklist |
| **Blocklist patterns** | Comma-separated URL patterns to block (e.g. `analytics, tracking`) |

### Breakpoints

Breakpoint mode pauses specific requests before they reach the server, allowing manual inspection and editing.

**How to use:**

1. Click **"Breakpoints OFF"** in the toolbar to enable
2. Enter URL patterns in the text field (e.g. `login, /api/v2/`)
   - Use `*` to pause **every** request
   - Separate multiple patterns with commas
3. When a matching request arrives, it is **paused** and an orange panel appears at the top
4. From the panel you can:
   - **Continue** — forwards the request (optionally with edited headers/body)
   - **Drop** — cancels the request without forwarding
   - **Continue All / Drop All** — bulk action on all paused requests
5. If no action is taken within 2 minutes, the request is dropped automatically

### Blocklist

The blocklist silently drops requests matching one or more URL patterns before they reach the destination server. No response is returned to the client.

**How to use:**

1. Enable the blocklist and add patterns in the toolbar
2. Matching requests are dropped and shown in the log with an error status
3. Multiple patterns can be separated by commas

### Detail Panel

Clicking a request opens the detail panel:

- **General** — method, URL, status code, timestamp, duration, protocol (HTTP/HTTPS)
- **Request Headers** — all sent headers
- **Request Body** — request payload (if present)
- **Response Headers** — all received headers
- **Response Body** — response payload with JSON syntax highlighting

Available actions:

| Action | Description |
|---|---|
| **Replay** | Resends the original request and shows the new response |
| **Copy Curl (CMD)** | Copies the curl command for Windows Command Prompt |
| **Copy Curl (PowerShell)** | Copies the `curl.exe` command for PowerShell |

### Binary Bodies

When the `content-type` is not text (`text/*`, `application/json`, `application/xml`, etc.) or the body contains more than 30% non-printable characters, it is displayed as `[Binary data: X bytes]` instead of corrupted text.

## API Reference

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/requests` | List all captured requests (supports `?q=` filter) |
| `GET` | `/api/requests/{id}` | Full details of a single request |
| `DELETE` | `/api/requests` | Clear all requests |
| `GET` | `/api/requests/export` | Export all requests in HAR format |
| `POST` | `/api/requests/{id}/replay` | Replay a request |
| `GET` | `/api/status` | Proxy status and request count |
| `POST` | `/api/pause` | Pause capture |
| `POST` | `/api/resume` | Resume capture |
| `GET` | `/api/proxy` | Check Windows proxy status |
| `POST` | `/api/proxy/enable` | Enable Windows system proxy |
| `POST` | `/api/proxy/disable` | Disable Windows system proxy |
| `GET` | `/api/certificate` | Download CA certificate (`.crt`) |
| `POST` | `/api/certificate/install` | Install CA into Windows Trusted Root store |
| `GET` | `/api/breakpoints` | Breakpoint status (enabled, patterns, paused items) |
| `POST` | `/api/breakpoints/enable` | Enable breakpoint mode |
| `POST` | `/api/breakpoints/disable` | Disable breakpoint mode |
| `POST` | `/api/breakpoints/patterns` | Set URL patterns (JSON body: `["pattern1", "pattern2"]`) |
| `POST` | `/api/breakpoints/{id}/continue` | Continue a paused request (optional JSON body with edited headers/body) |
| `POST` | `/api/breakpoints/{id}/drop` | Drop a paused request |
| `GET` | `/api/blocklist` | Blocklist status (enabled, patterns) |
| `POST` | `/api/blocklist/enable` | Enable blocklist |
| `POST` | `/api/blocklist/disable` | Disable blocklist |
| `POST` | `/api/blocklist/patterns` | Set blocklist patterns (JSON body: `["pattern1", "pattern2"]`) |

## Testing with curl

```bash
# HTTP (explicit proxy)
curl -x http://localhost:8888 http://httpbin.org/get

# HTTPS with self-signed cert (skip verification with -k)
curl -x http://localhost:8888 -k https://httpbin.org/get

# HTTPS with CA installed (no -k needed)
curl -x http://localhost:8888 https://httpbin.org/get
```

## Manual Windows Proxy Configuration

1. Open **Settings** → **Network & Internet** → **Proxy**
2. Enable "Use a proxy server"
3. Address: `localhost` | Port: `8888`

Or simply click **"Enable Proxy"** in the Web UI.

## CA Certificate Installation (for HTTPS without warnings)

### Via Web UI
Click **"Install CA Certificate"** in the toolbar (requires admin privileges).

### Manual
1. Download the certificate from `http://localhost:4000/api/certificate`
2. Open the `.crt` file → **Install Certificate**
3. Choose **Local Machine** → **Place all certificates in the following store**
4. Select **Trusted Root Certification Authorities**

## How It Works

```
┌─────────────┐    CONNECT host:443     ┌──────────────────┐    TLS     ┌──────────────┐
│  Application │ ──────────────────────→ │  RequestIntercept │ ────────→ │    Server    │
│  (HttpClient)│ ←────────────────────── │  (MITM Proxy)     │ ←──────── │  (httpbin)   │
└─────────────┘   200 OK + TLS (mit cert) └──────────────────┘           └──────────────┘
                          │
                          ▼
                    ┌─────────────┐
                    │   Web UI    │
                    │   :4000     │
                    └─────────────┘
```

The proxy acts as an intermediate TLS server:

1. The application sends a **CONNECT** request to the proxy (port 8888)
2. The proxy generates a TLS certificate for the target host (signed by the local CA)
3. Establishes TLS with the application (decrypting the traffic)
4. Forwards the request to the real server over TLS
5. Returns the response to the application
6. The entire exchange is recorded and viewable in the Web UI (port 4000)

For **HTTP** requests (no TLS), the proxy receives the absolute URL, extracts host and path, connects to the destination server, and relays the traffic.

### Data Flow

1. **HTTPS interception** — `CONNECT` → TLS handshake with auto-generated cert → decrypted relay → re-encrypt to server
2. **HTTP relay** — direct TCP proxy with absolute URL parsing
3. **Breakpoints** — matched requests pause via `TaskCompletionSource`; the user can edit and forward or drop
4. **Blocklist** — matched requests are immediately dropped before reaching the server
5. **Certificate management** — RSA 4096-bit CA root (10-year validity), RSA 2048-bit per-host certs (365 days), thread-safe caching via `ConcurrentDictionary`

## Project Structure

```
RequestIntercept/
├── Program.cs                          # ASP.NET Minimal API entry point + all endpoints
├── RequestIntercept.csproj             # .NET 9 Web project
├── appsettings.json                    # Port and logging configuration
├── Models/
│   ├── InterceptedRequest.cs           # Request/response data model
│   └── BreakpointItem.cs               # Breakpoint models (item, result, edit)
├── Services/
│   ├── ProxyService.cs                 # Core TCP proxy (HTTP + HTTPS MITM)
│   ├── CertificateService.cs           # CA root + per-host TLS cert generation
│   ├── RequestStore.cs                 # Thread-safe in-memory request storage
│   ├── BreakpointService.cs            # Breakpoint pattern matching and pause/continue
│   └── BlocklistService.cs             # URL pattern-based request blocking
└── wwwroot/
    ├── index.html                      # Single-page Web UI
    ├── style.css                       # Dark theme styles
    └── app.js                          # Frontend logic (polling, rendering, breakpoints)
```

## Technology Stack

- **.NET 9** — ASP.NET Core Minimal API + BackgroundService
- **System.Net.Security.SslStream** — TLS 1.2 server/client
- **System.Net.Sockets.TcpListener** — TCP proxy
- **System.Security.Cryptography** — RSA 4096/2048 certificate generation
- **Microsoft.Win32.Registry** — Windows proxy enable/disable
- **HTML/CSS/JS (vanilla)** — zero-framework Web UI

## License

MIT
