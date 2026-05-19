using System.Security.Cryptography;

namespace Domain.Services.Encryptions.Benchmark.Authenticators;

/// <summary>
/// HMAC-SHA256 message authenticator (RFC 2104 + FIPS 180-4).
///
/// SHA-2 family. 256-bit (32-byte) tag. Hardware-accelerated on x86/ARM via SHA-NI.
/// "Legacy" generation — widely deployed but superseded by SHA-3-based constructions.
/// Generates a random 32-byte key on construction; key is reused for the lifetime
/// of the benchmark session so sign and verify are consistent.
/// </summary>
public sealed class HmacSha256Authenticator : IMessageAuthenticator
{
    private readonly byte[] _key;

    public HmacSha256Authenticator()
    {
        _key = RandomNumberGenerator.GetBytes(32);
    }

    public string AuthId => AuthIds.HmacSha256;
    public string Generation => AlgorithmGeneration.Legacy;
    public int TagSizeBytes => 32;

    public byte[] Sign(byte[] data)
        => HMACSHA256.HashData(_key, data);

    public bool Verify(byte[] data, byte[] tag)
    {
        byte[] expected = HMACSHA256.HashData(_key, data);
        return CryptographicOperations.FixedTimeEquals(expected, tag);
    }
}
