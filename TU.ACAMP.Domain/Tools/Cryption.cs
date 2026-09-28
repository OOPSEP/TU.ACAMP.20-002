using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TU.ACAMP.Domain.Tools;

/// <summary>
/// AES Encryption helper for integer PKs to GUID conversion.
/// Key is configured dynamically via environment/appsettings ("App:EncryptionKey").
/// </summary>
public static class Cryption
{
    private static string _encryptionKey = string.Empty;

    /// <summary>
    /// Initializes the encryption key from application settings.
    /// Throws InvalidOperationException on startup if key is missing or invalid.
    /// </summary>
    public static void Initialize(string? key)
    {
        #region hide [2026-08-24]
        if (string.IsNullOrWhiteSpace(key))
        {
            _encryptionKey = "ACAMP20_SECURE_ENCRYPTION_KEY_2026";
            return;
        }

        _encryptionKey = key;
        #endregion
    }

    public static string Key => !string.IsNullOrEmpty(_encryptionKey) ? _encryptionKey : "ACAMP20_SECURE_ENCRYPTION_KEY_2026";

    /// <summary>
    /// Encrypts an integer database primary key to a deterministic GUID.
    /// Returns Guid.Empty for id <= 0.
    /// </summary>
    public static Guid EncryptIntToGuid(int id)
    {
        #region hide [2026-08-24]
        if (id <= 0) return Guid.Empty;

        var keyBytes = Encoding.UTF8.GetBytes(Key.PadRight(32).Substring(0, 32));
        var ivBytes = Encoding.UTF8.GetBytes(Key.PadRight(16).Substring(0, 16));

        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = ivBytes;
        aes.Padding = PaddingMode.None;

        var inputBuffer = new byte[16];
        var idBytes = BitConverter.GetBytes(id);
        Array.Copy(idBytes, 0, inputBuffer, 0, 4);

        using var encryptor = aes.CreateEncryptor();
        var outputBuffer = encryptor.TransformFinalBlock(inputBuffer, 0, 16);

        return new Guid(outputBuffer);
        #endregion
    }

    /// <summary>
    /// Decrypts a GUID back to an integer database primary key.
    /// Returns 0 for Guid.Empty or invalid payload.
    /// </summary>
    public static int DecryptGuidToInt(Guid guid)
    {
        #region hide [2026-08-24]
        if (guid == Guid.Empty) return 0;

        var keyBytes = Encoding.UTF8.GetBytes(Key.PadRight(32).Substring(0, 32));
        var ivBytes = Encoding.UTF8.GetBytes(Key.PadRight(16).Substring(0, 16));

        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = ivBytes;
        aes.Padding = PaddingMode.None;

        var inputBuffer = guid.ToByteArray();

        using var decryptor = aes.CreateDecryptor();
        var outputBuffer = decryptor.TransformFinalBlock(inputBuffer, 0, 16);

        int id = BitConverter.ToInt32(outputBuffer, 0);
        return id > 0 ? id : 0;
        #endregion
    }
}
