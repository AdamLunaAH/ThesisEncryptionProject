using System.Diagnostics;

namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Orchestrates a benchmark run by driving a cipher (and optional authenticator)
/// through one or more messages and capturing <see cref="MetricsSnapshot"/> per message.
///
/// Design notes:
/// • All operations are synchronous and sequential — no parallelism — so that
///   GC allocation counts and timing are attributable to a single logical operation.
/// • MAC order is encrypt-then-MAC (sign the ciphertext, not the plaintext) which
///   is the secure composition order and matches TLS 1.2 / SSH behaviour.
/// • Warmup rounds trigger JIT compilation so the first measured result is not
///   an outlier. Three warmup rounds is sufficient for tiered JIT steady-state.
/// • GC allocation tracking uses <c>GC.GetAllocatedBytesForCurrentThread()</c>
///   which is cumulative and thread-local, giving accurate per-operation counts
///   without interference from other threads.
/// </summary>
public static class BenchmarkMetricsCollector
{
    /// <summary>
    /// Converts Stopwatch ticks to microseconds. Pre-computed once to avoid
    /// repeated division in the hot measurement path.
    /// </summary>
    private static readonly double TicksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Measures a single message round-trip (encrypt → sign → decrypt → verify)
    /// and returns the captured metrics.
    /// </summary>
    /// <param name="cipher">The cipher to benchmark. Must already be keyed.</param>
    /// <param name="authenticator">Optional MAC layer applied after encryption. Pass <c>null</c> to skip.</param>
    /// <param name="plaintext">The message to encrypt.</param>
    public static MetricsSnapshot Measure(
        IMessageCipher cipher,
        IMessageAuthenticator? authenticator,
        byte[] plaintext)
    {
        // ── Encrypt ───────────────────────────────────────────────────────────
        int gen0Before = GC.CollectionCount(0);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();

        long t0 = Stopwatch.GetTimestamp();
        CipherResult encrypted = cipher.Encrypt(plaintext);
        long t1 = Stopwatch.GetTimestamp();

        // ── Sign (encrypt-then-MAC: sign the ciphertext + encapsulated key) ───
        byte[]? tag = null;
        long t2 = t1;
        long t3 = t1;

        if (authenticator is not null)
        {
            t2 = Stopwatch.GetTimestamp();
            // Sign both ciphertext and encapsulated key so integrity covers the full wire payload.
            byte[] dataToSign = encrypted.EncapsulatedKey is null
                ? encrypted.Ciphertext
                : Combine(encrypted.Ciphertext, encrypted.EncapsulatedKey);
            tag = authenticator.Sign(dataToSign);
            t3 = Stopwatch.GetTimestamp();
        }

        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        int gen0After = GC.CollectionCount(0);

        // ── Decrypt ───────────────────────────────────────────────────────────
        bool decryptSuccess = false;
        long t4 = Stopwatch.GetTimestamp();
        try
        {
            byte[] recovered = cipher.Decrypt(encrypted.Ciphertext, encrypted.EncapsulatedKey);
            // Constant-time length check — avoids leaking timing info on the correctness assertion.
            decryptSuccess = recovered.Length == plaintext.Length
                && CryptographicEquals(recovered, plaintext);
        }
        catch
        {
            decryptSuccess = false;
        }
        long t5 = Stopwatch.GetTimestamp();

        // ── Verify (MAC check) ─────────────────────────────────────────────────
        bool verifySuccess = true;
        long t6 = t5;
        long t7 = t5;

        if (authenticator is not null && tag is not null)
        {
            t6 = Stopwatch.GetTimestamp();
            byte[] dataToVerify = encrypted.EncapsulatedKey is null
                ? encrypted.Ciphertext
                : Combine(encrypted.Ciphertext, encrypted.EncapsulatedKey);
            verifySuccess = authenticator.Verify(dataToVerify, tag);
            t7 = Stopwatch.GetTimestamp();
        }

        // ── Build snapshot ────────────────────────────────────────────────────
        return new MetricsSnapshot
        {
            EncryptMicroseconds = (t1 - t0) * TicksToMicroseconds,
            SignMicroseconds = (t3 - t2) * TicksToMicroseconds,
            DecryptMicroseconds = (t5 - t4) * TicksToMicroseconds,
            VerifyMicroseconds = (t7 - t6) * TicksToMicroseconds,
            PlaintextBytes = plaintext.Length,
            CiphertextBytes = encrypted.Ciphertext.Length,
            EncapsulatedKeyBytes = encrypted.EncapsulatedKey?.Length ?? 0,
            MacTagBytes = tag?.Length ?? 0,
            GcAllocatedBytes = Math.Max(0L, allocAfter - allocBefore),
            GcGen0Collections = Math.Max(0, gen0After - gen0Before),
            DecryptSuccess = decryptSuccess,
            VerifySuccess = verifySuccess,
        };
    }

    /// <summary>
    /// Runs a full benchmark over a list of messages, preceded by optional warmup
    /// rounds to reach JIT steady-state.
    /// </summary>
    /// <param name="cipher">The cipher to benchmark. Must already be keyed.</param>
    /// <param name="authenticator">Optional MAC layer. Pass <c>null</c> to benchmark cipher alone.</param>
    /// <param name="messages">Messages to benchmark in order. Must be non-empty.</param>
    /// <param name="warmupCount">
    /// Number of warmup rounds before measurement. Each warmup cycles through all
    /// messages. Defaults to 3, which is sufficient for .NET tiered JIT.
    /// Pass 0 to disable warmup (e.g. when measuring cold-start cost intentionally).
    /// </param>
    /// <returns>One <see cref="MetricsSnapshot"/> per message, in input order.</returns>
    public static IReadOnlyList<MetricsSnapshot> RunBenchmark(
        IMessageCipher cipher,
        IMessageAuthenticator? authenticator,
        IReadOnlyList<byte[]> messages,
        int warmupCount = 3)
    {
        if (messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));

        // Warmup: discard results but drive both cipher + authenticator through
        // realistic input so all code paths are JIT-compiled.
        for (int w = 0; w < warmupCount; w++)
        {
            foreach (byte[] msg in messages)
                Measure(cipher, authenticator, msg);
        }

        var results = new List<MetricsSnapshot>(messages.Count);
        foreach (byte[] msg in messages)
            results.Add(Measure(cipher, authenticator, msg));

        return results;
    }

    /// <summary>
    /// Like <see cref="RunBenchmark"/> but also returns the raw ciphertext produced
    /// for each measured message, bundled in a <see cref="PayloadCapture"/>.
    /// Use this overload when you need to persist the actual encrypted bytes.
    /// </summary>
    public static IReadOnlyList<PayloadCapture> RunBenchmarkWithPayloads(
        IMessageCipher cipher,
        IMessageAuthenticator? authenticator,
        IReadOnlyList<byte[]> messages,
        int warmupCount = 3)
    {
        if (messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));

        // Warmup: discard results so all code paths are JIT-compiled.
        for (int w = 0; w < warmupCount; w++)
        {
            foreach (byte[] msg in messages)
                Measure(cipher, authenticator, msg);
        }

        var results = new List<PayloadCapture>(messages.Count);
        foreach (byte[] msg in messages)
            results.Add(MeasureWithPayload(cipher, authenticator, msg));

        return results;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Like <see cref="Measure"/> but also captures the raw encrypted bytes
    /// alongside the metrics, returned as a <see cref="PayloadCapture"/>.
    /// </summary>
    private static PayloadCapture MeasureWithPayload(
        IMessageCipher cipher,
        IMessageAuthenticator? authenticator,
        byte[] plaintext)
    {
        // Reuse the same measurement logic as Measure() but keep the encrypted variable
        // and tag so they can be bundled into the PayloadCapture result.

        int gen0Before = GC.CollectionCount(0);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();

        long t0 = Stopwatch.GetTimestamp();
        CipherResult encrypted = cipher.Encrypt(plaintext);
        long t1 = Stopwatch.GetTimestamp();

        byte[]? tag = null;
        long t2 = t1, t3 = t1;

        if (authenticator is not null)
        {
            t2 = Stopwatch.GetTimestamp();
            byte[] dataToSign = encrypted.EncapsulatedKey is null
                ? encrypted.Ciphertext
                : Combine(encrypted.Ciphertext, encrypted.EncapsulatedKey);
            tag = authenticator.Sign(dataToSign);
            t3 = Stopwatch.GetTimestamp();
        }

        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        int gen0After = GC.CollectionCount(0);

        bool decryptSuccess = false;
        long t4 = Stopwatch.GetTimestamp();
        try
        {
            byte[] recovered = cipher.Decrypt(encrypted.Ciphertext, encrypted.EncapsulatedKey);
            decryptSuccess = recovered.Length == plaintext.Length
                && CryptographicEquals(recovered, plaintext);
        }
        catch { decryptSuccess = false; }
        long t5 = Stopwatch.GetTimestamp();

        bool verifySuccess = true;
        long t6 = t5, t7 = t5;

        if (authenticator is not null && tag is not null)
        {
            t6 = Stopwatch.GetTimestamp();
            byte[] dataToVerify = encrypted.EncapsulatedKey is null
                ? encrypted.Ciphertext
                : Combine(encrypted.Ciphertext, encrypted.EncapsulatedKey);
            verifySuccess = authenticator.Verify(dataToVerify, tag);
            t7 = Stopwatch.GetTimestamp();
        }

        var metrics = new MetricsSnapshot
        {
            EncryptMicroseconds = (t1 - t0) * TicksToMicroseconds,
            SignMicroseconds = (t3 - t2) * TicksToMicroseconds,
            DecryptMicroseconds = (t5 - t4) * TicksToMicroseconds,
            VerifyMicroseconds = (t7 - t6) * TicksToMicroseconds,
            PlaintextBytes = plaintext.Length,
            CiphertextBytes = encrypted.Ciphertext.Length,
            EncapsulatedKeyBytes = encrypted.EncapsulatedKey?.Length ?? 0,
            MacTagBytes = tag?.Length ?? 0,
            GcAllocatedBytes = Math.Max(0L, allocAfter - allocBefore),
            GcGen0Collections = Math.Max(0, gen0After - gen0Before),
            DecryptSuccess = decryptSuccess,
            VerifySuccess = verifySuccess,
        };

        return new PayloadCapture
        {
            Metrics = metrics,
            Ciphertext = encrypted.Ciphertext,
            EncapsulatedKey = encrypted.EncapsulatedKey,
            MacTag = tag
        };
    }

    /// <summary>
    /// Concatenates two byte arrays. Used to build the signed buffer when an
    /// encapsulated key is present, so the MAC covers the complete wire payload.
    /// </summary>
    private static byte[] Combine(byte[] a, byte[] b)
    {
        byte[] combined = new byte[a.Length + b.Length];
        a.CopyTo(combined, 0);
        b.CopyTo(combined, a.Length);
        return combined;
    }

    /// <summary>
    /// Constant-time byte-array equality check used to verify decryption correctness
    /// without leaking timing information proportional to the position of the first
    /// differing byte.
    /// </summary>
    private static bool CryptographicEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}
