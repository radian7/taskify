using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace Taskify.Web.Services;

/// <summary>
/// Configures the Data Protection key ring that signs and encrypts the selected-user cookie (research R8).
/// The keys are persisted, so the cookie survives a restart, and they are encrypted at rest with a certificate
/// from the <c>dataprotection-cert</c> secret, so a copy of the key folder is useless without it (Principle I).
/// </summary>
public static class DataProtectionSetup
{
    /// <summary>The configuration key holding the base64 PFX certificate.</summary>
    public const string CertificateKey = "DataProtection:Certificate";

    /// <summary>The configuration key holding the certificate password.</summary>
    public const string CertificatePasswordKey = "DataProtection:CertificatePassword";

    /// <summary>The configuration key for the folder that holds the key ring (a volume when deployed).</summary>
    public const string KeysPathKey = "DataProtection:KeysPath";

    /// <summary>Registers Data Protection for the Web app.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">No certificate is configured outside development.</exception>
    public static WebApplicationBuilder AddTaskifyDataProtection(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var keysPath = builder.Configuration[KeysPathKey]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Taskify", "web-keys");

        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("Taskify.Web")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        var certificate = builder.Configuration[CertificateKey];
        if (!string.IsNullOrWhiteSpace(certificate))
        {
            dataProtection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12(
                Convert.FromBase64String(certificate),
                builder.Configuration[CertificatePasswordKey]));
        }
        else if (!builder.Environment.IsDevelopment())
        {
            // Fail closed: never run a deployment with an unprotected key ring.
            throw new InvalidOperationException($"{CertificateKey} is required outside development (see scripts/init-dev-secrets.ps1).");
        }

        return builder;
    }
}
