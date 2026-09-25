package com.gramchat.app.security

import android.content.ContentValues
import android.content.Context
import net.zetetic.sqlcipher.database.SQLiteDatabase
import net.zetetic.sqlcipher.database.SQLiteOpenHelper

/** One saved login. [sessionData] holds the raw cookie header string ("a=b; c=d") — encrypted at rest by SQLCipher. */
data class StoredSession(
    val userId: String,
    val username: String,
    val displayName: String,
    val avatarUrl: String,
    val sessionData: String,
    val lastLoginUtc: Long
)

/**
 * Encrypted SQLite database (SQLCipher, AES-256) holding the user's saved
 * logins on Android. Mirror of the .NET [SecureSessionStore] used by the
 * Windows and Linux clients: identical schema and the same rule that the
 * Instagram password is never stored — only session cookies, encrypted at
 * rest. The passphrase is the 256-bit master key unwrapped from the Android
 * Keystore ([SessionKeyProtector]).
 */
class SecureSessionStore(context: Context) {

    private val appContext = context.applicationContext

    private val passphrase: ByteArray by lazy {
        SQLiteDatabase.loadLibs(appContext)
        SessionKeyProtector.unlock(appContext)
    }

    private val helper = object : SQLiteOpenHelper(appContext, DB_NAME, null, DB_VERSION) {
        override fun onCreate(db: SQLiteDatabase) {
            db.execSQL(
                """
                CREATE TABLE IF NOT EXISTS accounts (
                    user_id       TEXT PRIMARY KEY,
                    username      TEXT NOT NULL DEFAULT '',
                    display_name  TEXT NOT NULL DEFAULT '',
                    avatar_url    TEXT NOT NULL DEFAULT '',
                    session_data  TEXT NOT NULL,
                    created_at    INTEGER NOT NULL,
                    last_login_at INTEGER NOT NULL
                );
                """.trimIndent()
            )
        }

        override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) = Unit
    }

    @Synchronized
    fun saveSession(session: StoredSession) {
        val db = helper.writableDatabase(passphrase)
        val values = ContentValues().apply {
            put("user_id", session.userId)
            put("username", session.username)
            put("display_name", session.displayName)
            put("avatar_url", session.avatarUrl)
            put("session_data", session.sessionData)
            put("created_at", session.lastLoginUtc)
            put("last_login_at", session.lastLoginUtc)
        }
        db.insertWithOnConflict("accounts", null, values, SQLiteDatabase.CONFLICT_REPLACE)
    }

    @Synchronized
    fun latestSession(): StoredSession? {
        val db = helper.readableDatabase(passphrase)
        db.rawQuery(
            "SELECT user_id, username, display_name, avatar_url, session_data, last_login_at " +
                "FROM accounts ORDER BY last_login_at DESC LIMIT 1",
            null
        ).use { cursor ->
            if (!cursor.moveToFirst()) return null
            return StoredSession(
                userId = cursor.getString(0),
                username = cursor.getString(1),
                displayName = cursor.getString(2),
                avatarUrl = cursor.getString(3),
                sessionData = cursor.getString(4),
                lastLoginUtc = cursor.getLong(5)
            )
        }
    }

    /** Removes every stored login ("Log out & clear local data"). */
    @Synchronized
    fun clearAll() {
        helper.writableDatabase(passphrase).delete("accounts", null, null)
    }

    private companion object {
        const val DB_NAME = "sessions.db"
        const val DB_VERSION = 1
    }
}