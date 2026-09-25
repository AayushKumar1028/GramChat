using System.IO;
using System.Security.Cryptography;

namespace GramChat.Services;

/// <summary>
/// Creates and protects the 256-bit key that encrypts the <see cref="SecureSessionStore"/>
/// database. Each platform uses the strongest key store it ships:
///
/// <list type="bullet">
///   <item>Windows - DPAPI (<see cref="System.Security.Cryptography.ProtectedData"/>,
///   CurrentUser scope, same mechanism Chromium uses for WebView2 cookies).</item>
///   <item>Linux - a 256-bit key file with 0600 permissions. This mirrors Chromium's
///   Linux behaviour (the "Local State" cookie key), and the surrounding database is
///   still AES-256 (SQLCipher) so the key file alone yields nothing readable.</item>
/// </list>
///
/// Android uses the hardware-backed Android Keystore instead (see
/// <c>android/.../security/SessionKeyProtector.kt</c>).
/// </summary>
public static class SessionKeyProtector
{
    // Fixed application entropy; DPAPI already scopes to the Windows user +
    // machine, this just prevents cross-app reuse of the same DPAPI blob.
    private static readonly byte[] Entropy = [0x49, 0x6E, 0x73, 0x74, 0x61, 0x43, 0x68, 0x61, 0x74, 0x2D, 0x53, 0x65, 0x73, 0x73, 0x69, 0x6F, 0x6E];

    private const int KeyLength = 32; // 256-bit AES key for SQLCipher

    /// <summary>
    /// Returns the database key as a 64-char hex string (SQLCipher raw-key form),
    /// creating and protecting it on first use.
    /// </summary>
    public static string GetOrCreateKey(string keyFilePath)
    {
        try
        {
            if (File.Exists(keyFilePath))
            {
                var stored = File.ReadAllBytes(keyFilePath);
                var key = UnprotectKey(stored);
                if (key is { Length: KeyLength })
                {
                    return Convert.ToHexString(key).ToLowerInvariant();
                }
            }
        }
        catch (Exception ex)
        {
            // A corrupt/unreadable key file is treated as absent below; the old
            // database (if any) will be detected as undecryptable on next init.
            System.Diagnostics.Debug.WriteLine($"SessionKeyProtector read failed: {ex}");
        }

        var fresh = RandomNumberGenerator.GetBytes(KeyLength);
        File.WriteAllBytes(keyFilePath, ProtectKey(fresh));
        RestrictFilePermissions(keyFilePath);
        return Convert.ToHexString(fresh).ToLowerInvariant();
    }

    private static byte[] ProtectKey(byte[] key)
    {
#if NET10_0_WINDOWS
        // DPAPI: decryptable only by the same Windows user on the same machine.
        return ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
#else
        // Linux (and any other non-Windows .NET target): the key file itself is
        // the protection boundary - 0600 makes it readable only by the owner.
        return key;
#endif
    }

    private static byte[]? UnprotectKey(byte[] stored)
    {
#if NET10_0_WINDOWS
        return ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.CurrentUser);
#else
        return stored;
#endif
    }

    private static void RestrictFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best-effort.
        }
    }
}