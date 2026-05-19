using System.Buffers.Binary;

namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Encodes and decodes the length-prefixed binary wire format used when transporting
/// encrypted payloads through SignalR for the integrated benchmark.
///
/// Format (all length prefixes are little-endian int32):
/// <code>
/// [4B ciphertextLen][ciphertext bytes]
/// [4B encKeyLen]    [encKey bytes     (0 bytes when no encapsulated key)]
/// [4B tagLen]       [tag bytes        (0 bytes when no MAC)]
/// </code>
///
/// The encoded bytes are then base64-encoded by the caller before being sent as a
/// SignalR string payload to <c>BenchmarkHub.BenchmarkEcho</c>.
/// </summary>
public static class BenchmarkWireFormat
{
    /// <summary>
    /// Encodes <paramref name="ciphertext"/>, optional <paramref name="encapsulatedKey"/>,
    /// and optional <paramref name="tag"/> into a single length-prefixed byte array.
    /// </summary>
    public static byte[] Encode(byte[] ciphertext, byte[]? encapsulatedKey, byte[]? tag)
    {
        var encKey = encapsulatedKey ?? Array.Empty<byte>();
        var macTag = tag ?? Array.Empty<byte>();

        int total = 4 + ciphertext.Length + 4 + encKey.Length + 4 + macTag.Length;
        var buf = new byte[total];
        int pos = 0;

        WriteSegment(buf, ref pos, ciphertext);
        WriteSegment(buf, ref pos, encKey);
        WriteSegment(buf, ref pos, macTag);

        return buf;
    }

    /// <summary>
    /// Decodes a byte array previously produced by <see cref="Encode"/>.
    /// Returns <c>null</c> for the encapsulated key and tag when their encoded
    /// lengths are zero (i.e. symmetric ciphers and unauthenticated runs).
    /// </summary>
    public static (byte[] Ciphertext, byte[]? EncapsulatedKey, byte[]? Tag) Decode(
        byte[] wirePayload)
    {
        int pos = 0;
        var ciphertext = ReadSegment(wirePayload, ref pos);
        var encKeySegment = ReadSegment(wirePayload, ref pos);
        var tagSegment = ReadSegment(wirePayload, ref pos);

        return (
            ciphertext,
            encKeySegment.Length > 0 ? encKeySegment : null,
            tagSegment.Length > 0 ? tagSegment : null
        );
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void WriteSegment(byte[] buf, ref int pos, byte[] data)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos), data.Length);
        pos += 4;
        data.CopyTo(buf, pos);
        pos += data.Length;
    }

    private static byte[] ReadSegment(byte[] buf, ref int pos)
    {
        int len = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(pos));
        pos += 4;
        var segment = new byte[len];
        buf.AsSpan(pos, len).CopyTo(segment);
        pos += len;
        return segment;
    }
}
