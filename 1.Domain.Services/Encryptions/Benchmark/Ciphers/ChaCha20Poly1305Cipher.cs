using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// ChaCha20-Poly1305 AEAD cipher using the .NET BCL <see cref="ChaCha20Poly1305"/> primitive.
///
/// Key size: 256 bits (32 bytes) — fixed.
/// Nonce: 96 bits (12 bytes) — random per message.
/// Tag:   128 bits (16 bytes).
///
/// Preferred over AES-GCM on platforms without hardware AES acceleration (e.g. older ARM).
/// Mandated in TLS 1.3 alongside AES-GCM.
/// Authentication is built in — no separate <see cref="IMessageAuthenticator"/> needed.
///
/// Ciphertext framing: [12-byte nonce | 16-byte Poly1305 tag | ciphertext]
/// </summary>
public sealed class ChaCha20Poly1305Cipher : IMessageCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // ChaCha20 only supports 256-bit keys

    private readonly byte[] _key;

    public ChaCha20Poly1305Cipher()
    {
        _key = RandomNumberGenerator.GetBytes(KeySize);
    }

    public string AlgorithmId => AlgorithmIds.ChaCha20Poly1305;
    public string AlgorithmFamily => Benchmark.AlgorithmFamily.Symmetric;
    public string Generation => AlgorithmGeneration.Modern;
    public int KeySizeBits => 256;
    public int MaxMessageBytes => int.MaxValue;
    public bool IsHybrid => false;

    public CipherResult Encrypt(byte[] plaintext)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintext.Length]; // stream cipher — same length

        using var chacha = new ChaCha20Poly1305(_key);
        chacha.Encrypt(nonce, plaintext, ciphertext, tag);

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

        using var chacha = new ChaCha20Poly1305(_key);
        chacha.Decrypt(nonce, encrypted, tag, plaintext);
        return plaintext;
    }

    public void Dispose() { }
}
