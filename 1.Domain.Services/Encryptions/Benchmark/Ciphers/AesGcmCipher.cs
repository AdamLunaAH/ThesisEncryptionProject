using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// AES-GCM cipher using the .NET BCL <see cref="AesGcm"/> primitive.
/// Provides authenticated encryption (AEAD) — authentication tag is always verified
/// during decryption, so a separate <see cref="IMessageAuthenticator"/> is not required.
///
/// Key sizes: 128 or 256 bits.
/// Nonce: 96 bits (12 bytes) — random per message.
/// Tag:   128 bits (16 bytes).
///
/// Ciphertext framing: [12-byte nonce | 16-byte GCM tag | ciphertext]
/// (same framing as <see cref="Ciphers.HybridAesGcmHelper"/>)
/// </summary>
public sealed class AesGcmCipher : IMessageCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;
    private readonly int _keySizeBits;

    /// <param name="keySizeBits">128 or 256.</param>
    public AesGcmCipher(int keySizeBits)
    {
        if (keySizeBits != 128 && keySizeBits != 256)
            throw new ArgumentException("AES-GCM key size must be 128 or 256 bits.", nameof(keySizeBits));

        _keySizeBits = keySizeBits;
        _key = RandomNumberGenerator.GetBytes(keySizeBits / 8);
    }

    public string AlgorithmId => _keySizeBits == 128 ? AlgorithmIds.Aes128Gcm : AlgorithmIds.Aes256Gcm;
    public string AlgorithmFamily => Benchmark.AlgorithmFamily.Symmetric;
    public string Generation => AlgorithmGeneration.Modern;
    public int KeySizeBits => _keySizeBits;
    public int MaxMessageBytes => int.MaxValue;
    public bool IsHybrid => false;

    public CipherResult Encrypt(byte[] plaintext)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintext.Length]; // GCM is a stream mode — same length

        using var gcm = new AesGcm(_key, TagSize);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Frame: [nonce | tag | ciphertext]
        byte[] framed = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(framed, 0);
        tag.CopyTo(framed, NonceSize);
        ciphertext.CopyTo(framed, NonceSize + TagSize);
        return new CipherResult(framed);
    }

    public byte[] Decrypt(byte[] ciphertext, byte[]? encapsulatedKey = null)
    {
        var nonce = ciphertext.AsSpan(0, NonceSize);
        var tag = ciphertext.AsSpan(NonceSize, TagSize);
        var encrypted = ciphertext.AsSpan(NonceSize + TagSize);

        byte[] plaintext = new byte[encrypted.Length];

        using var gcm = new AesGcm(_key, TagSize);
        gcm.Decrypt(nonce, encrypted, tag, plaintext);
        return plaintext;
    }

    public void Dispose() { }
}
