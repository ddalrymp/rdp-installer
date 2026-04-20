using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace RdpLauncher;

/// <summary>
/// Manages user credentials (Organization, Username, encrypted Password) using the
/// Windows registry and DPAPI for password protection.
/// All three fields can be independently saved and loaded.
/// </summary>
public sealed class CredentialManager
{
    private const string RegistrySubKey = @"Software\RdpLauncher";

    public string Organization { get; private set; } = "";
    public string Username { get; private set; } = "";

    public bool HasIdentity => !string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(Username);
    public bool HasPassword => _password != null;

    private string? _password;

    /// <summary>
    /// Loads Organization, Username, and encrypted password from the registry.
    /// </summary>
    public void Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey);
            if (key == null) return;

            Organization = key.GetValue("Organization") as string ?? "";
            Username = key.GetValue("Username") as string ?? "";

            var encryptedBase64 = key.GetValue("EncryptedPassword") as string;
            if (!string.IsNullOrEmpty(encryptedBase64))
            {
                _password = DecryptPassword(encryptedBase64);
            }

            Logger.Debug($"Credentials loaded. Organization: {Organization}, Username: {Username}, HasPassword: {HasPassword}");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load credentials from registry", ex);
        }
    }

    /// <summary>
    /// Saves Organization and Username to the registry.
    /// </summary>
    public void SaveIdentity(string organization, string username)
    {
        Organization = organization.Trim();
        Username = username.Trim();

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey);
            key.SetValue("Organization", Organization);
            key.SetValue("Username", Username);
            Logger.Debug($"Identity saved. Organization: {Organization}, Username: {Username}");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save identity to registry", ex);
        }
    }

    /// <summary>
    /// Sets the in-memory password (call before connection attempt).
    /// </summary>
    public void SetPassword(string password)
    {
        _password = password;
    }

    /// <summary>
    /// Gets the current password (in-memory or loaded from registry).
    /// </summary>
    public string? GetPassword()
    {
        return _password;
    }

    /// <summary>
    /// Persists the password to the registry using DPAPI encryption.
    /// Call after a successful connection or when the user explicitly saves.
    /// </summary>
    public void SavePassword()
    {
        if (string.IsNullOrEmpty(_password)) return;

        try
        {
            var encrypted = EncryptPassword(_password);
            using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey);
            key.SetValue("EncryptedPassword", encrypted);
            Logger.Debug("Password saved to registry (DPAPI-encrypted).");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save password to registry", ex);
        }
    }

    /// <summary>
    /// Removes the saved password from registry and memory.
    /// </summary>
    public void ClearPassword()
    {
        _password = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey, writable: true);
            key?.DeleteValue("EncryptedPassword", throwOnMissingValue: false);
        }
        catch
        {
            // Non-fatal
        }
    }

    /// <summary>
    /// Clears all stored identity and credentials.
    /// </summary>
    public void ClearAll()
    {
        Organization = "";
        Username = "";
        _password = null;

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegistrySubKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // Non-fatal
        }
    }

    private static string EncryptPassword(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string? DecryptPassword(string encryptedBase64)
    {
        try
        {
            var encrypted = Convert.FromBase64String(encryptedBase64);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null;
        }
    }
}
