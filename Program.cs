using Microsoft.AspNetCore.Mvc;
using RequestIntercept.Models;
using RequestIntercept.Services;

var builder = WebApplication.CreateBuilder(args);

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

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();

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

app.MapFallbackToFile("index.html");

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
