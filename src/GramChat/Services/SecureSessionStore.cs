using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GramChat.Services;

/// <summary>
/// One stored login: the encrypted session payload (platform-specific) plus the
/// account metadata that is safe to keep readable inside the database.
/// </summary>
/// <param name="UserId">Instagram user id (the <c>ds_user_id</c> cookie value), if known.</param>
/// <param name="Username">Instagram username, best-effort (empty when unknown).</param>
/// <param name="DisplayName">Full name, best-effort.</param>
/// <param name="AvatarUrl">Profile picture URL, best-effort.</param>
/// <param name="SessionData">
/// Encrypted-at-rest session payload. On Windows it is a JSON array of cookies;
/// on Linux it is base64 of WebKitGTK's <c>cookies.sqlite</c> (+ WAL/SHM)
/// files. The whole database is AES-256 encrypted, so this column is opaque.
/// </param>
/// <param name="LastLogin">When the session was last captured.</param>
public sealed record StoredSession(
    string UserId,
    string Username,
    string DisplayName,
    string AvatarUrl,
    string SessionData,
    DateTimeOffset LastLogin);

/// <summary>
/// Serializable cookie record used by the Windows client (WebView2). Kept here so
/// both .NET projects compile the same file, mirroring how <c>NavigationPolicy.cs</c>
/// and <c>PageHardening.cs</c> are shared verbatim between the clients.
/// </summary>
public sealed record CookieRecord(
    string Name,
    string Value,
    string Domain,
    string Path,
    bool HttpOnly,
    bool Secure,
    long ExpiresUnixSeconds, // 0 == session cookie
    int SameSite);           // CoreWebView2CookieSameSiteKind value (0=None,1=Lax,2=Strict)

/// <summary>
/// Encrypted SQLite database (SQLCipher, AES-256) holding the user's saved
/// logins. The database key is generated per device and protected by the
/// platform key store (Windows DPAPI / Linux 0600 key file - the same model
/// Chromium uses for cookie encryption on each OS). Nothing - not even the
/// session payload - is ever written to disk in plain text.
///
/// Same schema and behaviour on Android (Kotlin mirror:
/// <c>android/.../security/SecureSessionStore.kt</c>).
/// </summary>
public static class SecureSessionStore
{
    private const string DbFileName = "sessions.db";
    private const string KeyFileName = "session-key.bin";
    private const string SchemaVersion = "1";

    /// <summary>JS that returns a JSON string with the signed-in viewer profile (or "null").</summary>
    public const string ViewerProfileScript = """
        (() => {
          try {
            const v = window._sharedData && window._sharedData.config && window._sharedData.config.viewer;
            if (!v) return 'null';
            return JSON.stringify({
              userId: String(v.id || ''),
              username: v.username || '',
              displayName: v.full_name || '',
              avatarUrl: (v.profile_pic_url || '').replace(/^http:\/\//, 'https://')
            });
          } catch (e) { return 'null'; }
        })()
        """;

    /// <summary>Opens/creates the encrypted database and returns true when usable.</summary>
    public static async Task<bool> InitializeAsync(string dataDir)
    {
        SQLitePCL.Batteries_V2.Init();
        try
        {
            Directory.CreateDirectory(dataDir);
            RestrictDirectoryPermissions(dataDir);

            var key = SessionKeyProtector.GetOrCreateKey(Path.Combine(dataDir, KeyFileName));

            // A previous key mismatch (key file replaced) makes the DB undecryptable;
            // move it aside so the app can start fresh instead of failing forever.
            var dbPath = Path.Combine(dataDir, DbFileName);
            if (File.Exists(dbPath) && !CanOpenWithKey(dbPath, key))
            {
                // The failed key-check connection can release its native file
                // handle lazily; retry briefly before giving up on recovery.
                SqliteConnection.ClearAllPools();
                var backup = $"{dbPath}.undecryptable-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                var moved = false;
                for (var attempt = 0; attempt < 10 && !moved; attempt++)
                {
                    try
                    {
                        File.Move(dbPath, backup);
                        moved = true;
                    }
                    catch (IOException) when (attempt < 9)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        Thread.Sleep(150);
                    }
                }

                if (!moved)
                {
                    // Leave the old database untouched; the store stays unusable
                    // but the app itself keeps working (login just isn't saved).
                    return false;
                }
            }

            await using (var con = Open(dataDir, key))
            {
                await con.OpenAsync();
                await using var cmd = con.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS accounts (
                        user_id       TEXT PRIMARY KEY,
                        username      TEXT NOT NULL DEFAULT '',
                        display_name  TEXT NOT NULL DEFAULT '',
                        avatar_url    TEXT NOT NULL DEFAULT '',
                        session_data  TEXT NOT NULL,
                        created_at    INTEGER NOT NULL,
                        last_login_at INTEGER NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS meta (
                        key   TEXT PRIMARY KEY,
                        value TEXT NOT NULL
                    );
                    INSERT OR IGNORE INTO meta(key, value) VALUES ('schema_version', $version);
                    """;
                cmd.Parameters.AddWithValue("$version", SchemaVersion);
                await cmd.ExecuteNonQueryAsync();
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SecureSessionStore init failed: {ex}");
            return false;
        }
    }

    public static async Task SaveSessionAsync(string dataDir, StoredSession session)
    {
        await using var con = Open(dataDir, SessionKeyProtector.GetOrCreateKey(Path.Combine(dataDir, KeyFileName)));
        await con.OpenAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT INTO accounts(user_id, username, display_name, avatar_url, session_data, created_at, last_login_at)
            VALUES ($userId, $username, $displayName, $avatarUrl, $sessionData, $createdAt, $lastLogin)
            ON CONFLICT(user_id) DO UPDATE SET
                username      = excluded.username,
                display_name  = excluded.display_name,
                avatar_url    = excluded.avatar_url,
                session_data  = excluded.session_data,
                last_login_at = excluded.last_login_at;
            """;
        cmd.Parameters.AddWithValue("$userId", session.UserId);
        cmd.Parameters.AddWithValue("$username", session.Username);
        cmd.Parameters.AddWithValue("$displayName", session.DisplayName);
        cmd.Parameters.AddWithValue("$avatarUrl", session.AvatarUrl);
        cmd.Parameters.AddWithValue("$sessionData", session.SessionData);
        cmd.Parameters.AddWithValue("$createdAt", session.LastLogin.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$lastLogin", session.LastLogin.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<StoredSession?> GetLatestSessionAsync(string dataDir)
    {
        await using var con = Open(dataDir, SessionKeyProtector.GetOrCreateKey(Path.Combine(dataDir, KeyFileName)));
        await con.OpenAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT user_id, username, display_name, avatar_url, session_data, last_login_at
            FROM accounts ORDER BY last_login_at DESC LIMIT 1;
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new StoredSession(
            UserId: reader.GetString(0),
            Username: reader.GetString(1),
            DisplayName: reader.GetString(2),
            AvatarUrl: reader.GetString(3),
            SessionData: reader.GetString(4),
            LastLogin: DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)));
    }

    public static async Task<List<StoredSession>> GetAllSessionsAsync(string dataDir)
    {
        await using var con = Open(dataDir, SessionKeyProtector.GetOrCreateKey(Path.Combine(dataDir, KeyFileName)));
        await con.OpenAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT user_id, username, display_name, avatar_url, session_data, last_login_at FROM accounts ORDER BY last_login_at DESC;";
        await using var reader = await cmd.ExecuteReaderAsync();
        var sessions = new List<StoredSession>();
        while (await reader.ReadAsync())
        {
            sessions.Add(new StoredSession(
                UserId: reader.GetString(0),
                Username: reader.GetString(1),
                DisplayName: reader.GetString(2),
                AvatarUrl: reader.GetString(3),
                SessionData: reader.GetString(4),
                LastLogin: DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5))));
        }

        return sessions;
    }

    public static async Task DeleteSessionAsync(string dataDir, string userId)
    {
        await using var con = Open(dataDir, SessionKeyProtector.GetOrCreateKey(Path.Combine(dataDir, KeyFileName)));
        await con.OpenAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM accounts WHERE user_id = $userId;";
        cmd.Parameters.AddWithValue("$userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Removes every stored login ("Log out &amp; clear local data").</summary>
    public static async Task ClearAllAsync(string dataDir)
    {
        await using var con = Open(dataDir, SessionKeyProtector.GetOrCreateKey(Path.Combine(dataDir, KeyFileName)));
        await con.OpenAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM accounts;";
        await cmd.ExecuteNonQueryAsync();
    }

    // ----------------------------------------------------------------------
    // Cookie helpers shared by the .NET clients
    // ----------------------------------------------------------------------

    public static string SerializeCookies(IEnumerable<CookieRecord> cookies)
        => JsonSerializer.Serialize(cookies);

    public static List<CookieRecord> DeserializeCookies(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CookieRecord>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>JSON array of cookie name/value pairs from a "Set-Cookie"-style header string (Android mirror uses the same shape).</summary>
    public static string SerializeCookieHeader(string cookieHeader) => JsonSerializer.Serialize(
        cookieHeader
            .Split(';')
            .Select(pair => pair.Trim())
            .Where(pair => pair.Contains('='))
            .Select(pair =>
            {
                var eq = pair.IndexOf('=');
                return new CookieRecord(
                    pair[..eq].Trim(),
                    pair[(eq + 1)..].Trim(),
                    string.Empty, string.Empty, false, false, 0, 1);
            }));

    /// <summary>Reads the WebKitGTK cookie database (plain SQLite) and returns the sessionid/ds_user_id values, if present.</summary>
    public static (string SessionId, string UserId)? ReadWebKitCookieDb(string cookiesSqlitePath)
    {
        try
        {
            using var con = new SqliteConnection($"Data Source={cookiesSqlitePath};Mode=ReadOnly");
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT name, value FROM cookies WHERE name IN ('sessionid', 'ds_user_id') AND length(value) > 0;";
            using var reader = cmd.ExecuteReader();
            string sessionId = string.Empty, userId = string.Empty;
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var value = reader.GetString(1);
                if (name == "sessionid") sessionId = value;
                if (name == "ds_user_id") userId = value;
            }

            return string.IsNullOrEmpty(sessionId) ? null : (sessionId, userId);
        }
        catch
        {
            return null; // not a valid cookie db (yet)
        }
    }

    // ----------------------------------------------------------------------
    // Internals
    // ----------------------------------------------------------------------

    private static SqliteConnection Open(string dataDir, string keyHex)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDir, DbFileName),
            Mode = SqliteOpenMode.ReadWriteCreate,
            // A 64-char hex string is used by SQLCipher directly as the raw 256-bit key.
            Password = keyHex,
        };
        return new SqliteConnection(csb.ConnectionString);
    }

    private static bool CanOpenWithKey(string dbPath, string keyHex)
    {
        try
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Password = keyHex,
                // Probe connection: never pool it, so the file handle is released
                // on dispose and the undecryptable DB can be moved aside.
                Pooling = false,
            };
            using var con = new SqliteConnection(csb.ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            cmd.ExecuteScalar();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void RestrictDirectoryPermissions(string dataDir)
    {
        // Linux: the profile folder holds the WebKitGTK cookie DB while the app
        // runs (WebKitGTK has no at-rest cookie encryption, unlike Chromium).
        // Restrict it to the owning user, same model as Chromium's Linux profile.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(dataDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
            // Best-effort (e.g. filesystem without unix modes).
        }
    }
}