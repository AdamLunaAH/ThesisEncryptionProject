using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Authenticators;

/// <summary>
/// HMAC-SHA3-256 message authenticator.
///
/// SHA-3 (Keccak sponge) inside the HMAC construction (RFC 2104).
/// 256-bit (32-byte) tag. "Modern" generation.
/// SHA-3 has a fundamentally different design from SHA-2 — cross-generation
/// comparison reveals the performance cost of the sponge vs. Merkle-Damgård.
///
/// Note: HMAC-SHA3 is specified but KMAC128/256 is the NIST-preferred dedicated
/// MAC for SHA-3 primitives (avoids length-extension issues inherently).
/// Both are benchmarked so the cost difference is visible.
///
/// Generates a random 32-byte key on construction.
/// Requires .NET 8 or later (HMACSHA3_256 added in .NET 8).
/// </summary>
public sealed class HmacSha3_256Authenticator : IMessageAuthenticator
{
    private readonly byte[] _key;

    public HmacSha3_256Authenticator()
    {
        _key = RandomNumberGenerator.GetBytes(32);
    }

    public string AuthId => AuthIds.HmacSha3_256;
    public string Generation => AlgorithmGeneration.Modern;
    public int TagSizeBytes => 32;

    public byte[] Sign(byte[] data)
        => HMACSHA3_256.HashData(_key, data);

    public bool Verify(byte[] data, byte[] tag)
    {
        byte[] expected = HMACSHA3_256.HashData(_key, data);
        return CryptographicOperations.FixedTimeEquals(expected, tag);
    }
}
