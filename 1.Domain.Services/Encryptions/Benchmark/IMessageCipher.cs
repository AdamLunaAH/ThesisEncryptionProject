namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Abstraction over a single encryption algorithm used in benchmark runs.
/// Each implementation is stateless and thread-safe; key material is held
/// inside the instance and set at construction time.
///
/// Ciphertext framing rules (enforced by each implementation):
///   Symmetric     — [IV/nonce | ciphertext] all in <see cref="CipherResult.Ciphertext"/>
///   Non-hybrid RSA — RSA-OAEP ciphertext only; throws if plaintext exceeds
///                    <see cref="MaxMessageBytes"/>
///   Hybrid         — session payload in <see cref="CipherResult.Ciphertext"/>,
///                    wrapped key material in <see cref="CipherResult.EncapsulatedKey"/>
/// </summary>
public interface IMessageCipher : IDisposable
{
    /// <summary>Canonical algorithm identifier, e.g. "AES-256-GCM".</summary>
    string AlgorithmId { get; }

    /// <summary>
    /// High-level family: "None" | "Symmetric" | "Asymmetric" | "Hybrid".
    /// Use <see cref="AlgorithmFamily"/> constants.
    /// </summary>
    string AlgorithmFamily { get; }

    /// <summary>
    /// Standards generation: "Deprecated" | "Legacy" | "Modern" | "PostQuantum".
    /// Use <see cref="AlgorithmGeneration"/> constants.
    /// </summary>
    string Generation { get; }

    /// <summary>Nominal key size in bits (effective security level may differ, e.g. 3DES).</summary>
    int KeySizeBits { get; }

    /// <summary>
    /// Maximum plaintext size in bytes this cipher accepts in a single call.
    /// <see cref="int.MaxValue"/> for ciphers with no practical limit.
    /// Non-hybrid RSA instances set this to the OAEP payload capacity.
    /// </summary>
    int MaxMessageBytes { get; }

    /// <summary>
    /// <c>true</c> for ciphers that combine asymmetric key exchange with
    /// a symmetric data cipher (RSA-Hybrid, ECDH-Hybrid, ML-KEM-Hybrid).
    /// </summary>
    bool IsHybrid { get; }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns the ciphertext together
    /// with any encapsulated key material needed for decryption.
    /// </summary>
    /// <param name="plaintext">Raw bytes to encrypt.</param>
    /// <returns>
    /// A <see cref="CipherResult"/> whose <c>Ciphertext</c> is always populated.
    /// <c>EncapsulatedKey</c> is non-null only for hybrid ciphers.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="plaintext"/> exceeds <see cref="MaxMessageBytes"/>.
    /// </exception>
    CipherResult Encrypt(byte[] plaintext);

    /// <summary>
    /// Decrypts <paramref name="ciphertext"/> and returns the original plaintext.
    /// </summary>
    /// <param name="ciphertext">
    /// The <see cref="CipherResult.Ciphertext"/> value returned by <see cref="Encrypt"/>.
    /// </param>
    /// <param name="encapsulatedKey">
    /// The <see cref="CipherResult.EncapsulatedKey"/> value from the same
    /// <see cref="CipherResult"/>. Pass <c>null</c> for symmetric ciphers.
    /// </param>
    byte[] Decrypt(byte[] ciphertext, byte[]? encapsulatedKey = null);
}
