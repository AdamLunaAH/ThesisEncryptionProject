using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// Twofish-CBC cipher implemented via BouncyCastle.Cryptography.
///
/// Twofish was an AES finalist (1997–2000). It lost the competition to Rijndael (AES)
/// but remains unbroken. It is not natively supported by the .NET BCL, so this
/// implementation uses BouncyCastle — making it a software-only path even on
/// hardware that has AES-NI. The latency difference between this and AES-CBC on
/// the same hardware demonstrates the cost of lacking native CPU acceleration.
///
/// Key sizes: 128, 192, or 256 bits.
/// Block size: 128 bits.
/// Mode: CBC with PKCS#7 padding.
///
/// Ciphertext framing: [16-byte IV | PKCS#7-padded ciphertext]
/// </summary>
public sealed class TwofishCbcCipher : IMessageCipher
{
    private const int IvSize = 16; // Twofish block size in bytes (128 bits)

    private readonly byte[] _key;
    private readonly int _keySizeBits;

    /// <param name="keySizeBits">128, 192, or 256.</param>
    public TwofishCbcCipher(int keySizeBits)
    {
        if (keySizeBits != 128 && keySizeBits != 192 && keySizeBits != 256)
            throw new ArgumentException("Twofish key size must be 128, 192, or 256 bits.", nameof(keySizeBits));

        _keySizeBits = keySizeBits;
        _key = RandomNumberGenerator.GetBytes(keySizeBits / 8);
    }

    public string AlgorithmId => _keySizeBits switch
    {
        128 => AlgorithmIds.Twofish128Cbc,
        192 => AlgorithmIds.Twofish192Cbc,
        _ => AlgorithmIds.Twofish256Cbc
    };

    public string AlgorithmFamily => Benchmark.AlgorithmFamily.Symmetric;
    public string Generation => AlgorithmGeneration.Legacy;
    public int KeySizeBits => _keySizeBits;
    public int MaxMessageBytes => int.MaxValue;
    public bool IsHybrid => false;

    public CipherResult Encrypt(byte[] plaintext)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);

        var paddedCipher = BuildCipher(forEncryption: true, iv);
        byte[] output = new byte[paddedCipher.GetOutputSize(plaintext.Length)];
        int len = paddedCipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
        len += paddedCipher.DoFinal(output, len);

        // Frame: [IV | ciphertext]
        byte[] framed = new byte[IvSize + len];
        Buffer.BlockCopy(iv, 0, framed, 0, IvSize);
        Buffer.BlockCopy(output, 0, framed, IvSize, len);
        return new CipherResult(framed);
    }

    public byte[] Decrypt(byte[] ciphertext, byte[]? encapsulatedKey = null)
    {
        byte[] iv = ciphertext[..IvSize];
        byte[] encrypted = ciphertext[IvSize..];

        var paddedCipher = BuildCipher(forEncryption: false, iv);
        byte[] output = new byte[paddedCipher.GetOutputSize(encrypted.Length)];
        int len = paddedCipher.ProcessBytes(encrypted, 0, encrypted.Length, output, 0);
        len += paddedCipher.DoFinal(output, len);

        return output[..len];
    }

    private PaddedBufferedBlockCipher BuildCipher(bool forEncryption, byte[] iv)
    {
        var engine = new TwofishEngine();
        var cbcMode = new CbcBlockCipher(engine);
        var paddedBlock = new PaddedBufferedBlockCipher(cbcMode, new Pkcs7Padding());

        var keyParam = new KeyParameter(_key);
        var keyWithIv = new ParametersWithIV(keyParam, iv);
        paddedBlock.Init(forEncryption, keyWithIv);
        return paddedBlock;
    }

    public void Dispose() { }
}
