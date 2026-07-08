using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RequestIntercept.Services;

public class CertificateService
{
    private readonly string _caPfxPath;
    private readonly string _caCertPath;
    private readonly ConcurrentDictionary<string, X509Certificate2> _certCache = new();
    private X509Certificate2? _caCertificate;
    private static readonly HashAlgorithmName HashAlgo = HashAlgorithmName.SHA256;
    private static readonly RSASignaturePadding Padding = RSASignaturePadding.Pkcs1;

    public string? CaCertificatePath => _caCertPath;
    public X509Certificate2? CaCertificate => _caCertificate;

    public CertificateService(string storagePath)
    {
        var dir = Path.Combine(storagePath, ".requestintercept-certs");
        Directory.CreateDirectory(dir);
        _caPfxPath = Path.Combine(dir, "requestintercept-ca.pfx");
        _caCertPath = Path.Combine(dir, "requestintercept-ca.crt");
    }

    public void Initialize()
    {
        if (File.Exists(_caPfxPath))
        {
            _caCertificate = X509CertificateLoader.LoadPkcs12(
                File.ReadAllBytes(_caPfxPath), null,
                X509KeyStorageFlags.Exportable);
        }
        else
        {
            _caCertificate = CreateRootCertificate();
            File.WriteAllBytes(_caPfxPath, _caCertificate.Export(X509ContentType.Pkcs12));
            File.WriteAllBytes(_caCertPath, _caCertificate.Export(X509ContentType.Cert));
        }
    }

    private X509Certificate2 CreateRootCertificate()
    {
        var key = RSA.Create(4096);
        var distinguishedName = new X500DistinguishedName("CN=RequestIntercept Root CA, O=RequestIntercept, C=BR");

        var request = new CertificateRequest(distinguishedName, key, HashAlgo, Padding);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return certificate;
    }

    public X509Certificate2 GetOrCreateHostCertificate(string hostname)
    {
        return _certCache.GetOrAdd(hostname, CreateHostCertificate);
    }

    private X509Certificate2 CreateHostCertificate(string hostname)
    {
        if (_caCertificate is null)
            throw new InvalidOperationException("CA not initialized");

        using var key = RSA.Create(2048);
        var subject = new X500DistinguishedName($"CN={hostname}, O=RequestIntercept, C=BR");

        var request = new CertificateRequest(subject, key, HashAlgo, Padding);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(hostname);
        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));

        var serial = RandomNumberGenerator.GetBytes(16);
        using var certificate = request.Create(_caCertificate, DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365), serial);

        using var certWithKey = certificate.CopyWithPrivateKey(key);
        var pfxBytes = certWithKey.Export(X509ContentType.Pkcs12);
        var result = X509CertificateLoader.LoadPkcs12(pfxBytes, (string?)null,
            X509KeyStorageFlags.Exportable);
        return result;
    }

    public (bool Success, string Message) InstallCaCertificate()
    {
        if (_caCertificate is null)
            return (false, "Certificado CA nao foi gerado");

        try
        {
            // Try LocalMachine\Root first (requires admin), fall back to CurrentUser\Root
            var storeName = StoreName.Root;
            var storeLocation = StoreLocation.LocalMachine;
            try
            {
                using var store = new X509Store(storeName, storeLocation, OpenFlags.ReadWrite);
                store.Open(OpenFlags.ReadWrite);
                if (store.Certificates.Find(X509FindType.FindBySubjectName, "RequestIntercept Root CA", false).Count > 0)
                    return (true, "Certificado CA ja esta instalado");
                store.Add(_caCertificate);
                store.Close();
                return (true, "Certificado CA instalado com sucesso em Trusted Root (maquina)");
            }
            catch
            {
                storeLocation = StoreLocation.CurrentUser;
                using var store = new X509Store(storeName, storeLocation, OpenFlags.ReadWrite);
                store.Open(OpenFlags.ReadWrite);
                if (store.Certificates.Find(X509FindType.FindBySubjectName, "RequestIntercept Root CA", false).Count > 0)
                    return (true, "Certificado CA ja esta instalado");
                store.Add(_caCertificate);
                store.Close();
                return (true, "Certificado CA instalado em Trusted Root (usuario). Para instalacao completa, execute como administrador.");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao instalar certificado: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _caCertificate?.Dispose();
        foreach (var cert in _certCache.Values)
            cert.Dispose();
        _certCache.Clear();
    }
}
