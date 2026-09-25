package com.gramchat.app.security

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Protects the 256-bit master key that encrypts the session database
 * (see [SecureSessionStore]).
 *
 * The master key never leaves the device as plain text: it is wrapped
 * (AES/GCM) by a key that lives in the Android Keystore — hardware-backed on
 * supported devices — and only the wrapped blob is persisted in private
 * SharedPreferences. If the Keystore entry is lost (e.g. after a factory
 * reset), the old wrapped key cannot be unwrapped and a fresh master key is
 * minted; the old database simply stays unreadable.
 *
 * This is the Android counterpart of DPAPI (Windows) / the 0600 key file
 * (Linux) used by the .NET clients.
 */
object SessionKeyProtector {

    private const val ANDROID_KEYSTORE = "AndroidKeyStore"
    private const val KEY_ALIAS = "gramchat-session-master"
    private const val TRANSFORMATION = "AES/GCM/NoPadding"
    private const val GCM_TAG_BITS = 128
    private const val IV_LENGTH = 12
    private const val MASTER_KEY_BYTES = 32 // 256-bit AES key for SQLCipher
    private const val PREFS = "gramchat_security"
    private const val PREF_WRAPPED = "wrapped_master_key"

    /** Returns the 256-bit master key, creating and wrapping it on first use. */
    fun unlock(context: Context): ByteArray {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        val wrappingKey = getOrCreateWrappingKey(keyStore)
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

        val wrapped = prefs.getString(PREF_WRAPPED, null)
        if (wrapped != null) {
            try {
                return unwrap(wrappingKey, Base64.decode(wrapped, Base64.NO_WRAP))
            } catch (_: Exception) {
                // Keystore entry gone (device reset / app data moved): mint a
                // fresh master key; the old database stays undecryptable.
            }
        }

        val fresh = ByteArray(MASTER_KEY_BYTES).also { SecureRandom().nextBytes(it) }
        prefs.edit()
            .putString(PREF_WRAPPED, Base64.encodeToString(wrap(wrappingKey, fresh), Base64.NO_WRAP))
            .apply()
        return fresh
    }

    private fun getOrCreateWrappingKey(keyStore: KeyStore): SecretKey {
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }

        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return generator.generateKey()
    }

    private fun wrap(key: SecretKey, master: ByteArray): ByteArray {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key)
        val iv = cipher.iv
        val ciphertext = cipher.doFinal(master)
        return iv + ciphertext
    }

    private fun unwrap(key: SecretKey, blob: ByteArray): ByteArray {
        val iv = blob.copyOfRange(0, IV_LENGTH)
        val ciphertext = blob.copyOfRange(IV_LENGTH, blob.size)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(GCM_TAG_BITS, iv))
        return cipher.doFinal(ciphertext)
    }
}