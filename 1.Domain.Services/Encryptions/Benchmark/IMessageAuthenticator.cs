namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Optional MAC layer applied on top of a cipher in benchmark runs.
/// The authenticator signs the ciphertext bytes (encrypt-then-MAC), so the
/// tag covers the IV/nonce as well as the encrypted payload.
///
/// The benchmark records sign and verify latencies separately so the cost of
/// adding an authentication layer is visible independently of the cipher cost.
/// </summary>
public interface IMessageAuthenticator
{
    /// <summary>Canonical auth identifier, e.g. "HMAC-SHA256".</summary>
    string AuthId { get; }

    /// <summary>
    /// Standards generation: "Legacy" | "Modern" | "PostQuantum".
    /// Use <see cref="AlgorithmGeneration"/> constants.
    /// </summary>
    string Generation { get; }

    /// <summary>Fixed output tag size in bytes for this authenticator.</summary>
    int TagSizeBytes { get; }

    /// <summary>
    /// Computes a MAC tag over <paramref name="data"/> and returns the tag bytes.
    /// </summary>
    byte[] Sign(byte[] data);

    /// <summary>
    /// Verifies that <paramref name="tag"/> is a valid MAC for <paramref name="data"/>.
    /// Returns <c>false</c> instead of throwing on mismatch — the benchmark records
    /// the boolean outcome rather than propagating exceptions.
    /// </summary>
    bool Verify(byte[] data, byte[] tag);
}
