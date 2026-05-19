using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Authenticators;

/// <summary>
/// KMAC128 message authenticator (NIST SP 800-185).
///
/// KMAC (Keccak Message Authentication Code) is the NIST-preferred MAC for
/// SHA-3 primitives. Built on cSHAKE128 — a customizable SHAKE128 variant.
/// Unlike HMAC, KMAC has no length-extension vulnerability by design, and
/// the key is integral to the algorithm rather than prepended/appended.
///
/// 256-bit (32-byte) tag (variable-length output; 32 B selected to match HMAC-SHA256).
/// "Modern" generation.
///
/// Generates a random 32-byte key (256-bit, ≥ NIST recommended 128-bit minimum).
/// Requires .NET 8 or later (Kmac128 added in .NET 8).
/// </summary>
public sealed class Kmac128Authenticator : IMessageAuthenticator
{
    private static readonly byte[] CustomizationString = [];

    private readonly byte[] _key;

    public Kmac128Authenticator()
    {
        _key = RandomNumberGenerator.GetBytes(32);
    }

    public string AuthId => AuthIds.Kmac128;
    public string Generation => AlgorithmGeneration.Modern;
    public int TagSizeBytes => 32;

    public byte[] Sign(byte[] data)
        => Kmac128.HashData(_key, data, TagSizeBytes, CustomizationString);

    public bool Verify(byte[] data, byte[] tag)
    {
        byte[] expected = Kmac128.HashData(_key, data, TagSizeBytes, CustomizationString);
        return CryptographicOperations.FixedTimeEquals(expected, tag);
    }
}
