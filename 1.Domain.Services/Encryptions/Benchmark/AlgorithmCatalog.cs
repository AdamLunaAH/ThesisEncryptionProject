namespace Domain.Services.Encryptions.Benchmark;

// ──────────────────────────────────────────────────────────────────────────────
// String constants for algorithm families
// ──────────────────────────────────────────────────────────────────────────────

public static class AlgorithmFamily
{
    public const string None = "None";
    public const string Symmetric = "Symmetric";
    public const string Asymmetric = "Asymmetric";
    public const string Hybrid = "Hybrid";
}

// ──────────────────────────────────────────────────────────────────────────────
// String constants for algorithm generations
// ──────────────────────────────────────────────────────────────────────────────

public static class AlgorithmGeneration
{
    /// <summary>
    /// Formally deprecated by a standards body (e.g. NIST SP 800-131A for 3DES).
    /// Still deployed in legacy systems; included to show the cost of compatibility debt.
    /// </summary>
    public const string Deprecated = "Deprecated";

    /// <summary>Widely deployed, not yet deprecated, but superseded by Modern variants.</summary>
    public const string Legacy = "Legacy";

    /// <summary>Current best-practice (e.g. AES-GCM, ChaCha20-Poly1305, ECDH-Hybrid).</summary>
    public const string Modern = "Modern";

    /// <summary>Post-quantum safe (NIST PQC standardisation, ML-KEM / FIPS 203).</summary>
    public const string PostQuantum = "PostQuantum";
}

// ──────────────────────────────────────────────────────────────────────────────
// Canonical algorithm ID strings
// ──────────────────────────────────────────────────────────────────────────────

public static class AlgorithmIds
{
    // ── Baseline ──────────────────────────────────────────────────────────────
    public const string None = "None";

    // ── Symmetric – Deprecated ────────────────────────────────────────────────
    /// <summary>3DES two-key (112-bit effective security). 64-bit block — SWEET32 risk.</summary>
    public const string TripleDes128Cbc = "3DES-128-CBC";

    /// <summary>3DES three-key (168-bit effective security). 64-bit block — SWEET32 risk.</summary>
    public const string TripleDes192Cbc = "3DES-192-CBC";

    // ── Symmetric – Legacy (BCL) ───────────────────────────────────────────────
    public const string Aes128Cbc = "AES-128-CBC";
    public const string Aes192Cbc = "AES-192-CBC";
    public const string Aes256Cbc = "AES-256-CBC";

    // ── Symmetric – Modern (BCL) ───────────────────────────────────────────────
    public const string Aes128Gcm = "AES-128-GCM";
    public const string Aes256Gcm = "AES-256-GCM";

    /// <summary>256-bit key, stream cipher. TLS 1.3 alternative to AES on platforms without AES-NI.</summary>
    public const string ChaCha20Poly1305 = "ChaCha20-Poly1305";

    // ── Symmetric – Legacy (BouncyCastle) ─────────────────────────────────────
    /// <summary>Twofish AES finalist. Requires BouncyCastle.Cryptography.</summary>
    public const string Twofish128Cbc = "Twofish-128-CBC";
    public const string Twofish192Cbc = "Twofish-192-CBC";
    public const string Twofish256Cbc = "Twofish-256-CBC";

    // ── Asymmetric – Legacy (BCL) ──────────────────────────────────────────────
    /// <summary>Non-hybrid RSA-OAEP-SHA256. Payload limited to ~190 bytes.</summary>
    public const string Rsa2048 = "RSA-2048";

    /// <summary>Non-hybrid RSA-OAEP-SHA256. Payload limited to ~446 bytes.</summary>
    public const string Rsa4096 = "RSA-4096";

    // ── Hybrid – Legacy (BCL) ──────────────────────────────────────────────────
    /// <summary>RSA-OAEP wraps a fresh AES-256-GCM session key per message.</summary>
    public const string Rsa2048Hybrid = "RSA-2048-Hybrid-AES-256-GCM";
    public const string Rsa4096Hybrid = "RSA-4096-Hybrid-AES-256-GCM";

    // ── Hybrid – Modern (BCL) ──────────────────────────────────────────────────
    /// <summary>Ephemeral ECDH key agreement (P-256) + AES-256-GCM data cipher.</summary>
    public const string EcdhP256Hybrid = "ECDH-P256-Hybrid-AES-256-GCM";

    /// <summary>Ephemeral ECDH key agreement (P-384) + AES-256-GCM data cipher.</summary>
    public const string EcdhP384Hybrid = "ECDH-P384-Hybrid-AES-256-GCM";

    // ── Hybrid – Post-Quantum (BCL .NET 10) ────────────────────────────────────
    /// <summary>ML-KEM-768 (FIPS 203) + AES-256-GCM. NIST security level 3.</summary>
    public const string MlKem768Hybrid = "ML-KEM-768-Hybrid-AES-256-GCM";

    /// <summary>ML-KEM-1024 (FIPS 203) + AES-256-GCM. NIST security level 5.</summary>
    public const string MlKem1024Hybrid = "ML-KEM-1024-Hybrid-AES-256-GCM";
}

// ──────────────────────────────────────────────────────────────────────────────
// Canonical authenticator ID strings
// ──────────────────────────────────────────────────────────────────────────────

public static class AuthIds
{
    public const string HmacSha256 = "HMAC-SHA256";
    public const string HmacSha512 = "HMAC-SHA512";
    public const string HmacSha3_256 = "HMAC-SHA3-256";
    public const string HmacSha3_512 = "HMAC-SHA3-512";

    /// <summary>NIST SP 800-185. cSHAKE128-based MAC. Native in .NET 10 BCL.</summary>
    public const string Kmac128 = "KMAC-128";

    /// <summary>NIST SP 800-185. cSHAKE256-based MAC. Native in .NET 10 BCL.</summary>
    public const string Kmac256 = "KMAC-256";
}

// ──────────────────────────────────────────────────────────────────────────────
// Algorithm metadata record
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Describes a cipher algorithm independently of any key material.
/// Used by the benchmark controller to validate input and populate
/// <c>BenchmarkRun</c> database records without instantiating a cipher.
/// </summary>
public sealed record AlgorithmInfo(
    string AlgorithmId,
    string AlgorithmFamily,
    string Generation,
    int KeySizeBits,
    bool IsHybrid,
    int MaxMessageBytes,
    bool RequiresThirdPartyLibrary,
    string LibraryName,
    string Notes);

// ──────────────────────────────────────────────────────────────────────────────
// Authenticator metadata record
// ──────────────────────────────────────────────────────────────────────────────

public sealed record AuthInfo(
    string AuthId,
    string Generation,
    int TagSizeBytes,
    string Notes);

// ──────────────────────────────────────────────────────────────────────────────
// Central lookup catalogue
// ──────────────────────────────────────────────────────────────────────────────

public static class AlgorithmCatalog
{
    // Non-hybrid RSA OAEP-SHA256 payload limits:
    //   2048-bit key → modulus 256 B − 2 × hash(32 B) − 2 = 190 B
    //   4096-bit key → modulus 512 B − 2 × hash(32 B) − 2 = 446 B
    private const int Rsa2048MaxBytes = 190;
    private const int Rsa4096MaxBytes = 446;

    public static readonly IReadOnlyDictionary<string, AlgorithmInfo> Ciphers =
        new Dictionary<string, AlgorithmInfo>
        {
            // ── Baseline ──────────────────────────────────────────────────────
            [AlgorithmIds.None] = new(
                AlgorithmIds.None,
                AlgorithmFamily.None,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 0,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "Pass-through, no encryption. Baseline for overhead comparison."),

            // ── 3DES – Deprecated ─────────────────────────────────────────────
            [AlgorithmIds.TripleDes128Cbc] = new(
                AlgorithmIds.TripleDes128Cbc,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Deprecated,
                KeySizeBits: 128,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "Two-key 3DES (112-bit effective). 64-bit block vulnerable to SWEET32. NIST deprecated 2017."),

            [AlgorithmIds.TripleDes192Cbc] = new(
                AlgorithmIds.TripleDes192Cbc,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Deprecated,
                KeySizeBits: 192,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "Three-key 3DES (168-bit effective). 64-bit block vulnerable to SWEET32. NIST deprecated 2017."),

            // ── AES-CBC – Legacy ───────────────────────────────────────────────
            [AlgorithmIds.Aes128Cbc] = new(
                AlgorithmIds.Aes128Cbc,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 128,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "AES-128 CBC. Hardware-accelerated on x86/ARM. No authentication (unauthenticated)."),

            [AlgorithmIds.Aes192Cbc] = new(
                AlgorithmIds.Aes192Cbc,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 192,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "AES-192 CBC. Rarely used; not supported in some TLS suites."),

            [AlgorithmIds.Aes256Cbc] = new(
                AlgorithmIds.Aes256Cbc,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 256,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "AES-256 CBC. Current production cipher in this codebase."),

            // ── AES-GCM – Modern ───────────────────────────────────────────────
            [AlgorithmIds.Aes128Gcm] = new(
                AlgorithmIds.Aes128Gcm,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Modern,
                KeySizeBits: 128,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "AES-128-GCM. AEAD: authentication built in. Mandatory TLS 1.3 suite."),

            [AlgorithmIds.Aes256Gcm] = new(
                AlgorithmIds.Aes256Gcm,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Modern,
                KeySizeBits: 256,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "AES-256-GCM. AEAD. Recommended modern symmetric cipher."),

            // ── ChaCha20-Poly1305 – Modern ─────────────────────────────────────
            [AlgorithmIds.ChaCha20Poly1305] = new(
                AlgorithmIds.ChaCha20Poly1305,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Modern,
                KeySizeBits: 256,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "ChaCha20-Poly1305. AEAD. TLS 1.3 suite. Preferred on platforms without AES-NI."),

            // ── Twofish-CBC – Legacy (BouncyCastle) ───────────────────────────
            [AlgorithmIds.Twofish128Cbc] = new(
                AlgorithmIds.Twofish128Cbc,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 128,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: true,
                LibraryName: "BouncyCastle.Cryptography",
                Notes: "Twofish-128 CBC. AES finalist. No native BCL support; software-only via BouncyCastle."),

            [AlgorithmIds.Twofish192Cbc] = new(
                AlgorithmIds.Twofish192Cbc,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 192,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: true,
                LibraryName: "BouncyCastle.Cryptography",
                Notes: "Twofish-192 CBC. AES finalist. Software-only via BouncyCastle."),

            [AlgorithmIds.Twofish256Cbc] = new(
                AlgorithmIds.Twofish256Cbc,
                AlgorithmFamily.Symmetric,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 256,
                IsHybrid: false,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: true,
                LibraryName: "BouncyCastle.Cryptography",
                Notes: "Twofish-256 CBC. AES finalist. Software-only via BouncyCastle."),

            // ── RSA non-hybrid – Legacy ────────────────────────────────────────
            [AlgorithmIds.Rsa2048] = new(
                AlgorithmIds.Rsa2048,
                AlgorithmFamily.Asymmetric,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 2048,
                IsHybrid: false,
                MaxMessageBytes: Rsa2048MaxBytes,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: $"RSA-OAEP-SHA256 direct. Payload limit {Rsa2048MaxBytes} B. Slow key-gen per session."),

            [AlgorithmIds.Rsa4096] = new(
                AlgorithmIds.Rsa4096,
                AlgorithmFamily.Asymmetric,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 4096,
                IsHybrid: false,
                MaxMessageBytes: Rsa4096MaxBytes,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: $"RSA-OAEP-SHA256 direct. Payload limit {Rsa4096MaxBytes} B. Very slow key-gen."),

            // ── RSA hybrid – Legacy ────────────────────────────────────────────
            [AlgorithmIds.Rsa2048Hybrid] = new(
                AlgorithmIds.Rsa2048Hybrid,
                AlgorithmFamily.Hybrid,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 2048,
                IsHybrid: true,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "RSA-2048-OAEP wraps a fresh AES-256-GCM session key per message. No size limit."),

            [AlgorithmIds.Rsa4096Hybrid] = new(
                AlgorithmIds.Rsa4096Hybrid,
                AlgorithmFamily.Hybrid,
                AlgorithmGeneration.Legacy,
                KeySizeBits: 4096,
                IsHybrid: true,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "RSA-4096-OAEP wraps a fresh AES-256-GCM session key per message. No size limit."),

            // ── ECDH hybrid – Modern ───────────────────────────────────────────
            [AlgorithmIds.EcdhP256Hybrid] = new(
                AlgorithmIds.EcdhP256Hybrid,
                AlgorithmFamily.Hybrid,
                AlgorithmGeneration.Modern,
                KeySizeBits: 256,
                IsHybrid: true,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "Ephemeral ECDH P-256 + HKDF + AES-256-GCM. Compact encapsulated key (65 B uncompressed)."),

            [AlgorithmIds.EcdhP384Hybrid] = new(
                AlgorithmIds.EcdhP384Hybrid,
                AlgorithmFamily.Hybrid,
                AlgorithmGeneration.Modern,
                KeySizeBits: 384,
                IsHybrid: true,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL",
                Notes: "Ephemeral ECDH P-384 + HKDF + AES-256-GCM. Higher security curve."),

            // ── ML-KEM hybrid – Post-Quantum ──────────────────────────────────
            [AlgorithmIds.MlKem768Hybrid] = new(
                AlgorithmIds.MlKem768Hybrid,
                AlgorithmFamily.Hybrid,
                AlgorithmGeneration.PostQuantum,
                KeySizeBits: 768,
                IsHybrid: true,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL (.NET 10)",
                Notes: "ML-KEM-768 (FIPS 203) encapsulation + AES-256-GCM. NIST security level 3. Quantum-safe."),

            [AlgorithmIds.MlKem1024Hybrid] = new(
                AlgorithmIds.MlKem1024Hybrid,
                AlgorithmFamily.Hybrid,
                AlgorithmGeneration.PostQuantum,
                KeySizeBits: 1024,
                IsHybrid: true,
                MaxMessageBytes: int.MaxValue,
                RequiresThirdPartyLibrary: false,
                LibraryName: "BCL (.NET 10)",
                Notes: "ML-KEM-1024 (FIPS 203) encapsulation + AES-256-GCM. NIST security level 5. Quantum-safe."),
        };

    public static readonly IReadOnlyDictionary<string, AuthInfo> Authenticators =
        new Dictionary<string, AuthInfo>
        {
            [AuthIds.HmacSha256] = new(
                AuthIds.HmacSha256,
                AlgorithmGeneration.Legacy,
                TagSizeBytes: 32,
                Notes: "HMAC-SHA256 (RFC 2104). 256-bit tag. SHA-2 family."),

            [AuthIds.HmacSha512] = new(
                AuthIds.HmacSha512,
                AlgorithmGeneration.Legacy,
                TagSizeBytes: 64,
                Notes: "HMAC-SHA512 (RFC 2104). 512-bit tag. SHA-2 family."),

            [AuthIds.HmacSha3_256] = new(
                AuthIds.HmacSha3_256,
                AlgorithmGeneration.Modern,
                TagSizeBytes: 32,
                Notes: "HMAC-SHA3-256. SHA-3 hash inside the HMAC construction."),

            [AuthIds.HmacSha3_512] = new(
                AuthIds.HmacSha3_512,
                AlgorithmGeneration.Modern,
                TagSizeBytes: 64,
                Notes: "HMAC-SHA3-512. SHA-3 hash inside the HMAC construction."),

            [AuthIds.Kmac128] = new(
                AuthIds.Kmac128,
                AlgorithmGeneration.Modern,
                TagSizeBytes: 32,
                Notes: "KMAC128 (NIST SP 800-185). cSHAKE128-native MAC. Variable output; 256-bit tag used here."),

            [AuthIds.Kmac256] = new(
                AuthIds.Kmac256,
                AlgorithmGeneration.Modern,
                TagSizeBytes: 64,
                Notes: "KMAC256 (NIST SP 800-185). cSHAKE256-native MAC. Variable output; 512-bit tag used here."),
        };

    /// <summary>
    /// Returns metadata for a cipher algorithm by ID.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown for unknown IDs.</exception>
    public static AlgorithmInfo GetCipherInfo(string algorithmId)
    {
        if (Ciphers.TryGetValue(algorithmId, out var info))
            return info;
        throw new ArgumentException(
            $"Unknown cipher algorithm '{algorithmId}'. Valid values: {string.Join(", ", Ciphers.Keys)}",
            nameof(algorithmId));
    }

    /// <summary>
    /// Returns metadata for an authenticator by ID.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown for unknown IDs.</exception>
    public static AuthInfo GetAuthInfo(string authId)
    {
        if (Authenticators.TryGetValue(authId, out var info))
            return info;
        throw new ArgumentException(
            $"Unknown authenticator '{authId}'. Valid values: {string.Join(", ", Authenticators.Keys)}",
            nameof(authId));
    }

    /// <summary>All cipher algorithm IDs in definition order.</summary>
    public static IEnumerable<string> AllCipherIds => Ciphers.Keys;

    /// <summary>All authenticator IDs in definition order.</summary>
    public static IEnumerable<string> AllAuthIds => Authenticators.Keys;
}
