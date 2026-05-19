using System.Security.Cryptography;
using System.Text;

namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// ML-KEM (FIPS 203) + AES-256-GCM hybrid cipher using the .NET 10 BCL.
///
/// ML-KEM (Module Lattice-based Key Encapsulation Mechanism) is NIST's
/// post-quantum standard (formerly Kyber). It is quantum-safe against
/// Grover's and Shor's algorithms.
///
/// Per-message key exchange (provides forward secrecy per message):
///   Encrypt:
///     1. Encapsulate: run ML-KEM encapsulation with the held public key.
///        Produces: (KEM ciphertext, 32-byte shared secret).
///     2. Derive a 256-bit AES key via HKDF-SHA256 over the shared secret.
///     3. Encrypt the payload with AES-256-GCM.
///     4. Return: CipherResult(ciphertext=GCM_output, encapsulatedKey=KEM_ciphertext).
///
///   Decrypt:
///     1. Decapsulate: run ML-KEM decapsulation with the held private key
///        over the KEM ciphertext to recover the shared secret.
///     2. Derive the same AES key via HKDF-SHA256.
///     3. Decrypt with AES-256-GCM.
///
/// Parameter sets:
///   ML-KEM-768  (NIST security level 3) — KEM ciphertext: 1088 bytes.
///   ML-KEM-1024 (NIST security level 5) — KEM ciphertext: 1568 bytes.
/// Shared secret size: 32 bytes for both parameter sets.
///
/// Requires .NET 10 or later (MLKem is stable; no [RequiresPreviewFeatures] in .NET 10).
/// </summary>
public sealed class MlKemHybridCipher : IMessageCipher
{
    private static readonly byte[] HkdfInfo = Encoding.UTF8.GetBytes("benchmark-mlkem-v1");

    private readonly MLKem _key;
    private readonly MLKemAlgorithm _algorithm;

    public MlKemHybridCipher(MLKemAlgorithm algorithm)
    {
        _algorithm = algorithm;
        _key = MLKem.GenerateKey(algorithm);
    }

    public string AlgorithmId => _algorithm == MLKemAlgorithm.MLKem768
        ? AlgorithmIds.MlKem768Hybrid
        : AlgorithmIds.MlKem1024Hybrid;

    public string AlgorithmFamily => Benchmark.AlgorithmFamily.Hybrid;
    public string Generation => AlgorithmGeneration.PostQuantum;
    public int KeySizeBits => _algorithm == MLKemAlgorithm.MLKem768 ? 768 : 1024;
    public int MaxMessageBytes => int.MaxValue;
    public bool IsHybrid => true;

    public CipherResult Encrypt(byte[] plaintext)
    {
        // Encapsulate: produce KEM ciphertext + shared secret
        byte[] kemCiphertext = new byte[_algorithm.CiphertextSizeInBytes];
        byte[] sharedSecret = new byte[_algorithm.SharedSecretSizeInBytes];
        _key.Encapsulate(kemCiphertext, sharedSecret);

        byte[] aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, salt: null, info: HkdfInfo);
        byte[] ciphertext = HybridAesGcmHelper.Encrypt(plaintext, aesKey);

        return new CipherResult(ciphertext, kemCiphertext);
    }

    public byte[] Decrypt(byte[] ciphertext, byte[]? encapsulatedKey = null)
    {
        if (encapsulatedKey is null)
            throw new ArgumentNullException(nameof(encapsulatedKey),
                "ML-KEM-Hybrid decryption requires the KEM ciphertext in encapsulatedKey.");

        byte[] sharedSecret = new byte[_algorithm.SharedSecretSizeInBytes];
        _key.Decapsulate(encapsulatedKey, sharedSecret);

        byte[] aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, salt: null, info: HkdfInfo);
        return HybridAesGcmHelper.Decrypt(ciphertext, aesKey);
    }

    public void Dispose() => _key.Dispose();
}
