namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Holds the output of a single encrypt operation.
/// For symmetric ciphers the IV/nonce is embedded inside <see cref="Ciphertext"/>
/// so the caller never needs to manage it separately.
/// For hybrid ciphers (RSA-Hybrid, ECDH, ML-KEM) <see cref="EncapsulatedKey"/>
/// carries the wrapped/encapsulated session-key material that the receiver needs
/// before it can decrypt <see cref="Ciphertext"/>.
/// </summary>
public readonly struct CipherResult
{
    /// <summary>
    /// The encrypted payload. IV/nonce is always prepended by the cipher implementation.
    /// Format is implementation-specific but always self-contained.
    /// </summary>
    public byte[] Ciphertext { get; }

    /// <summary>
    /// Wrapped session-key material for hybrid ciphers, <c>null</c> for symmetric ciphers.
    /// <list type="bullet">
    ///   <item>RSA-Hybrid  — RSA-OAEP(AES session key)</item>
    ///   <item>ECDH-Hybrid — ephemeral public key bytes</item>
    ///   <item>ML-KEM-Hybrid — KEM ciphertext (encapsulation output)</item>
    /// </list>
    /// </summary>
    public byte[]? EncapsulatedKey { get; }

    public CipherResult(byte[] ciphertext, byte[]? encapsulatedKey = null)
    {
        Ciphertext = ciphertext;
        EncapsulatedKey = encapsulatedKey;
    }
}
