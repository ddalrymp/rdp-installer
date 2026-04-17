using System.Security.Cryptography.X509Certificates;

namespace RdpLauncher;

/// <summary>
/// Manages certificate trust for RDP connections.
/// Imports signing certificates into the CurrentUser TrustedPublisher store
/// so that signed .rdp files are trusted without security prompts.
/// </summary>
public sealed class CertificateManager
{
    /// <summary>
    /// Checks whether a certificate with the given thumbprint exists
    /// in the CurrentUser TrustedPublisher store.
    /// </summary>
    public static bool IsCertificateTrusted(string thumbprint)
    {
        using var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var found = store.Certificates.Find(
            X509FindType.FindByThumbprint, thumbprint, validOnly: false);

        return found.Count > 0;
    }

    /// <summary>
    /// Imports a certificate (.cer file) into the CurrentUser TrustedPublisher store.
    /// Returns true if the import succeeded, false otherwise.
    /// </summary>
    public static bool ImportCertificate(string certFilePath)
    {
        if (!File.Exists(certFilePath))
            return false;

        try
        {
            var cert = new X509Certificate2(certFilePath);
            using var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
            store.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads a certificate from a URL and imports it into the TrustedPublisher store.
    /// Also saves the certificate to the local cache directory.
    /// </summary>
    public static async Task<bool> DownloadAndImportCertificateAsync(
        string certUrl, string cacheDir, HttpClient? httpClient = null)
    {
        var client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var shouldDispose = httpClient == null;

        try
        {
            var certBytes = await client.GetByteArrayAsync(certUrl);

            Directory.CreateDirectory(cacheDir);
            var cachedCertPath = Path.Combine(cacheDir, "signing-cert.cer");
            await File.WriteAllBytesAsync(cachedCertPath, certBytes);

            var cert = new X509Certificate2(certBytes);
            using var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
            store.Close();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (shouldDispose)
                client.Dispose();
        }
    }

    /// <summary>
    /// Imports a certificate from the local cache if it exists.
    /// Used as a fallback when the network is unavailable.
    /// </summary>
    public static bool ImportFromCache(string cacheDir)
    {
        var cachedCertPath = Path.Combine(cacheDir, "signing-cert.cer");
        return ImportCertificate(cachedCertPath);
    }

    /// <summary>
    /// Removes a certificate with the given thumbprint from the TrustedPublisher store.
    /// Used during uninstallation.
    /// </summary>
    public static bool RemoveCertificate(string thumbprint)
    {
        try
        {
            using var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            var found = store.Certificates.Find(
                X509FindType.FindByThumbprint, thumbprint, validOnly: false);

            foreach (var cert in found)
            {
                store.Remove(cert);
            }

            store.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
