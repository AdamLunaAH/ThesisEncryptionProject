namespace Domain.Services.Encryptions.Benchmark.Ciphers;

/// <summary>
/// Pass-through cipher — no encryption is applied.
/// Used as a baseline to isolate the SignalR/Orleans overhead from cryptographic cost.
/// Ciphertext == plaintext (same bytes, no framing).
/// </summary>
public sealed class NullCipher : IMessageCipher
{
    public string AlgorithmId => AlgorithmIds.None;
    public string AlgorithmFamily => Benchmark.AlgorithmFamily.None;
    public string Generation => AlgorithmGeneration.Legacy;
    public int KeySizeBits => 0;
    public int MaxMessageBytes => int.MaxValue;
    public bool IsHybrid => false;

    public CipherResult Encrypt(byte[] plaintext)
    {
        byte[] copy = new byte[plaintext.Length];
        plaintext.CopyTo(copy, 0);
        return new CipherResult(copy);
    }

    public byte[] Decrypt(byte[] ciphertext, byte[]? encapsulatedKey = null)
    {
        byte[] copy = new byte[ciphertext.Length];
        ciphertext.CopyTo(copy, 0);
        return copy;
    }

    public void Dispose() { }
}
