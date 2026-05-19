using System.Security.Cryptography;
using Domain.Services.Encryptions.Benchmark.Ciphers;

namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Creates <see cref="IMessageCipher"/> instances from a canonical algorithm ID string.
///
/// Key material is generated inside each cipher's constructor so the construction
/// cost (including slow RSA key generation) can be measured as a one-time
/// "session setup" cost, separate from per-message encryption latency.
///
/// Each returned instance owns its key material and must be disposed when the
/// benchmark run finishes.
/// </summary>
public static class CipherFactory
{
    /// <summary>
    /// Creates a new keyed cipher for the given <paramref name="algorithmId"/>.
    /// </summary>
    /// <param name="algorithmId">
    /// One of the string constants defined in <see cref="AlgorithmIds"/>,
    /// e.g. <c>"AES-256-GCM"</c> or <c>"ML-KEM-768-Hybrid"</c>.
    /// </param>
    /// <returns>A freshly keyed <see cref="IMessageCipher"/>. Caller must dispose.</returns>
    /// <exception cref="ArgumentException">Unknown algorithm ID.</exception>
    public static IMessageCipher Create(string algorithmId) => algorithmId switch
    {
        // ── Baseline ──────────────────────────────────────────────────────────
        AlgorithmIds.None => new NullCipher(),

        // ── 3DES – Deprecated ─────────────────────────────────────────────────
        AlgorithmIds.TripleDes128Cbc => new TripleDesCbcCipher(128),
        AlgorithmIds.TripleDes192Cbc => new TripleDesCbcCipher(192),

        // ── AES-CBC – Legacy ───────────────────────────────────────────────────
        AlgorithmIds.Aes128Cbc => new AesCbcCipher(128),
        AlgorithmIds.Aes192Cbc => new AesCbcCipher(192),
        AlgorithmIds.Aes256Cbc => new AesCbcCipher(256),

        // ── AES-GCM – Modern ───────────────────────────────────────────────────
        AlgorithmIds.Aes128Gcm => new AesGcmCipher(128),
        AlgorithmIds.Aes256Gcm => new AesGcmCipher(256),

        // ── ChaCha20-Poly1305 – Modern ─────────────────────────────────────────
        AlgorithmIds.ChaCha20Poly1305 => new ChaCha20Poly1305Cipher(),

        // ── Twofish-CBC – Legacy (BouncyCastle) ───────────────────────────────
        AlgorithmIds.Twofish128Cbc => new TwofishCbcCipher(128),
        AlgorithmIds.Twofish192Cbc => new TwofishCbcCipher(192),
        AlgorithmIds.Twofish256Cbc => new TwofishCbcCipher(256),

        // ── RSA non-hybrid – Legacy ────────────────────────────────────────────
        AlgorithmIds.Rsa2048 => new RsaOaepCipher(2048, hybrid: false),
        AlgorithmIds.Rsa4096 => new RsaOaepCipher(4096, hybrid: false),

        // ── RSA hybrid – Legacy ────────────────────────────────────────────────
        AlgorithmIds.Rsa2048Hybrid => new RsaOaepCipher(2048, hybrid: true),
        AlgorithmIds.Rsa4096Hybrid => new RsaOaepCipher(4096, hybrid: true),

        // ── ECDH hybrid – Modern ───────────────────────────────────────────────
        AlgorithmIds.EcdhP256Hybrid => new EcdhHybridCipher(256),
        AlgorithmIds.EcdhP384Hybrid => new EcdhHybridCipher(384),

        // ── ML-KEM hybrid – Post-Quantum ──────────────────────────────────────
        AlgorithmIds.MlKem768Hybrid => new MlKemHybridCipher(MLKemAlgorithm.MLKem768),
        AlgorithmIds.MlKem1024Hybrid => new MlKemHybridCipher(MLKemAlgorithm.MLKem1024),

        _ => throw new ArgumentException(
            $"Unknown cipher algorithm '{algorithmId}'. " +
            $"Valid values: {string.Join(", ", AlgorithmCatalog.AllCipherIds)}",
            nameof(algorithmId))
    };

    /// <summary>
    /// Creates one fresh cipher instance for every algorithm in the catalogue.
    /// Useful for "compare all" benchmark runs.
    /// Callers are responsible for disposing all returned instances.
    /// </summary>
    public static IReadOnlyList<IMessageCipher> CreateAll()
        => AlgorithmCatalog.AllCipherIds
            .Select(Create)
            .ToList();
}
