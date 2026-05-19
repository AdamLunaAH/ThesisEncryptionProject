using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// RSA-OAEP-SHA256 cipher with two modes selectable at construction time.
///
/// Non-hybrid mode (<c>hybrid: false</c>):
///   Encrypts the plaintext directly with RSA-OAEP. Fast for small payloads but
///   limited by the OAEP padding overhead — usable payload is:
///     2048-bit key → ~190 bytes  |  4096-bit key → ~446 bytes
///   Throws <see cref="ArgumentException"/> if plaintext exceeds <see cref="MaxMessageBytes"/>.
///   <c>CipherResult.EncapsulatedKey</c> is <c>null</c>.
///
/// Hybrid mode (<c>hybrid: true</c>):
///   Generates a fresh random 256-bit AES session key per message, encrypts the
///   plaintext with AES-256-GCM, then wraps the session key with RSA-OAEP.
///   No plaintext size limit. <c>CipherResult.EncapsulatedKey</c> holds the
///   RSA-wrapped session key.
///
/// NOTE: RSA key generation is intentionally performed in the constructor so that
/// key-generation cost is paid once per benchmark run (not per message).
/// For 4096-bit keys this can take ~500 ms on first call.
/// </summary>
public sealed class RsaOaepCipher : IMessageCipher
{
    private readonly RSA _rsa;
    private readonly int _keySizeBits;
    private readonly bool _hybrid;
    private readonly int _maxMessageBytes;

    // OAEP-SHA256 overhead: 2 * hash_size (32) + 2 = 66 bytes
    private static int OaepMaxBytes(int keySizeBits) => (keySizeBits / 8) - 66;

    /// <param name="keySizeBits">2048 or 4096.</param>
    /// <param name="hybrid">
    ///   <c>false</c> = direct RSA encrypt (size-limited).
    ///   <c>true</c>  = RSA wraps AES-256-GCM session key (no size limit).
    /// </param>
    public RsaOaepCipher(int keySizeBits, bool hybrid)
    {
        if (keySizeBits != 2048 && keySizeBits != 4096)
            throw new ArgumentException("RSA key size must be 2048 or 4096 bits.", nameof(keySizeBits));

        _keySizeBits = keySizeBits;
        _hybrid = hybrid;
        _maxMessageBytes = hybrid ? int.MaxValue : OaepMaxBytes(keySizeBits);

        _rsa = RSA.Create(keySizeBits);
    }

    public string AlgorithmId => (_keySizeBits, _hybrid) switch
    {
        (2048, false) => AlgorithmIds.Rsa2048,
        (4096, false) => AlgorithmIds.Rsa4096,
        (2048, true) => AlgorithmIds.Rsa2048Hybrid,
        _ => AlgorithmIds.Rsa4096Hybrid
    };

    public string AlgorithmFamily => _hybrid ? Benchmark.AlgorithmFamily.Hybrid : Benchmark.AlgorithmFamily.Asymmetric;
    public string Generation => AlgorithmGeneration.Legacy;
    public int KeySizeBits => _keySizeBits;
    public int MaxMessageBytes => _maxMessageBytes;
    public bool IsHybrid => _hybrid;

    public CipherResult Encrypt(byte[] plaintext)
    {
        if (plaintext.Length > _maxMessageBytes)
            throw new ArgumentException(
                $"{AlgorithmId} cannot encrypt {plaintext.Length} bytes; maximum is {_maxMessageBytes} bytes. " +
                "Use the hybrid variant (RSA-*-Hybrid) to remove this restriction.",
                nameof(plaintext));

        if (!_hybrid)
        {
            byte[] ciphertext = _rsa.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256);
            return new CipherResult(ciphertext);
        }
        else
        {
            // Generate a fresh AES-256 session key for this message (perfect forward secrecy per-message)
            byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
            byte[] encapsulatedKey = _rsa.Encrypt(sessionKey, RSAEncryptionPadding.OaepSHA256);
            byte[] ciphertext = HybridAesGcmHelper.Encrypt(plaintext, sessionKey);
            return new CipherResult(ciphertext, encapsulatedKey);
        }
    }

    public byte[] Decrypt(byte[] ciphertext, byte[]? encapsulatedKey = null)
    {
        if (!_hybrid)
        {
            return _rsa.Decrypt(ciphertext, RSAEncryptionPadding.OaepSHA256);
        }
        else
        {
            if (encapsulatedKey is null)
                throw new ArgumentNullException(nameof(encapsulatedKey),
                    "Hybrid RSA decryption requires the encapsulated session key.");

            byte[] sessionKey = _rsa.Decrypt(encapsulatedKey, RSAEncryptionPadding.OaepSHA256);
            return HybridAesGcmHelper.Decrypt(ciphertext, sessionKey);
        }
    }

    public void Dispose() => _rsa.Dispose();
}
