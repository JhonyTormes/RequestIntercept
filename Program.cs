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

app.MapFallbackToFile("index.html");

Console.WriteLine(@$"=============================================
  RequestIntercept Proxy
=============================================
  Web UI:    http://localhost:{webPort}
  Proxy:     0.0.0.0:{builder.Configuration.GetValue<int>("Proxy:Port", 8888)}

  Configure your browser/OS to use the proxy above.
  For HTTPS inspection, trust the CA certificate:
  - Download at http://localhost:{webPort}/api/certificate
  - Install as Trusted Root Certification Authority
=============================================");

app.Run();
