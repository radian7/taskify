using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Taskify.AppHost;

/// <summary>
/// A throwaway TLS server certificate for the local PostgreSQL container (constitution Principle I: encrypted
/// connections to the database; research R16).
/// </summary>
/// <remarks>
/// <para>
/// Development only. It is generated in memory every time the AppHost starts, copied into the container, and never
/// written to the repository, so there is no key material to leak. Deployments use a managed PostgreSQL with a
/// real certificate and <c>SSL Mode=VerifyFull</c>.
/// </para>
/// </remarks>
/// <param name="CertificatePem">The certificate in PEM form (<c>server.crt</c>).</param>
/// <param name="PrivateKeyPem">The private key in PKCS#8 PEM form (<c>server.key</c>).</param>
internal sealed record DevPostgresCertificate(string CertificatePem, string PrivateKeyPem)
{
    /// <summary>Generates a self-signed RSA-2048 certificate for host names <c>postgres</c> and <c>localhost</c>, valid for 30 days.</summary>
    /// <returns>The certificate and its key.</returns>
    public static DevPostgresCertificate Generate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=postgres", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("postgres");
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], critical: false)); // TLS server authentication

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        return new DevPostgresCertificate(certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }
}
