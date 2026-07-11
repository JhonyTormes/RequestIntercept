using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using RequestIntercept.Models;
using RequestIntercept.Services;

var builder = WebApplication.CreateBuilder(args);

// Load embedded appsettings.json if available
var assembly = typeof(Program).Assembly;
var embeddedNs = assembly.GetName().Name + ".wwwroot";
var embeddedRes = assembly.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("appsettings.json"));
if (embeddedRes is not null)
{
    using var stream = assembly.GetManifestResourceStream(embeddedRes);
    if (stream is not null)
        builder.Configuration.AddJsonStream(stream);
}

builder.Services.AddSingleton<CertificateService>(_ =>
{
    var certService = new CertificateService(AppDomain.CurrentDomain.BaseDirectory);
    certService.Initialize();
    return certService;
});
builder.Services.AddSingleton<RequestStore>();
builder.Services.AddSingleton<ProxyService>();
builder.Services.AddHostedService<ProxyService>(sp => sp.GetRequiredService<ProxyService>());

var webPort = builder.Configuration.GetValue<int>("Web:Port", 4000);
builder.WebHost.UseUrls($"http://0.0.0.0:{webPort}");

var app = builder.Build();

var embeddedProvider = new EmbeddedFileProvider(assembly, embeddedNs);

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseStaticFiles();
}
else
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = embeddedProvider
    });
}

// ---- API Endpoints ----

app.MapGet("/api/requests", (RequestStore store) =>
    store.GetAll().Select(r => new
    {
        r.Id,
        r.Timestamp,
        r.Method,
        r.Url,
        r.Host,
        r.StatusCode,
        r.DurationMs,
        r.IsHttps,
        r.Error
    })
);

app.MapGet("/api/requests/{id:guid}", (Guid id, RequestStore store) =>
{
    var r = store.GetById(id);
    return r is null ? Results.NotFound() : Results.Ok(r);
});

app.MapDelete("/api/requests", (RequestStore store) =>
{
    store.Clear();
    return Results.NoContent();
});

app.MapGet("/api/requests/export", (RequestStore store) =>
{
    var requests = store.GetAll();
    var entries = requests.Select(r =>
    {
        var reqHeaders = r.RequestHeaders?.SelectMany(kv =>
            kv.Value.Select(v => new { name = kv.Key, value = v })).ToArray() ?? [];
        var respHeaders = r.ResponseHeaders?.SelectMany(kv =>
            kv.Value.Select(v => new { name = kv.Key, value = v })).ToArray() ?? [];

        return new
        {
            startedDateTime = r.Timestamp.ToString("o"),
            time = r.DurationMs,
            request = new
            {
                method = r.Method,
                url = r.Url,
                httpVersion = "HTTP/1.1",
                cookies = Array.Empty<object>(),
                headers = reqHeaders,
                queryString = Array.Empty<object>(),
                postData = r.RequestBody is not null ? new { mimeType = GetContentType(r.RequestHeaders), text = r.RequestBody } : null,
                headersSize = -1,
                bodySize = r.RequestBody?.Length ?? -1
            },
            response = r.StatusCode.HasValue ? new
            {
                status = r.StatusCode.Value,
                statusText = "",
                httpVersion = "HTTP/1.1",
                cookies = Array.Empty<object>(),
                headers = respHeaders,
                content = new
                {
                    size = r.ResponseBody?.Length ?? 0,
                    mimeType = GetContentType(r.ResponseHeaders),
                    text = r.ResponseBody
                },
                redirectURL = "",
                headersSize = -1,
                bodySize = r.ResponseBody?.Length ?? -1
            } : null,
            cache = new { },
            timings = new { send = 0, wait = r.DurationMs, receive = 0 }
        };
    }).ToList();

    var har = new
    {
        log = new
        {
            version = "1.2",
            creator = new { name = "RequestIntercept", version = "1.0" },
            entries
        }
    };

    return Results.Json(har, new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    }, "application/json", statusCode: 200);
});

app.MapGet("/api/status", ([FromServices] RequestStore store, [FromServices] ProxyService proxy) =>
    Results.Ok(new
    {
        proxy.ProxyPort,
        Paused = store.IsPaused,
        RequestCount = store.GetAll().Count
    })
);

app.MapPost("/api/pause", (RequestStore store) =>
{
    store.IsPaused = true;
    return Results.Ok(new { Paused = true });
});

app.MapPost("/api/resume", (RequestStore store) =>
{
    store.IsPaused = false;
    return Results.Ok(new { Paused = false });
});

app.MapGet("/api/certificate", (CertificateService certService) =>
{
    if (certService.CaCertificatePath is null || !File.Exists(certService.CaCertificatePath))
        return Results.NotFound("CA certificate not found");

    var bytes = File.ReadAllBytes(certService.CaCertificatePath);
    return Results.File(bytes, "application/x-x509-ca-cert", "requestintercept-ca.crt");
});

app.MapPost("/api/certificate/install", (CertificateService certService) =>
{
    var (success, message) = certService.InstallCaCertificate();
    return success ? Results.Ok(new { installed = true, message }) : Results.Ok(new { installed = false, message });
});

app.MapFallback(async (HttpContext context) =>
{
    var file = embeddedProvider.GetFileInfo("index.html");
    if (file.Exists)
    {
        context.Response.ContentType = "text/html";
        using var stream = file.CreateReadStream();
        await stream.CopyToAsync(context.Response.Body);
    }
});

app.MapPost("/api/proxy/enable", (IConfiguration config) =>
{
    try
    {
        var port = config.GetValue<int>("Proxy:Port", 8888);
        using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
        if (reg is null) return Results.Problem("Failed to open registry");
        reg.SetValue("ProxyEnable", 1, Microsoft.Win32.RegistryValueKind.DWord);
        reg.SetValue("ProxyServer", $"localhost:{port}", Microsoft.Win32.RegistryValueKind.String);
        reg.SetValue("ProxyHttp1.1", 0, Microsoft.Win32.RegistryValueKind.DWord);
        return Results.Ok(new { enabled = true, message = "Proxy do Windows ativado" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
}).ExcludeFromDescription();

app.MapPost("/api/proxy/disable", () =>
{
    try
    {
        using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
        if (reg is null) return Results.Problem("Failed to open registry");
        reg.SetValue("ProxyEnable", 0, Microsoft.Win32.RegistryValueKind.DWord);
        return Results.Ok(new { enabled = false, message = "Proxy do Windows desativado" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
}).ExcludeFromDescription();

app.MapGet("/api/proxy", () =>
{
    try
    {
        using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        var enabled = reg?.GetValue("ProxyEnable") is int val && val == 1;
        var server = reg?.GetValue("ProxyServer") as string ?? "";
        return Results.Ok(new { enabled, server });
    }
    catch
    {
        return Results.Ok(new { enabled = false, server = "" });
    }
}).ExcludeFromDescription();

Console.WriteLine($@"=============================================
  RequestIntercept Proxy
=============================================
  Web UI:    http://localhost:{webPort}
  Proxy:     0.0.0.0:{builder.Configuration.GetValue<int>("Proxy:Port", 8888)}

  Para comecar:

  1. Acesse a Web UI: http://localhost:{webPort}
  2. Clique em ""Ativar Proxy"" no topo da pagina
  3. As requisicoes serao capturadas automaticamente

  Para HTTPS, instale o certificado CA:
     http://localhost:{webPort}/api/certificate
=============================================");

app.Run();

static string GetContentType(Dictionary<string, string[]>? headers)
{
    if (headers is null) return "application/octet-stream";
    if (headers.TryGetValue("content-type", out var ct) && ct.Length > 0)
        return ct[0].Split(';')[0].Trim();
    return "application/octet-stream";
}
