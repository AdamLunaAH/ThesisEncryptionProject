using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Authenticators;

/// <summary>
/// HMAC-SHA3-512 message authenticator.
///
/// SHA-3 (Keccak sponge) inside the HMAC construction (RFC 2104).
/// 512-bit (64-byte) tag. "Modern" generation.
/// Largest tag size in the authenticator catalogue — useful for showing the
/// marginal cost of extra tag width within the SHA-3 family vs. SHA-2.
///
/// Generates a random 64-byte key on construction.
/// Requires .NET 8 or later (HMACSHA3_512 added in .NET 8).
/// </summary>
public sealed class HmacSha3_512Authenticator : IMessageAuthenticator
{
    private readonly byte[] _key;

    public HmacSha3_512Authenticator()
    {
        _key = RandomNumberGenerator.GetBytes(64);
    }

    public string AuthId => AuthIds.HmacSha3_512;
    public string Generation => AlgorithmGeneration.Modern;
    public int TagSizeBytes => 64;

    public byte[] Sign(byte[] data)
        => HMACSHA3_512.HashData(_key, data);

    public bool Verify(byte[] data, byte[] tag)
    {
        byte[] expected = HMACSHA3_512.HashData(_key, data);
        return CryptographicOperations.FixedTimeEquals(expected, tag);
    }
}
