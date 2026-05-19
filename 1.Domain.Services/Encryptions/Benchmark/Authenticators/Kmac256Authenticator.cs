using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Authenticators;

/// <summary>
/// KMAC256 message authenticator (NIST SP 800-185).
///
/// Built on cSHAKE256 — higher security margin than KMAC128.
/// NIST security level 256 bits (vs. 128 bits for KMAC128).
///
/// 512-bit (64-byte) tag (variable-length output; 64 B selected to match HMAC-SHA512).
/// "Modern" generation.
///
/// Generates a random 64-byte key (512-bit, ≥ NIST recommended 256-bit minimum).
/// Requires .NET 8 or later (Kmac256 added in .NET 8).
/// </summary>
public sealed class Kmac256Authenticator : IMessageAuthenticator
{
    private static readonly byte[] CustomizationString = [];

    private readonly byte[] _key;

    public Kmac256Authenticator()
    {
        _key = RandomNumberGenerator.GetBytes(64);
    }

    public string AuthId => AuthIds.Kmac256;
    public string Generation => AlgorithmGeneration.Modern;
    public int TagSizeBytes => 64;

    public byte[] Sign(byte[] data)
        => Kmac256.HashData(_key, data, TagSizeBytes, CustomizationString);

    public bool Verify(byte[] data, byte[] tag)
    {
        byte[] expected = Kmac256.HashData(_key, data, TagSizeBytes, CustomizationString);
        return CryptographicOperations.FixedTimeEquals(expected, tag);
    }
}
