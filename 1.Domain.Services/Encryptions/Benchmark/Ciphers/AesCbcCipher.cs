using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// AES-CBC cipher using the .NET BCL <see cref="Aes"/> primitive.
/// Hardware-accelerated via AES-NI on x86/ARM where available.
///
/// Key sizes: 128, 192, or 256 bits.
/// Block size: 128 bits.
/// Mode: CBC with PKCS#7 padding (unauthenticated — pair with an
/// <see cref="IMessageAuthenticator"/> in the benchmark if authentication is required).
///
/// Ciphertext framing: [16-byte IV | PKCS#7-padded ciphertext]
/// </summary>
public sealed class AesCbcCipher : IMessageCipher
{
    private const int IvSize = 16; // AES block size in bytes

    private readonly byte[] _key;
    private readonly int _keySizeBits;

    /// <param name="keySizeBits">128, 192, or 256.</param>
    public AesCbcCipher(int keySizeBits)
    {
        if (keySizeBits != 128 && keySizeBits != 192 && keySizeBits != 256)
            throw new ArgumentException("AES key size must be 128, 192, or 256 bits.", nameof(keySizeBits));

        _keySizeBits = keySizeBits;
        _key = RandomNumberGenerator.GetBytes(keySizeBits / 8);
    }

    public string AlgorithmId => _keySizeBits switch
    {
        128 => AlgorithmIds.Aes128Cbc,
        192 => AlgorithmIds.Aes192Cbc,
        _ => AlgorithmIds.Aes256Cbc
    };

    public string AlgorithmFamily => Benchmark.AlgorithmFamily.Symmetric;
    public string Generation => AlgorithmGeneration.Legacy;
    public int KeySizeBits => _keySizeBits;
    public int MaxMessageBytes => int.MaxValue;
    public bool IsHybrid => false;

    public CipherResult Encrypt(byte[] plaintext)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        byte[] encrypted = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        // Frame: [IV | ciphertext]
        byte[] framed = new byte[IvSize + encrypted.Length];
        iv.CopyTo(framed, 0);
        encrypted.CopyTo(framed, IvSize);
        return new CipherResult(framed);
    }

    public byte[] Decrypt(byte[] ciphertext, byte[]? encapsulatedKey = null)
    {
        byte[] iv = ciphertext[..IvSize];
        byte[] encrypted = ciphertext[IvSize..];

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
    }

    public void Dispose() { }
}
