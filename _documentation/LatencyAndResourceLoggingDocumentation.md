# Latency and Resource Logging — Documentation

This document explains in detail how per-message latency is measured and how process-level resource metrics are captured, aggregated, and persisted for each benchmark run.

---

## Part 1 — Per-Message Latency Measurement

### Clock source and tick conversion

All latency measurement uses `System.Diagnostics.Stopwatch.GetTimestamp()`. This reads a high-resolution hardware counter (typically the TSC on x86 or CNTVCT on ARM) and returns a raw tick count. The tick rate is `Stopwatch.Frequency` ticks per second.

`BenchmarkMetricsCollector` pre-computes the tick-to-microsecond conversion factor once at class load time to avoid repeated division in the measurement hot path:

```csharp
private static readonly double TicksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;
```

Every timing field is then computed as:

```csharp
double microseconds = (tEnd - tStart) * TicksToMicroseconds;
```

This gives sub-microsecond resolution on modern hardware where `Stopwatch.Frequency` is typically 10 MHz (100 ns per tick) or higher.

### Measurement sequence per message

`BenchmarkMetricsCollector.Measure` runs the following sequence synchronously on a single thread. No async calls, no parallelism — this ensures all timing and GC counts are attributable to exactly one message operation:

```
1. gen0Before  = GC.CollectionCount(0)
2. allocBefore = GC.GetAllocatedBytesForCurrentThread()

3. t0 = Stopwatch.GetTimestamp()
4.      cipher.Encrypt(plaintext)                 → encrypted (ciphertext + optional encapsulatedKey)
5. t1 = Stopwatch.GetTimestamp()                    EncryptMicroseconds = (t1 − t0) × µs/tick

6. t2 = Stopwatch.GetTimestamp()
7.      authenticator.Sign(ciphertext [+ encKey])  → tag
8. t3 = Stopwatch.GetTimestamp()                    SignMicroseconds = (t3 − t2) × µs/tick
         (steps 6–8 skipped when no authenticator; t2=t3=t1, SignMicroseconds=0)

9. allocAfter  = GC.GetAllocatedBytesForCurrentThread()
10. gen0After  = GC.CollectionCount(0)
         GcAllocatedBytes  = allocAfter  − allocBefore   (encrypt + sign only)
         GcGen0Collections = gen0After   − gen0Before

11. t4 = Stopwatch.GetTimestamp()
12.     cipher.Decrypt(ciphertext, encapsulatedKey) → recovered plaintext
13. t5 = Stopwatch.GetTimestamp()                    DecryptMicroseconds = (t5 − t4) × µs/tick

14. t6 = Stopwatch.GetTimestamp()
15.     authenticator.Verify(ciphertext [+ encKey], tag)
16. t7 = Stopwatch.GetTimestamp()                    VerifyMicroseconds = (t7 − t6) × µs/tick
         (steps 14–16 skipped when no authenticator; t6=t7=t5, VerifyMicroseconds=0)
```

GC tracking is deliberately restricted to the encrypt + sign phase (the server "send" side). Decrypt and verify are timed for round-trip completeness but their heap allocations are not recorded; tracking both sides would double-count allocations for buffers that are created once and reused.

The MAC is computed over the ciphertext bytes in encrypt-then-MAC order. For hybrid ciphers the encapsulated key is concatenated with the ciphertext before signing, so the tag covers the full wire payload.

### TotalRoundTripMicroseconds

`MetricsSnapshot.TotalRoundTripMicroseconds` is a computed property — not stored as a separate field — defined as:

```csharp
public double TotalRoundTripMicroseconds =>
    EncryptMicroseconds + SignMicroseconds +
    SignalRTransitMicroseconds +
    DecryptMicroseconds + VerifyMicroseconds;
```

For in-process benchmark runs `SignalRTransitMicroseconds` is always zero, so the total reflects cipher + MAC phases only. For the SignalR-transport path it includes real network round-trip time.

### SignalR transit measurement

In the SignalR-transport path (`EncryptionBenchmarkSignalRDemo`), `BenchmarkSignalRClient.SendEchoAsync` measures the network round-trip:

```csharp
// Just before writing to the WebSocket:
_sendTimestamp = Stopwatch.GetTimestamp();
await _hubConnection.SendAsync("BenchmarkEcho", base64Payload, cancellationToken);

// In the BenchmarkEchoResponse handler (fires on the SignalR receive thread):
var ts = Stopwatch.GetTimestamp();   // captured as first line of callback
_pendingEcho?.TrySetResult((ts, payload));

// Back in SendEchoAsync after awaiting the TCS:
var elapsedTicks = receiveTimestamp - _sendTimestamp;
var microseconds = elapsedTicks * 1_000_000.0 / Stopwatch.Frequency;
```

The receive timestamp is captured as the **first line** of the response callback to minimise jitter from continuation scheduling. What is included in this measurement:

- SignalR serialisation on the sender side
- HTTP/WebSocket framing and TLS record overhead (both directions)
- Server-side `BenchmarkHub.BenchmarkEcho` execution (a single `SendAsync` call)
- WebSocket receive and SignalR deserialisation on the client side

What is **not** included: cipher operations (those happen before `_sendTimestamp` is set and after the echoed bytes are recovered).

### Warmup and JIT steady-state

Before any measured messages, `BenchmarkMetricsCollector.RunBenchmark` (or the SignalR slice) runs `warmupCount` un-measured iterations. The default is 3.

The .NET runtime uses tiered JIT compilation: code starts running as Tier 0 (interpreted or minimally optimised) and is recompiled to Tier 1 (optimised native) after approximately 30 invocations. Three warmup rounds over `messageCount` messages typically exceeds 30 invocations of every cipher and MAC code path, putting the JIT at steady-state before the first measured result is taken.

For the SignalR-transport path, warmup runs the grain's `EncryptMessageAsync` and `DecryptMessageAsync` directly without sending through SignalR — only the cipher JIT is warmed, not the network path.

### Correctness checks

After decryption, `BenchmarkMetricsCollector` performs a constant-time equality check:

```csharp
decryptSuccess = recovered.Length == plaintext.Length
    && CryptographicOperations.FixedTimeEquals(recovered, plaintext);
```

`CryptographicOperations.FixedTimeEquals` compares all bytes in constant time regardless of where the first difference occurs, preventing timing side-channels on the correctness assertion itself.

`VerifySuccess` is the boolean returned directly by `authenticator.Verify`, which internally uses `CryptographicOperations.FixedTimeEquals` to compare the recomputed tag with the received tag.

Both flags are stored per-message in `BenchmarkMessageResult` and aggregated as `SuccessRate` in `BenchmarkSessionAggregate`.

---

## Part 2 — Per-Message GC Allocation Tracking

### Thread-local allocation counter

`GC.GetAllocatedBytesForCurrentThread()` returns the cumulative number of bytes allocated on the managed heap by the calling thread since the thread was created. Taking a before/after delta around the encrypt + sign phase gives the per-operation allocation count without interference from other threads:

```csharp
long allocBefore = GC.GetAllocatedBytesForCurrentThread();
// ... encrypt + sign ...
long allocAfter  = GC.GetAllocatedBytesForCurrentThread();
GcAllocatedBytes = Math.Max(0L, allocAfter - allocBefore);
```

`Math.Max(0L, ...)` guards against the counter rolling backwards, which can happen on some runtimes when a GC compaction moves objects.

This captures short-lived intermediate buffers created inside cipher and MAC implementations — for example:

- AES-GCM: nonce byte array, tag byte array, framed output byte array
- ML-KEM: KEM ciphertext array, shared secret array, HKDF output
- HMAC: internal block buffer

### Gen-0 collection pressure

`GcGen0Collections` tracks how many Gen-0 GC collections were triggered during the encrypt + sign phase. A non-zero value means the allocations within that phase exceeded the ephemeral segment threshold and forced a collection — relevant for latency jitter analysis because a Gen-0 collection pauses the thread momentarily.

---

## Part 3 — Per-Run Aggregate Statistics

After all measured messages complete, each slice computes a `BenchmarkSessionAggregateDaM` from the collected `MetricsSnapshot` list.

### Percentile computation

Percentiles are computed using the **nearest-rank method** over a sorted array:

```csharp
private static double Percentile(double[] sorted, double p)
{
    if (sorted.Length == 1) return sorted[0];
    int index = (int)Math.Ceiling(p * sorted.Length) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
}
```

P50, P95, and P99 are computed for `TotalRoundTripMicroseconds`. With the default of 20 measured messages, P95 resolves to the 19th highest value and P99 resolves to the highest value. Higher message counts give more distinct percentile buckets.

### Stored aggregate fields

| Field                           | Description                                                                      |
| ------------------------------- | -------------------------------------------------------------------------------- |
| `AvgEncryptMicroseconds`        | Mean of `EncryptMicroseconds` across all measured messages.                      |
| `AvgDecryptMicroseconds`        | Mean of `DecryptMicroseconds` across all measured messages.                      |
| `AvgSignMicroseconds`           | Mean of `SignMicroseconds`. Zero when no MAC.                                    |
| `AvgVerifyMicroseconds`         | Mean of `VerifyMicroseconds`. Zero when no MAC.                                  |
| `AvgSignalRTransitMicroseconds` | Mean of `SignalRTransitMicroseconds`. Zero for in-process runs.                  |
| `AvgTotalRoundTripMicroseconds` | Mean of `TotalRoundTripMicroseconds`.                                            |
| `MinRoundTripMicroseconds`      | Minimum `TotalRoundTripMicroseconds` across all messages.                        |
| `MaxRoundTripMicroseconds`      | Maximum.                                                                         |
| `P50RoundTripMicroseconds`      | 50th percentile (median).                                                        |
| `P95RoundTripMicroseconds`      | 95th percentile.                                                                 |
| `P99RoundTripMicroseconds`      | 99th percentile.                                                                 |
| `AvgCiphertextBytes`            | Mean ciphertext size per message.                                                |
| `AvgEncapsulatedKeyBytes`       | Mean encapsulated key size. Zero for symmetric.                                  |
| `AvgTotalWireBytes`             | Mean total wire bytes per message.                                               |
| `AvgEncryptionOverheadBytes`    | Mean of `TotalWireBytes − PlaintextBytes`.                                       |
| `TotalGcAllocatedBytes`         | Sum of `GcAllocatedBytes` across all messages.                                   |
| `AvgGcAllocatedBytesPerMessage` | Mean per-message heap allocation.                                                |
| `TotalGcGen0Collections`        | Sum of `GcGen0Collections` across all messages.                                  |
| `SuccessRate`                   | Fraction of messages where both `DecryptSuccess` and `VerifySuccess` are `true`. |

---

## Part 4 — Process-Level Resource Sampling

### ResourceMonitorBroadcaster

`ResourceMonitorBroadcaster` (`0.App.WebApiEncryption/Services/`) is an ASP.NET Core `BackgroundService` that runs continuously for the lifetime of the process. It samples process metrics every **100,000 µs (100 ms)** and:

1. Calls `_captureService.FeedSnapshot(snapshot)` — appends the snapshot to any open capture windows.
2. Calls `_hubContext.Clients.All.SendAsync("Metrics", snapshot)` — broadcasts live data to connected `EncryptionMonitor` clients.

Both actions happen on every tick regardless of whether a benchmark is running.

### What is measured per tick

```csharp
var proc = Process.GetCurrentProcess();
proc.Refresh();
```

| Metric                    | Source                                                      | Formula                                                 |
| ------------------------- | ----------------------------------------------------------- | ------------------------------------------------------- |
| `CpuPercent`              | `proc.TotalProcessorTime`                                   | `Δ(cpuTime) / (Δwall × coreCount) × 100`, clamped 0–100 |
| `MemoryUsedMb`            | `proc.WorkingSet64`                                         | Physical RAM pages mapped into this process, in MB      |
| `MemoryLimitMb`           | `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`            | Container or OS memory ceiling visible to this process  |
| `MemoryPercent`           | Computed                                                    | `MemoryUsedMb / MemoryLimitMb × 100`                    |
| `GcHeapMb`                | `GC.GetTotalMemory(false)`                                  | Managed GC heap size (no forced collection)             |
| `GcGen0Collections`       | `GC.CollectionCount(0)`                                     | Cumulative Gen-0 collections since process start        |
| `GcGen1Collections`       | `GC.CollectionCount(1)`                                     | Cumulative Gen-1                                        |
| `GcGen2Collections`       | `GC.CollectionCount(2)`                                     | Cumulative Gen-2                                        |
| `ThreadPoolWorkerThreads` | `ThreadPool.GetMaxThreads − ThreadPool.GetAvailableThreads` | Currently active worker threads                         |
| `UptimeSeconds`           | `DateTimeOffset.UtcNow − _startedAt`                        | Since broadcaster construction                          |
| `SignalRConnectionCount`  | `ConnectionTracker.Count`                                   | Total active SignalR connections across all hubs        |
| `OrleansGrainCount`       | `IManagementGrain.GetRuntimeStatistics`                     | Active grain activations in the silo                    |

CPU is a delta measurement: at each tick the broadcaster subtracts `_lastCpuTime` and `_lastMeasureTime` from the current values to compute the fraction of CPU time used since the previous tick. The first snapshot after process start always shows 0 or near-0.

Memory is an absolute working-set reading, not a delta. The resource aggregate corrects for this (see below).

### Capture window lifecycle

The slice calls `resourceCapture.StartCapture(runId)` immediately before the grain benchmark call and `resourceCapture.StopCapture(runId)` immediately after:

```csharp
// Before the measured loop — force a full blocking GC to establish a clean baseline
GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

resourceCapture?.StartCapture(runId);
var snapshots = await grain.RunBenchmarkAsync(messages, warmupCount);
var resourceSummary = resourceCapture?.StopCapture(runId);
```

The forced full GC before `StartCapture` ensures the memory baseline is at its lowest possible value, so the captured samples reflect only allocations caused by the benchmark run itself rather than accumulated garbage from previous runs.

`ResourceCaptureService` uses a `ConcurrentDictionary<Guid, CaptureSession>` so multiple concurrent benchmark runs can each accumulate their own sample buffer without locking each other. The broadcaster's `FeedSnapshot` call iterates all active sessions and appends to each.

### Aggregate computation from raw samples

`ResourceCaptureService.BuildSummary` converts the raw sample list into a `ResourceCaptureSummary`:

**CPU:**
The **first sample is skipped** when computing CPU aggregates. The broadcaster computes CPU% as a delta from its previous tick, so the first snapshot after `StartCapture` always covers the interval `[T − 100ms, T]` — straddling the pre-run boundary and including CPU from whatever ran before the benchmark. Every subsequent sample covers only in-run activity.

CPU samples are sorted and percentiled the same way as latency values (nearest-rank).

**Memory:**
Memory is reported as **deltas relative to `samples[0].GcHeapMb`** (the GC heap size at the start of the capture window):

```csharp
double memBaseline = samples[0].GcHeapMb;
var memDeltas = samples.Select(s => s.GcHeapMb - memBaseline).OrderBy(v => v).ToArray();
```

`GcHeapMb` (`GC.GetTotalMemory(false)`) is used rather than `WorkingSet64` (`MemoryUsedMb`). After the pre-run compacting GC, the CLR releases empty segments back to the OS via `VirtualFree`, but `WorkingSet64` does not drop immediately — the OS reclaims those pages asynchronously over the next several hundred milliseconds. Taking a baseline from `WorkingSet64` right after the GC captures an artificially high value, and subsequent samples show the OS progressively reclaiming pages, producing large spurious negative deltas (e.g. −300 MB on a run that allocates only a few hundred bytes). `GcHeapMb` reflects the true compacted heap size immediately and only changes when managed objects are allocated or collected, giving accurate in-run deltas. `MemoryMaxMb` therefore represents the peak increase in managed heap size above the run-start baseline; a small negative value means a mid-run GC collected more than was allocated since the baseline.

**GC collections:**
GC counters are cumulative since process start, so deltas are computed over the capture window:

```csharp
GcGen0Delta = samples[n - 1].GcGen0Collections - samples[0].GcGen0Collections;
```

**Thread pool:**
Peak active worker thread count seen across all samples within the window.

### Persistence

After `StopCapture` returns, the slice persists two things:

1. **Individual sample rows** — one `BenchmarkResourceSampleDaM` per snapshot tick, written to `benchmark.BenchmarkResourceSample`. Columns: `CapturedAt`, `CpuPercent`, `MemoryUsedMb`, `GcHeapMb`, `GcGen0/1/2Collections`, `ThreadPoolWorkerThreads`.

2. **Aggregate row** — one `BenchmarkResourceAggregateDaM` per run, written to `benchmark.BenchmarkResourceAggregate`. Contains `SampleCount`, `DurationMilliseconds`, CPU percentiles, memory deltas, GC deltas, and peak thread count.

The aggregate row is pre-computed so comparison queries across runs never need to scan the raw sample table.

---

## Part 5 — How Latency and Resource Data Flow Together

```
HTTP request arrives
    │
    ▼
EncryptionBenchmarkController
    │  resolves GC baseline (Collect gen-2, blocking)
    │  resourceCapture.StartCapture(runId)
    │
    ▼
grain.RunBenchmarkAsync(messages, warmupCount)
    │
    │   ┌─────────────────────────────────────────────┐
    │   │ BenchmarkMetricsCollector.Measure (per msg)  │
    │   │   Stopwatch timestamps around each phase     │
    │   │   GC.GetAllocatedBytesForCurrentThread()     │
    │   │   → MetricsSnapshot                          │
    │   └─────────────────────────────────────────────┘
    │                                ▲
    │   (every 100 ms)               │  concurrent
    │   ResourceMonitorBroadcaster   │
    │     BuildSnapshotAsync()       │
    │     FeedSnapshot(snapshot) ────┤ captured into CaptureSession buffer
    │     Clients.All.SendAsync(...)  → live dashboard (EncryptionMonitor)
    │
    ▼
resourceCapture.StopCapture(runId)
    │  → ResourceCaptureSummary (raw samples + aggregates)
    │
    ▼
Repository persistence
    ├── SaveRunAsync          → benchmark.BenchmarkRun
    ├── AddMessageResultsAsync → benchmark.BenchmarkMessageResult  (per-msg latency + GC)
    ├── SaveAggregateAsync    → benchmark.BenchmarkSessionAggregate (latency percentiles)
    ├── AddSamplesAsync       → benchmark.BenchmarkResourceSample    (per-tick CPU/mem)
    └── SaveAggregateAsync    → benchmark.BenchmarkResourceAggregate (resource percentiles)
```

The two measurement systems are independent: per-message latency (nanosecond precision, thread-local) runs inside the grain call; process resource sampling (100 ms interval, process-wide) runs in the background. They share only the `runId` as a correlation key so query joins are possible.

---

## Part 6 — Worked Example: ML-KEM-1024-Hybrid-AES-256-GCM with KMAC-256, SignalR Transport, and Payload Saving

This section walks through every logging and measurement event for a single benchmark run with both `useSignalRTransport=true` and `savePayloads=true` active simultaneously.

When `useSignalRTransport=true` is set the controller routes to `EncryptionBenchmarkSignalRDemo.Execute`, which performs the full per-message SignalR timing loop. Setting `savePayloads=true` at the same time is additive: the slice calls `BenchmarkWireFormat.Decode` on each `encResult.WirePayload` after the timing timestamps are captured, appends the raw bytes to an in-memory list, and writes payload files and DB rows after `StopCapture` — so the file I/O is excluded from the resource capture window.

Example configuration:

- **Algorithm:** `ML-KEM-1024-Hybrid-AES-256-GCM`
- **AuthId:** `KMAC-256`
- **TLS:** `Tls13`
- **useSignalRTransport:** `true`
- **savePayloads:** `true`
- **MessageCount:** 5 measured (plus 3 warmup)
- **MessageSizeBytes:** 256

---

### Pre-loop setup

```
1. Controller resolves hub URL from appsettings (e.g. https://localhost:7258/hubs/benchmark)
   Both flags active → routes to EncryptionBenchmarkSignalRDemo.Execute with:
     payloadRepository = _payloadRepository        ← populated because savePayloads=true
     storageRoot       = _payloadOptions.Value.ResolvedStorageRoot

2. A fresh BenchmarkSignalRClient is created for this (algId, authId) run:
     await signalRClient.ConnectAsync(hubUrl, Tls13, ...)
     → TLS 1.3 handshake over HTTPS
     → WebSocket upgrade
     → BenchmarkEchoResponse handler registered on the connection

3. Grain created:
     grain = grainFactory.GetGrain<IBenchmarkSessionGrain>(runId.ToString())
     await grain.ConfigureAsync("ML-KEM-1024-Hybrid-AES-256-GCM", "KMAC-256")
     → ML-KEM-1024 static key pair generated and held in grain private field
     → KMAC-256 key (32 random bytes) generated and held in grain private field
     → Key material is never serialised; lives only in this grain activation

4. 8 plaintext messages generated (3 warmup + 5 measured), each 256 random bytes

5. Warmup (3 rounds, grain-side only — no SignalR):
     for w in [0, 1, 2]:
         encResult = await grain.EncryptMessageAsync(messages[w])
         await grain.DecryptMessageAsync(encResult.WirePayload)
     → drives ML-KEM-1024 + AES-256-GCM and KMAC-256 code paths to JIT Tier 1 steady-state

6. Forced full compacting GC to establish a clean memory baseline:
     GC.Collect(2, Aggressive, blocking: true, compacting: true) × 2
     GC.WaitForPendingFinalizers()

7. resourceCapture.StartCapture(runId)
     → ConcurrentDictionary entry created for this runId
     → ResourceMonitorBroadcaster will now append every tick snapshot to this session

8. payloadCaptures list initialised (savePayloads=true, inside the capture window):
     payloadCaptures = new List<(byte[] Ciphertext, EncapsulatedKey, MacTag, ...)>(5)
```

### Per-message measured loop (repeated 5 times)

All timestamps are captured with `Stopwatch.GetTimestamp()` inside `BenchmarkSessionGrain` and `BenchmarkSignalRClient`. Times below are illustrative for ML-KEM-1024 on a typical developer machine.

```
┌─ Message 0 (index 0) ────────────────────────────────────────────────────────────┐
│                                                                                  │
│  Step (a): grain.EncryptMessageAsync(plaintext[3])    ← warmupCount offset       │
│  ┌── inside BenchmarkSessionGrain ───────────────────────────────────────────┐   │
│  │  t0 = Stopwatch.GetTimestamp()                                            │   │
│  │  Generate fresh ephemeral ML-KEM-1024 key pair                            │   │
│  │  ML-KEM encapsulate(staticPublicKey)                                      │   │
│  │    → KEM ciphertext (1568 B) + shared secret (32 B)                       │   │
│  │  HKDF-SHA256: shared secret → 32-byte AES-256-GCM session key             │   │
│  │  AES-256-GCM encrypt(plaintext, freshNonce) → ciphertext (268 B)          │   │
│  │  t1 = Stopwatch.GetTimestamp()                                            │   │
│  │  EncryptMicroseconds = (t1 − t0) × µs/tick          e.g.  374.5 µs        │   │
│  │                                                                           │   │
│  │  t2 = Stopwatch.GetTimestamp()                                            │   │
│  │  KMAC-256.Sign(ciphertext ‖ KEM ciphertext) → tag (32 B)                  │   │
│  │  (input to KMAC: 268 + 1568 = 1836 B)                                     │   │
│  │  t3 = Stopwatch.GetTimestamp()                                            │   │
│  │  SignMicroseconds = (t3 − t2) × µs/tick              e.g.   21.3 µs       │   │
│  │                                                                           │   │
│  │  Returns EncryptPhaseResult:                                              │   │
│  │    WirePayload         = BenchmarkWireFormat.Encode(ct, ek, tag)          │   │
│  │    PlaintextBytes      = 256                                              │   │
│  │    CiphertextBytes     = 268  (256 B data + 12 B nonce overhead)          │   │
│  │    EncapsulatedKeyBytes = 1568  (ML-KEM-1024 KEM ciphertext)              │   │
│  │    MacTagBytes         = 32                                               │   │
│  └───────────────────────────────────────────────────────────────────────────┘   │
│                                                                                  │
│  Step (a.1) — payload capture side-channel (savePayloads=true):                  │
│    Runs after t3 is captured, so no impact on the timing measurements.           │
│    (ct, ek, tag) = BenchmarkWireFormat.Decode(encResult.WirePayload)             │
│    payloadCaptures.Add((ct=268B, ek=1568B, tag=32B, ptBytes=256, wire=1868B))    │
│                                                                                  │
│  Step (b): signalRClient.SendEchoAsync(Convert.ToBase64String(wirePayload))      │
│  ┌── inside BenchmarkSignalRClient ──────────────────────────────────────────┐   │
│  │  _sendTimestamp = Stopwatch.GetTimestamp()                                │   │
│  │  hubConnection.SendAsync("BenchmarkEcho", base64Payload)                  │   │
│  │    → ~2507 B base64 string (1880 B wire → base64)                         │   │
│  │    → TLS 1.3 record written to WebSocket                                  │   │
│  │    → arrives at BenchmarkHub.BenchmarkEcho on server                      │   │
│  │    → hub calls Clients.Caller.SendAsync("BenchmarkEchoResponse", payload) │   │
│  │    → TLS 1.3 record returns over same WebSocket                           │   │
│  │  BenchmarkEchoResponse handler fires:                                     │   │
│  │    _receiveTimestamp = Stopwatch.GetTimestamp()   ← FIRST line            │   │
│  │    _pendingEcho.TrySetResult((_receiveTimestamp, payload))                │   │
│  │  SignalRTransitMicroseconds = (_receiveTimestamp − _sendTimestamp) × µs/tick  │
│  │                                               e.g.  823.7 µs              │   │
│  │  (higher than ECDH path due to the larger ML-KEM TLS records)             │   │
│  └───────────────────────────────────────────────────────────────────────────┘   │
│                                                                                  │
│  Step (c): grain.DecryptMessageAsync(Convert.FromBase64String(receivedPayload))  │
│  ┌── inside BenchmarkSessionGrain ───────────────────────────────────────────┐   │
│  │  t4 = Stopwatch.GetTimestamp()                                            │   │
│  │  BenchmarkWireFormat.Decode → ciphertext / KEM ciphertext / tag           │   │
│  │  ML-KEM-1024 decapsulate(KEM ciphertext) → shared secret                  │   │
│  │  HKDF-SHA256: shared secret → AES-256-GCM session key                     │   │
│  │  AES-256-GCM decrypt(ciphertext, nonce) → plaintext                       │   │
│  │  t5 = Stopwatch.GetTimestamp()                                            │   │
│  │  DecryptMicroseconds = (t5 − t4) × µs/tick           e.g.  338.2 µs       │   │
│  │                                                                           │   │
│  │  t6 = Stopwatch.GetTimestamp()                                            │   │
│  │  KMAC-256.ComputeHash(ciphertext ‖ KEM ciphertext)                        │   │
│  │  CryptographicOperations.FixedTimeEquals(computed, tag)                   │   │
│  │  t7 = Stopwatch.GetTimestamp()                                            │   │
│  │  VerifyMicroseconds = (t7 − t6) × µs/tick              e.g.   20.8 µs     │   │
│  │                                                                           │   │
│  │  Returns DecryptPhaseResult { DecryptSuccess: true, VerifySuccess: true } │   │
│  └───────────────────────────────────────────────────────────────────────────┘   │
│                                                                                  │
│  MetricsSnapshot assembled in slice:                                             │
│    EncryptMicroseconds          =  374.5 µs                                      │
│    SignMicroseconds             =   21.3 µs                                      │
│    SignalRTransitMicroseconds   =  823.7 µs                                      │
│    DecryptMicroseconds          =  338.2 µs                                      │
│    VerifyMicroseconds           =   20.8 µs                                      │
│    ─────────────────────────────────────────                                     │
│    TotalRoundTripMicroseconds   = 1578.5 µs  (computed property, sum of above)   │
│                                                                                  │
│    PlaintextBytes               =   256 B                                        │
│    CiphertextBytes              =   268 B                                        │
│    EncapsulatedKeyBytes         =  1568 B                                        │
│    MacTagBytes                  =    32 B                                        │
│    TotalWireBytes               =  1868 B  (268 + 1568 + 32)                     │
│    EncryptionOverheadBytes      =  1612 B  (1868 − 256)                          │
└──────────────────────────────────────────────────────────────────────────────────┘
      ↑ messages 1–4 repeat the same structure
```

### Background resource sampling (concurrent with the loop)

```
Every 100 ms — ResourceMonitorBroadcaster.BuildSnapshotAsync() fires:
  CPU%     = Δ(proc.TotalProcessorTime) / (Δwall × coreCount) × 100
  MemUsedMb = proc.WorkingSet64 / 1 048 576
  GcHeapMb = GC.GetTotalMemory(false) / 1 048 576
  ThreadPoolWorkerThreads = maxWorkers − availableWorkers
  SignalRConnectionCount  = ConnectionTracker.Count
  OrleansGrainCount       = IManagementGrain.GetRuntimeStatistics(...)

  → FeedSnapshot(snapshot)  appends to this run's CaptureSession buffer
  → Clients.All.SendAsync("Metrics", snapshot)  pushes to live monitor dashboard
```

ML-KEM-1024 lattice operations are substantially more CPU-intensive than ECDH-based algorithms. Expect higher `CpuPercent` readings and larger `GcHeapMb` deltas compared to classical-crypto runs, due to the larger intermediate byte arrays (KEM ciphertext, shared secret, HKDF input/output buffers).

### Post-loop persistence

```
resourceSummary = resourceCapture.StopCapture(runId)
  → CaptureSession removed from dictionary
  → ResourceCaptureSummary computed:
      CPU samples: first sample dropped (straddles pre-run boundary)
      Memory: GcHeapMb deltas relative to samples[0]
      GC gen counts: last − first across capture window
      Peak thread-pool workers

await grain.CompleteSessionAsync()
  → grain calls DeactivateOnIdle()
  → OnDeactivateAsync fires: ML-KEM key pair + KMAC-256 key disposed and zeroed

Persistence (sequential):
  repository.SaveRunAsync(runEntity)
    → INSERT benchmark.BenchmarkRun
         BenchmarkRunId, RunAt, AlgorithmId="ML-KEM-1024-Hybrid-AES-256-GCM",
         AuthId="KMAC-256", TlsVersion="Tls13", MessageCount=5,
         MessageSizeBytes=256, WarmupCount=3, RunNumber=N, AlgorithmRunNumber=M

  repository.AddMessageResultsAsync(messageResults)
    → INSERT benchmark.BenchmarkMessageResult × 5
         MessageIndex, EncryptMicroseconds, SignMicroseconds,
         SignalRTransitMicroseconds, DecryptMicroseconds, VerifyMicroseconds,
         TotalRoundTripMicroseconds,
         PlaintextBytes, CiphertextBytes, EncapsulatedKeyBytes, MacTagBytes,
         TotalWireBytes, EncryptionOverheadBytes,
         DecryptSuccess, VerifySuccess

  repository.SaveAggregateAsync(aggregate)
    → INSERT benchmark.BenchmarkSessionAggregate × 1
         AvgEncryptMicroseconds, AvgSignMicroseconds, AvgSignalRTransitMicroseconds,
         AvgDecryptMicroseconds, AvgVerifyMicroseconds, AvgTotalRoundTripMicroseconds,
         MinRoundTripMicroseconds, P50RoundTripMicroseconds,
         P95RoundTripMicroseconds, P99RoundTripMicroseconds, MaxRoundTripMicroseconds,
         AvgTotalWireBytes, AvgEncryptionOverheadBytes, SuccessRate, ...

  resourceRepository.AddSamplesAsync(resourceSamples)
    → INSERT benchmark.BenchmarkResourceSample × N  (one per 100 ms tick)
         CapturedAt, CpuPercent, MemoryUsedMb, GcHeapMb, ThreadPoolWorkerThreads

  resourceRepository.SaveAggregateAsync(resourceAggregate)
    → INSERT benchmark.BenchmarkResourceAggregate × 1
         CpuAvg, CpuP50, CpuP95, CpuP99, CpuMax,
         MemoryAvgMb, MemoryP50Mb, MemoryP95Mb, MemoryP99Mb, MemoryMaxMb,
         DurationMilliseconds, SampleCount, ThreadPoolMaxWorkers

  ← payload persistence runs after StopCapture — file I/O is not inside the resource capture window:

  Step 8 — write payload files to disk:
    runDir = {storageRoot}/{runId}/ML-KEM-1024-Hybrid-AES-256-GCM/
    for each message i (0..4):
      WritePayloadBytesAsync({runDir}/{i:D3}.bin, ct, encapsulatedKey, tag)
      → e.g. storage/a1b2c3.../ML-KEM-1024-Hybrid-AES-256-GCM/000.bin  (1868 B)
                                                                001.bin  (1868 B)
                                                                ...

  payloadRepository.SavePayloadsAsync(payloadEntities)
    → INSERT benchmark.BenchmarkMessagePayload × 5
         BenchmarkRunId, MessageIndex, AlgorithmId,
         Ciphertext       (268 B blob),
         EncapsulatedKey  (1568 B blob),
         MacTag           (32 B blob),
         PlaintextBytes=256, TotalWireBytes=1868,
         FilePath = relative path to the .bin file
```

The on-disk files allow external tools (Explorer, `Get-ChildItem`, etc.) to inspect the post-quantum wire size directly. The DB payload rows allow SQL queries to compare ciphertext sizes across algorithms without reading files.

---

### Timing field summary for the example run

| Field                        | Value (illustrative) | What is timed                                                        |
| ---------------------------- | -------------------- | -------------------------------------------------------------------- |
| `EncryptMicroseconds`        | ~375 µs              | ML-KEM-1024 keygen + encapsulate + HKDF + AES-256-GCM encrypt        |
| `SignMicroseconds`           | ~21 µs               | KMAC-256 over ciphertext ‖ KEM ciphertext (1836 B input)             |
| `SignalRTransitMicroseconds` | ~824 µs              | TLS 1.3 WebSocket round-trip; larger than ECDH due to bigger payload |
| `DecryptMicroseconds`        | ~338 µs              | ML-KEM-1024 decapsulate + HKDF + AES-256-GCM decrypt                 |
| `VerifyMicroseconds`         | ~21 µs               | KMAC-256 recompute + constant-time compare                           |
| `TotalRoundTripMicroseconds` | ~1579 µs             | Sum of all five fields                                               |

The encrypt and decrypt phases dominate the non-network cost because ML-KEM-1024 lattice operations are substantially more expensive than symmetric primitives or classical key exchange. The SignalR transit is proportionally higher than for classical-crypto runs because the larger wire payload (1868 B vs ~391 B for ECDH-P256) requires more TLS records per WebSocket frame.

---

## Part 7 — Precision and Limitations

| Concern                        | Detail                                                                                                                                                                                                                                                                                            |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Timer resolution               | `Stopwatch.Frequency` on Windows x64 is typically 10 MHz (100 ns per tick), giving ~0.1 µs timing resolution. Actual jitter is higher (OS scheduling, cache effects).                                                                                                                             |
| First-sample CPU bias          | The first resource sample after `StartCapture` is excluded from CPU aggregates because it straddles the pre-run boundary.                                                                                                                                                                         |
| Memory working-set granularity | `WorkingSet64` is page-granular (4 KB on x86/x64). Small allocations may not be visible until a page fault occurs.                                                                                                                                                                                |
| Short runs                     | If a benchmark run completes in less than 100 ms, no resource samples may be collected at all. `StopCapture` returns `null` in that case and no resource rows are written.                                                                                                                        |
| GC thread interference         | `GcGen0Collections` in `MetricsSnapshot` counts only collections on the measurement thread. The process-level `GcGen0Delta` in `ResourceCaptureSummary` counts all Gen-0 collections process-wide within the window, including those triggered by other threads (Orleans, SignalR, ASP.NET Core). |
| SignalR transit jitter         | `SignalRTransitMicroseconds` includes OS TCP stack scheduling jitter and TLS handshake state. Individual values vary significantly; the aggregate average is more stable.                                                                                                                         |

---

## Part 8 — Source File Reference

| File                                                                       | Role                                                                                                                                                                                                                |
| -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `1.Benchmark.Grains/Grains/BenchmarkSessionGrain.cs`                       | Per-message timing for the SignalR transport path. `EncryptMessageAsync` and `DecryptMessageAsync` capture `Stopwatch.GetTimestamp()` before and after each cipher phase using `1_000_000.0 / Stopwatch.Frequency`. |
| `1.Domain.Services/Encryptions/Benchmark/BenchmarkMetricsCollector.cs`     | Core latency measurement engine. `Measure`, `RunBenchmark`, `RunBenchmarkWithPayloads`.                                                                                                                             |
| `1.Domain.Services/Encryptions/Benchmark/MetricsSnapshot.cs`               | Per-message result value type. All timing, size, GC, and correctness fields.                                                                                                                                        |
| `1.Benchmark.Client/Services/BenchmarkSignalRClient.cs`                    | SignalR transit timing. `SendEchoAsync` captures send/receive timestamps.                                                                                                                                           |
| `4a.CrossCut.Concerns/Monitor/IResourceCaptureService.cs`                  | Capture window interface (`StartCapture`, `StopCapture`, `FeedSnapshot`).                                                                                                                                           |
| `4a.CrossCut.Concerns/Monitor/ResourceCaptureSummary.cs`                   | Aggregate result record from `StopCapture`.                                                                                                                                                                         |
| `4a.CrossCut.Concerns/Monitor/ResourceMetricsSnapshot.cs`                  | Per-tick point-in-time snapshot record.                                                                                                                                                                             |
| `0.App.WebApiEncryption/Services/ResourceCaptureService.cs`                | Capture window implementation. Thread-safe concurrent dictionary of sessions.                                                                                                                                       |
| `0.App.WebApiEncryption/Services/ResourceMonitorBroadcaster.cs`            | Background service. CPU/memory/GC sampling loop at 100 ms interval.                                                                                                                                                 |
| `3c.DataAccess.Models/Database/Benchmark/BenchmarkSessionAggregateDaM.cs`  | EF entity for per-run latency aggregate.                                                                                                                                                                            |
| `3c.DataAccess.Models/Database/Benchmark/BenchmarkResourceAggregateDaM.cs` | EF entity for per-run resource aggregate.                                                                                                                                                                           |
