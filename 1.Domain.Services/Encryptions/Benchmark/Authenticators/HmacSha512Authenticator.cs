using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Authenticators;

/// <summary>
/// HMAC-SHA512 message authenticator (RFC 2104 + FIPS 180-4).
///
/// SHA-2 family. 512-bit (64-byte) tag. On 64-bit platforms SHA-512 is often
/// faster than SHA-256 due to wider word operations.
/// "Legacy" generation — superseded by SHA-3-based MACs for new designs.
/// Generates a random 64-byte key on construction (key length == hash output
/// length is the HMAC recommended practice for SHA-512).
/// </summary>
public sealed class HmacSha512Authenticator : IMessageAuthenticator
{
    private readonly byte[] _key;

    public HmacSha512Authenticator()
    {
        _key = RandomNumberGenerator.GetBytes(64);
    }

    public string AuthId => AuthIds.HmacSha512;
    public string Generation => AlgorithmGeneration.Legacy;
    public int TagSizeBytes => 64;

    public byte[] Sign(byte[] data)
        => HMACSHA512.HashData(_key, data);

    public bool Verify(byte[] data, byte[] tag)
    {
        byte[] expected = HMACSHA512.HashData(_key, data);
        return CryptographicOperations.FixedTimeEquals(expected, tag);
    }
}
