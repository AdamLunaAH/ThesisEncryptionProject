using System.Security.Cryptography;
using System.Text;

namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// Ephemeral ECDH + AES-256-GCM hybrid cipher using the .NET BCL.
///
/// Key exchange per message (provides per-message forward secrecy):
///   Encrypt:
///     1. Generate a fresh ephemeral ECDH key pair.
///     2. Compute raw shared secret = ECDH(ephemeral_private, server_static_public).
///     3. Derive a 256-bit AES key via HKDF-SHA256 over the shared secret.
///     4. Encrypt the payload with AES-256-GCM.
///     5. Return: CipherResult(ciphertext=GCM_output, encapsulatedKey=ephemeral_public_key_bytes).
///
///   Decrypt:
///     1. Import ephemeral public key from encapsulatedKey.
///     2. Compute shared secret = ECDH(server_static_private, ephemeral_public).
///     3. Derive the same AES key via HKDF-SHA256.
///     4. Decrypt with AES-256-GCM.
///
/// Curves: P-256 (NIST / FIPS 186-4) or P-384.
/// EncapsulatedKey size: 91 bytes for P-256, 120 bytes for P-384 (SubjectPublicKeyInfo DER).
/// </summary>
public sealed class EcdhHybridCipher : IMessageCipher
{
    private static readonly byte[] HkdfInfo = Encoding.UTF8.GetBytes("benchmark-ecdh-v1");

    private readonly ECDiffieHellman _serverKey;
    private readonly ECCurve _curve;
    private readonly int _keySizeBits;

    /// <param name="keySizeBits">256 (P-256) or 384 (P-384).</param>
    public EcdhHybridCipher(int keySizeBits)
    {
        if (keySizeBits != 256 && keySizeBits != 384)
            throw new ArgumentException("ECDH key size must be 256 (P-256) or 384 (P-384).", nameof(keySizeBits));

        _keySizeBits = keySizeBits;
        _curve = keySizeBits == 256 ? ECCurve.NamedCurves.nistP256 : ECCurve.NamedCurves.nistP384;
        _serverKey = ECDiffieHellman.Create(_curve);
    }

    public string AlgorithmId => _keySizeBits == 256 ? AlgorithmIds.EcdhP256Hybrid : AlgorithmIds.EcdhP384Hybrid;
    public string AlgorithmFamily => Benchmark.AlgorithmFamily.Hybrid;
    public string Generation => AlgorithmGeneration.Modern;
    public int KeySizeBits => _keySizeBits;
    public int MaxMessageBytes => int.MaxValue;
    public bool IsHybrid => true;

    public CipherResult Encrypt(byte[] plaintext)
    {
        // Fresh ephemeral key pair for each message — perfect forward secrecy
        using var ephemeral = ECDiffieHellman.Create(_curve);

        byte[] ephemeralPublicKeyBytes = ephemeral.PublicKey.ExportSubjectPublicKeyInfo();
        byte[] aesKey = DeriveAesKey(ephemeral, _serverKey.PublicKey);
        byte[] ciphertext = HybridAesGcmHelper.Encrypt(plaintext, aesKey);

        return new CipherResult(ciphertext, ephemeralPublicKeyBytes);
    }

    public byte[] Decrypt(byte[] ciphertext, byte[]? encapsulatedKey = null)
    {
        if (encapsulatedKey is null)
            throw new ArgumentNullException(nameof(encapsulatedKey),
                "ECDH-Hybrid decryption requires the ephemeral public key in encapsulatedKey.");

        using var ephemeralPub = ECDiffieHellman.Create();
        ephemeralPub.ImportSubjectPublicKeyInfo(encapsulatedKey, out _);

        byte[] aesKey = DeriveAesKey(_serverKey, ephemeralPub.PublicKey);
        return HybridAesGcmHelper.Decrypt(ciphertext, aesKey);
    }

    /// <summary>
    /// Computes the ECDH shared secret and derives a 256-bit AES key via HKDF-SHA256.
    /// Using HKDF instead of the raw shared point x-coordinate prevents related-key attacks.
    /// </summary>
    private static byte[] DeriveAesKey(ECDiffieHellman privateKey, ECDiffieHellmanPublicKey peerPublicKey)
    {
        byte[] rawSecret = privateKey.DeriveRawSecretAgreement(peerPublicKey);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, rawSecret, 32, salt: null, info: HkdfInfo);
    }

    public void Dispose() => _serverKey.Dispose();
}
