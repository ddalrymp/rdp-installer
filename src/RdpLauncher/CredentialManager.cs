using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace RdpLauncher;

/// <summary>
/// Manages user credentials (OrgId, UserId, encrypted password) using the
/// Windows registry and DPAPI for password protection.
/// </summary>
public sealed class CredentialManager
{
    private const string RegistrySubKey = @"Software\RdpLauncher";

    public string OrgId { get; private set; } = "";
    public string UserId { get; private set; } = "";
    public string Username => string.IsNullOrEmpty(OrgId) || string.IsNullOrEmpty(UserId)
        ? ""
        : $"{OrgId}_{UserId}";

    public bool HasCredentials => !string.IsNullOrEmpty(OrgId) && !string.IsNullOrEmpty(UserId);
    public bool HasPassword => _password != null;

    private string? _password;

    /// <summary>
    /// Loads OrgId, UserId, and encrypted password from the registry.
    /// </summary>
    public void Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey);
            if (key == null) return;

            OrgId = key.GetValue("OrgId") as string ?? "";
            UserId = key.GetValue("UserId") as string ?? "";

            var encryptedBase64 = key.GetValue("EncryptedPassword") as string;
            if (!string.IsNullOrEmpty(encryptedBase64))
            {
                _password = DecryptPassword(encryptedBase64);
            }
        }
        catch
        {
            // Registry not available — leave defaults
        }
    }

    /// <summary>
    /// Saves OrgId and UserId to the registry. Does NOT save password here.
    /// </summary>
    public void SaveIdentity(string orgId, string userId)
    {
        OrgId = orgId.Trim().ToUpperInvariant();
        UserId = userId.Trim().ToUpperInvariant();

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey);
            key.SetValue("OrgId", OrgId);
            key.SetValue("UserId", UserId);
        }
        catch
        {
            // Non-fatal
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
    /// Only call this after a successful connection.
    /// </summary>
    public void SavePassword()
    {
        if (string.IsNullOrEmpty(_password)) return;

        try
        {
            var encrypted = EncryptPassword(_password);
            using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey);
            key.SetValue("EncryptedPassword", encrypted);
        }
        catch
        {
            // Non-fatal: user will need to re-enter next time
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
        OrgId = "";
        UserId = "";
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
