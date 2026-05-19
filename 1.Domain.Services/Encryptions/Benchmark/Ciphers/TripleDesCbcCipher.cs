using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// 3DES-CBC cipher using the .NET BCL <see cref="TripleDES"/> primitive.
///
/// Key sizes:
///   128 bits (16 bytes) → two-key 3DES, 112-bit effective security.
///   192 bits (24 bytes) → three-key 3DES, 168-bit effective security.
///
/// Block size: 64 bits — susceptible to the SWEET32 birthday attack after ~4 GB
/// of data encrypted under the same key/IV.
/// NIST deprecated 3DES for new applications in SP 800-131A Rev. 2 (2019).
///
/// Ciphertext framing: [8-byte IV | PKCS#7-padded ciphertext]
/// </summary>
public sealed class TripleDesCbcCipher : IMessageCipher
{
    private const int IvSize = 8; // 3DES block size in bytes

    private readonly byte[] _key;
    private readonly int _keySizeBits;

    /// <param name="keySizeBits">128 or 192.</param>
    public TripleDesCbcCipher(int keySizeBits)
    {
        if (keySizeBits != 128 && keySizeBits != 192)
            throw new ArgumentException("3DES key size must be 128 or 192 bits.", nameof(keySizeBits));

        _keySizeBits = keySizeBits;
        _key = RandomNumberGenerator.GetBytes(keySizeBits / 8);
    }

    public string AlgorithmId => _keySizeBits == 128 ? AlgorithmIds.TripleDes128Cbc : AlgorithmIds.TripleDes192Cbc;
    public string AlgorithmFamily => Benchmark.AlgorithmFamily.Symmetric;
    public string Generation => AlgorithmGeneration.Deprecated;
    public int KeySizeBits => _keySizeBits;
    public int MaxMessageBytes => int.MaxValue;
    public bool IsHybrid => false;

    public CipherResult Encrypt(byte[] plaintext)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);

        using var des = TripleDES.Create();
        des.Key = _key;
        des.IV = iv;
        des.Mode = CipherMode.CBC;
        des.Padding = PaddingMode.PKCS7;

        using var encryptor = des.CreateEncryptor();
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

        using var des = TripleDES.Create();
        des.Key = _key;
        des.IV = iv;
        des.Mode = CipherMode.CBC;
        des.Padding = PaddingMode.PKCS7;

        using var decryptor = des.CreateDecryptor();
        return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
    }

    public void Dispose() { }
}
