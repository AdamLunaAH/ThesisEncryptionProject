using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// Shared AES-256-GCM encrypt/decrypt logic used by all hybrid cipher implementations.
/// Ciphertext framing: [12-byte nonce | 16-byte GCM tag | encrypted payload]
/// </summary>
internal static class HybridAesGcmHelper
{
    private const int NonceSize = 12; // 96-bit nonce — recommended for AES-GCM
    private const int TagSize = 16; // 128-bit authentication tag

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with <paramref name="aesKey"/> (32 bytes).
    /// Returns framed bytes: [nonce | tag | ciphertext].
    /// </summary>
    internal static byte[] Encrypt(byte[] plaintext, byte[] aesKey)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintext.Length];

        using var gcm = new AesGcm(aesKey, TagSize);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        ciphertext.CopyTo(result, NonceSize + TagSize);
        return result;
    }

    /// <summary>
    /// Decrypts framed bytes produced by <see cref="Encrypt"/>.
    /// </summary>
    internal static byte[] Decrypt(byte[] framed, byte[] aesKey)
    {
        var nonce = framed.AsSpan(0, NonceSize);
        var tag = framed.AsSpan(NonceSize, TagSize);
        var ciphertext = framed.AsSpan(NonceSize + TagSize);

        byte[] plaintext = new byte[ciphertext.Length];

        using var gcm = new AesGcm(aesKey, TagSize);
        gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
