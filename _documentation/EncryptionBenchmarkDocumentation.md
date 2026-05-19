# Encryption Benchmark — Documentation

## Overview

The encryption benchmark is the core of the project. It measures the performance and wire-size overhead of multiple cryptographic algorithms across four generations — from deprecated legacy ciphers (3DES) up to post-quantum algorithms (ML-KEM / FIPS 203) — under a uniform measurement harness.

Every run is driven through **Orleans grains** so that key material is isolated per run, results are persisted to **PostgreSQL** (`benchmark` schema), and the HTTP API remains stateless. An optional **SignalR echo test** captures real network round-trip latency on top of the cipher timings.

---

## Algorithm Catalog

All algorithms are registered in `AlgorithmCatalog` (`1.Domain.Services/Encryptions/Benchmark/AlgorithmCatalog.cs`). Each entry carries metadata (family, generation, key size, hybrid flag, max payload, library) that the controller uses for input validation and database records.

### Generations

| Generation    | Description                                                                                                    |
| ------------- | -------------------------------------------------------------------------------------------------------------- |
| `Deprecated`  | Formally deprecated by NIST (e.g. NIST SP 800-131A for 3DES). Included to show the cost of compatibility debt. |
| `Legacy`      | Widely deployed, not yet deprecated, but superseded by modern variants.                                        |
| `Modern`      | Current best-practice (AES-GCM, ChaCha20-Poly1305, ECDH-Hybrid).                                               |
| `PostQuantum` | Post-quantum safe (NIST PQC / ML-KEM, FIPS 203).                                                               |

### Cipher Algorithms

#### Symmetric — Deprecated (BCL)

| Algorithm ID   | Key Bits | Notes                                                                                                   |
| -------------- | -------- | ------------------------------------------------------------------------------------------------------- |
| `3DES-128-CBC` | 128      | Two-key 3DES, 112-bit effective security. 64-bit block — vulnerable to SWEET32. NIST deprecated 2017.   |
| `3DES-192-CBC` | 192      | Three-key 3DES, 168-bit effective security. 64-bit block — vulnerable to SWEET32. NIST deprecated 2017. |

#### Symmetric — Legacy (BCL)

| Algorithm ID  | Key Bits | Notes                                                                          |
| ------------- | -------- | ------------------------------------------------------------------------------ |
| `AES-128-CBC` | 128      | Hardware-accelerated on x86/ARM. No built-in authentication (unauthenticated). |
| `AES-192-CBC` | 192      | Rarely used; not supported in some TLS suites.                                 |
| `AES-256-CBC` | 256      | Previous production cipher; superseded by GCM variants.                        |

#### Symmetric — Legacy (BouncyCastle)

| Algorithm ID      | Key Bits | Notes                                               |
| ----------------- | -------- | --------------------------------------------------- |
| `Twofish-128-CBC` | 128      | AES finalist. Requires `BouncyCastle.Cryptography`. |
| `Twofish-192-CBC` | 192      | AES finalist. Requires `BouncyCastle.Cryptography`. |
| `Twofish-256-CBC` | 256      | AES finalist. Requires `BouncyCastle.Cryptography`. |

#### Symmetric — Modern (BCL)

| Algorithm ID        | Key Bits | Notes                                                                  |
| ------------------- | -------- | ---------------------------------------------------------------------- |
| `AES-128-GCM`       | 128      | AEAD: authentication built in. Mandatory TLS 1.3 cipher suite.         |
| `AES-256-GCM`       | 256      | AEAD. Recommended modern symmetric cipher.                             |
| `ChaCha20-Poly1305` | 256      | Stream cipher. TLS 1.3 alternative to AES on platforms without AES-NI. |

#### Asymmetric — Legacy (BCL, non-hybrid RSA)

| Algorithm ID | Key Bits | Max Payload | Notes                                                                      |
| ------------ | -------- | ----------- | -------------------------------------------------------------------------- |
| `RSA-2048`   | 2048     | 190 B       | RSA-OAEP-SHA256. Direct encryption — payload strictly limited by key size. |
| `RSA-4096`   | 4096     | 446 B       | RSA-OAEP-SHA256. Direct encryption.                                        |

> The 190/446 byte limits come from: modulus size − 2 × SHA-256 hash size − 2 bytes.
> The benchmark automatically clamps `messageSizeBytes` to these limits for RSA ciphers.

#### Hybrid — Legacy (BCL, RSA wrapping AES)

| Algorithm ID                  | Notes                                                                              |
| ----------------------------- | ---------------------------------------------------------------------------------- |
| `RSA-2048-Hybrid-AES-256-GCM` | RSA-OAEP wraps a fresh AES-256-GCM session key per message. No message size limit. |
| `RSA-4096-Hybrid-AES-256-GCM` | Same as above with a 4096-bit RSA key.                                             |

#### Hybrid — Modern (BCL, ECDH key agreement)

| Algorithm ID                   | Notes                                                                                                                      |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------- |
| `ECDH-P256-Hybrid-AES-256-GCM` | Ephemeral ECDH key agreement (P-256 curve) + AES-256-GCM data cipher. Encapsulated key = ephemeral public key DER (~91 B). |
| `ECDH-P384-Hybrid-AES-256-GCM` | Same as above with P-384 curve (~120 B encapsulated key).                                                                  |

#### Hybrid — Post-Quantum (.NET 10 BCL, ML-KEM)

| Algorithm ID                     | NIST Level | Notes                                                                               |
| -------------------------------- | ---------- | ----------------------------------------------------------------------------------- |
| `ML-KEM-768-Hybrid-AES-256-GCM`  | 3          | ML-KEM-768 (FIPS 203) KEM + AES-256-GCM. Encapsulated key = 1088 B KEM ciphertext.  |
| `ML-KEM-1024-Hybrid-AES-256-GCM` | 5          | ML-KEM-1024 (FIPS 203) KEM + AES-256-GCM. Encapsulated key = 1568 B KEM ciphertext. |

---

## Authenticators (MAC layer)

An authenticator can be layered on top of any cipher using encrypt-then-MAC order (the ciphertext + encapsulated key is signed, not the plaintext). Passing `authId` to the `/run` endpoint enables this layer.

All authenticators are in `1.Domain.Services/Encryptions/Benchmark/Authenticators/`.

| Auth ID         | Tag Size | Notes                                                        |
| --------------- | -------- | ------------------------------------------------------------ |
| `HMAC-SHA256`   | 32 B     | Standard HMAC with SHA-256.                                  |
| `HMAC-SHA512`   | 64 B     | Standard HMAC with SHA-512.                                  |
| `HMAC-SHA3-256` | 32 B     | HMAC with SHA-3 (Keccak).                                    |
| `HMAC-SHA3-512` | 64 B     | HMAC with SHA-3 (Keccak).                                    |
| `KMAC-128`      | variable | NIST SP 800-185. cSHAKE128-based MAC. Native in .NET 10 BCL. |
| `KMAC-256`      | variable | NIST SP 800-185. cSHAKE256-based MAC. Native in .NET 10 BCL. |

---

## Measurement Model

### `MetricsSnapshot`

Defined in `1.Domain.Services/Encryptions/Benchmark/MetricsSnapshot.cs`.
An Orleans-serializable `readonly struct` capturing everything measurable for one message round-trip.

**Timing fields (all in microseconds, µs):**

| Field                        | Description                                                                                                                                  |
| ---------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `EncryptMicroseconds`        | Wall-clock time for `IMessageCipher.Encrypt`.                                                                                                |
| `SignMicroseconds`           | Wall-clock time for `IMessageAuthenticator.Sign`. Zero if no MAC.                                                                            |
| `SignalRTransitMicroseconds` | Wall-clock round-trip through `BenchmarkHub`, measured by `BenchmarkSignalRClient`. Zero in the standard in-process path.                    |
| `DecryptMicroseconds`        | Wall-clock time for `IMessageCipher.Decrypt`.                                                                                                |
| `VerifyMicroseconds`         | Wall-clock time for `IMessageAuthenticator.Verify`. Zero if no MAC.                                                                          |
| `TotalRoundTripMicroseconds` | Sum of all five timing fields: encrypt + sign + SignalR transit + decrypt + verify. SignalR transit is zero in the standard in-process path. |

**Size fields:**

| Field                  | Description                                                                              |
| ---------------------- | ---------------------------------------------------------------------------------------- |
| `PlaintextBytes`       | Original message size.                                                                   |
| `CiphertextBytes`      | Encrypted payload size. For symmetric ciphers this includes the embedded IV/nonce.       |
| `EncapsulatedKeyBytes` | Size of the key-encapsulation material. Zero for symmetric ciphers.                      |
| `MacTagBytes`          | MAC tag size. Zero if no authenticator.                                                  |
| `TotalWireBytes`       | `CiphertextBytes + EncapsulatedKeyBytes + MacTagBytes` — what would be sent on the wire. |

**GC fields:**

| Field             | Description                                                                                                   |
| ----------------- | ------------------------------------------------------------------------------------------------------------- |
| `AllocatedBytes`  | Heap bytes allocated by the encrypt + sign phase (thread-local, from `GC.GetAllocatedBytesForCurrentThread`). |
| `Gen0Collections` | GC gen-0 collections triggered during encrypt + sign.                                                         |
| `DecryptSuccess`  | Whether decrypt + constant-time equality check passed.                                                        |
| `VerifySuccess`   | Whether MAC verify passed.                                                                                    |

> GC tracking covers only the **send side** (encrypt + sign). Decrypt/verify are timed for round-trip completeness but their allocations are excluded to keep the metric focused on what a server pays per outgoing message.

---

## Benchmark Engine

### `BenchmarkMetricsCollector`

`1.Domain.Services/Encryptions/Benchmark/BenchmarkMetricsCollector.cs`

The static measurement engine. All operations are **synchronous and sequential** — no parallelism — so that GC counts and timing are attributable to a single logical operation.

**Measurement order per message:**

```
1. GC snapshot before
2. Stopwatch: cipher.Encrypt(plaintext)         → EncryptMicroseconds
3. Stopwatch: authenticator.Sign(ciphertext)     → SignMicroseconds   (if MAC enabled)
4. GC snapshot after
5. Stopwatch: cipher.Decrypt(ciphertext)         → DecryptMicroseconds
6. Stopwatch: authenticator.Verify(ciphertext)   → VerifyMicroseconds (if MAC enabled)
7. Constant-time equality check on recovered plaintext
```

**Warmup:** The `RunBenchmark` methods run `warmupCount` (default 3) un-measured iterations first to bring the .NET tiered JIT to steady-state, preventing the first measured message from being a timing outlier.

**Two public APIs:**

- `RunBenchmark(cipher, authenticator, messages, warmupCount)` → `IReadOnlyList<MetricsSnapshot>` — metrics only.
- `RunBenchmarkWithPayloads(cipher, authenticator, messages, warmupCount)` → `IReadOnlyList<PayloadCapture>` — metrics + raw ciphertext bytes (used by the payload demo path).

### `PayloadCapture`

`1.Domain.Services/Encryptions/Benchmark/PayloadCapture.cs`

Pairs a `MetricsSnapshot` with the raw bytes from one encryption operation:

| Property          | Description                                              |
| ----------------- | -------------------------------------------------------- |
| `Metrics`         | The `MetricsSnapshot` for this message.                  |
| `Ciphertext`      | Raw encrypted bytes.                                     |
| `EncapsulatedKey` | Key encapsulation material (null for symmetric ciphers). |
| `MacTag`          | MAC tag bytes (null if no authenticator).                |

---

## Orleans Grains

Key material is generated and held inside Orleans grains, giving each benchmark run an isolated cryptographic context.

### `IBenchmarkSessionGrain` / `BenchmarkSessionGrain`

**Used by:** standard benchmark path (`EncryptionBenchmarkDemo`) and SignalR-transport path (`EncryptionBenchmarkSignalRDemo`)
**Keyed by:** run GUID (string key)

| Method                                     | Description                                                                                                                                            |
| ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `ConfigureAsync(algorithmId, authId)`      | Generates key material; holds cipher + authenticator as non-serialisable grain fields.                                                                 |
| `RunBenchmarkAsync(messages, warmupCount)` | Runs the full in-process benchmark via `BenchmarkMetricsCollector`. Calls `DeactivateOnIdle()` on return. Used by the standard path.                   |
| `GetConfigurationAsync()`                  | Returns the current algorithm ID, auth ID, and configured flag.                                                                                        |
| `EncryptMessageAsync(plaintext)`           | Encrypts one message and returns `EncryptPhaseResult` (wire payload, encrypt + sign timings, GC data, byte sizes). Used by the SignalR-transport path. |
| `DecryptMessageAsync(wirePayload)`         | Decodes the binary wire format, decrypts, and optionally verifies the MAC. Returns `DecryptPhaseResult`. Used by the SignalR-transport path.           |
| `CompleteSessionAsync()`                   | Calls `DeactivateOnIdle()` to release key material. Called by the slice after the measured loop ends.                                                  |

### `IBenchmarkPayloadGrain` / `BenchmarkPayloadGrain`

**Used by:** payload-capture benchmark path (`EncryptionBenchmarkPayloadDemo`)
**Keyed by:** run GUID (string key)
**Responsibility:** Same as session grain but drives `BenchmarkMetricsCollector.RunBenchmarkWithPayloads` and returns `IReadOnlyList<PayloadCapture>` (metrics + raw bytes).

Both grains are colocated in-process with the web API via an embedded Orleans silo (no separate cluster node needed).

---

## Execution Paths (Slices)

### Standard path — `EncryptionBenchmarkDemo`

`0.Slices/UseCases/EncryptionBenchmarkDemo.cs`

Used when `savePayloads=false` (default).

For each algorithm:

1. Obtain a fresh `IBenchmarkSessionGrain` keyed by a new run GUID.
2. `ConfigureAsync(algId, authId)` — generates key material on the grain.
3. Generate `messageCount + warmupCount` random byte arrays of `messageSizeBytes`.
4. `RunBenchmarkAsync(messages, warmupCount)` → `IReadOnlyList<MetricsSnapshot>`.
5. Persist to database:
    - `BenchmarkRunDaM` — run header.
    - `BenchmarkMessageResultDaM` — one row per measured message.
    - `BenchmarkSessionAggregateDaM` — Min/Max/P50/P95/P99 aggregate over the run.
6. Optionally connect a `BenchmarkSignalRClient` and perform one echo per measured message (see SignalR section).
7. Return JSON with per-algorithm summaries, run IDs, and optional SignalR latency stats.

### Payload-capture path — `EncryptionBenchmarkPayloadDemo`

`0.Slices/UseCases/EncryptionBenchmarkPayloadDemo.cs`

Used when `savePayloads=true`.

Same flow as the standard path except:

- Uses `IBenchmarkPayloadGrain` → `RunAndCaptureAsync` → `IReadOnlyList<PayloadCapture>`.
- Saves raw bytes to **PostgreSQL** (`benchmark.BenchmarkMessagePayload` table).
- Saves raw bytes to **disk** as `.bin` files under the configured `StorageRoot`:

```
{storageRoot}/{runId}/{algorithmId}/{index:D3}.bin
```

Each `.bin` file contains the concatenated wire bytes: `Ciphertext || EncapsulatedKey || MacTag`.

This path enables file-system size comparison via `DirectoryInfo`/`FileInfo` (see storage endpoints).

### SignalR-transport path — `EncryptionBenchmarkSignalRDemo`

`0.Slices/UseCases/EncryptionBenchmarkSignalRDemo.cs`

Used when `useSignalRTransport=true`.

Unlike the two paths above, this slice routes every encrypted payload through the SignalR hub, making the network layer a first-class part of the measurement. Cipher operations are still executed inside the grain, but the payload travels over the real network connection between encrypt and decrypt.

For each algorithm:

1. A `BenchmarkSignalRClient` connects to `BenchmarkHub` once, shared across all measured messages for the request.
2. Obtain a fresh `IBenchmarkSessionGrain` keyed by a new run GUID.
3. `ConfigureAsync(algId, authId)` — generates key material on the grain.
4. Generate `messageCount + warmupCount` random byte arrays of `messageSizeBytes`.
5. **Warmup** (cipher JIT only, no SignalR): `grain.EncryptMessageAsync` → `grain.DecryptMessageAsync` for each warmup message.
6. **Measured loop** per message:
    - `grain.EncryptMessageAsync(plaintext)` → `EncryptPhaseResult` (wire payload + encrypt/sign timings + GC data).
    - `Convert.ToBase64String(wirePayload)` — encodes the binary wire format as a SignalR-safe string.
    - `signalRClient.SendEchoAsync(base64)` → hub echoes back; `SignalRRoundTripResult.RoundTripMicroseconds` captured.
    - `Convert.FromBase64String(signalRResult.ReceivedPayload)` → recovers the wire bytes returned by the hub.
    - `grain.DecryptMessageAsync(receivedWireBytes)` → `DecryptPhaseResult` (decrypt/verify timings + success flags).
    - `MetricsSnapshot` built with all five timing dimensions including `SignalRTransitMicroseconds`.
7. `grain.CompleteSessionAsync()` — releases key material via `DeactivateOnIdle()`.
8. Persist run header, per-message results, aggregate, and optional resource samples — same pattern as the standard path.

**Wire format** — `BenchmarkWireFormat` (`1.Domain.Services/Encryptions/Benchmark/BenchmarkWireFormat.cs`):

A length-prefixed little-endian binary packing used to carry all three ciphertext components through a single SignalR string payload:

```
[4B ciphertextLen][ciphertext][4B encKeyLen][encKey][4B tagLen][tag]
```

Zero-length segments represent `null` encapsulated key (symmetric ciphers) or `null` MAC tag (unauthenticated runs). The packed bytes are base64-encoded by the slice before sending and base64-decoded after receiving.

**New result structs** — Orleans-serializable `readonly struct`s in `1.Domain.Services/Encryptions/Benchmark/`:

- `EncryptPhaseResult` — returned by `grain.EncryptMessageAsync`. Carries `WirePayload`, `EncryptMicroseconds`, `SignMicroseconds`, GC data, and individual byte-size fields.
- `DecryptPhaseResult` — returned by `grain.DecryptMessageAsync`. Carries `DecryptMicroseconds`, `VerifyMicroseconds`, `DecryptSuccess`, `VerifySuccess`.

---

## HTTP API

All endpoints are on `EncryptionBenchmarkController` at `/api/encryptionbenchmark`.

### `GET /api/encryptionbenchmark/run`

Runs the benchmark and persists results.

| Query Parameter       | Type     | Default         | Description                                                                                                                                                                            |
| --------------------- | -------- | --------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `algorithms`          | string[] | _(all)_         | One or more cipher algorithm IDs. Omit to run all.                                                                                                                                     |
| `authId`              | string   | _(none)_        | MAC authenticator ID. Omit to skip MAC layer.                                                                                                                                          |
| `messageCount`        | int      | `20`            | Messages to measure per algorithm after warmup.                                                                                                                                        |
| `messageSizeBytes`    | int      | `256`           | Plaintext payload size in bytes. Auto-clamped for RSA.                                                                                                                                 |
| `warmupCount`         | int      | `3`             | Warmup iterations before measurement.                                                                                                                                                  |
| `savePayloads`        | bool     | `false`         | When true, saves raw ciphertext to DB and disk.                                                                                                                                        |
| `useSignalRTransport` | bool     | `false`         | When true, uses SignalR as the actual encrypted-message transport: encrypt → send ciphertext via hub → receive echo → decrypt, with `SignalRTransitMicroseconds` measured per message. |
| `includeSignalREcho`  | bool     | `false`         | When true, performs a side-channel SignalR echo per message after in-process cipher measurement. Does not affect cipher timings.                                                       |
| `signalRHubUrl`       | string   | _(appsettings)_ | Override SignalR hub URL.                                                                                                                                                              |
| `tlsVersion`          | enum     | `SystemDefault` | `Tls12`, `Tls13`, or `SystemDefault`.                                                                                                                                                  |
| `notes`               | string   | _(none)_        | Free-text annotation stored on each run header.                                                                                                                                        |

**Example — run AES-256-GCM and ML-KEM-1024 with HMAC-SHA256:**

```
GET /api/encryptionbenchmark/run?algorithms=AES-256-GCM&algorithms=ML-KEM-1024-Hybrid-AES-256-GCM&authId=HMAC-SHA256&messageCount=50
```

### `GET /api/encryptionbenchmark/runs`

Returns all stored run headers ordered by date descending.

### `GET /api/encryptionbenchmark/runs/{id}`

Returns a single run with all per-message results and aggregate statistics.

### `GET /api/encryptionbenchmark/algorithms`

Returns the full algorithm catalogue — all valid cipher IDs and authenticator IDs with their metadata. Use this to discover valid values for the `algorithms` and `authId` parameters.

### `GET /api/encryptionbenchmark/runs/{id}/payloads`

Returns payload metadata for every message in a run. Binary columns (`Ciphertext`, `EncapsulatedKey`, `MacTag`) are excluded from the response for size. Only available for runs executed with `savePayloads=true`.

### `GET /api/encryptionbenchmark/payloads/by-algorithm?algorithmId={id}`

Returns payload metadata across all runs for a specific algorithm, with aggregate size statistics (average plaintext, average wire bytes, average overhead).

### `GET /api/encryptionbenchmark/runs/{id}/storage`

Reports the on-disk `.bin` file sizes for a run, broken down per algorithm directory. Returns count, total bytes, average/min/max file size, and directory path for each algorithm sub-folder.

### `GET /api/encryptionbenchmark/storage/compare?algorithms={id}&algorithms={id}`

Compares on-disk payload sizes across all stored runs for the requested algorithms. Useful for side-by-side wire-overhead comparison between algorithm families.

### `GET /api/encryptionbenchmark/export`

Exports all benchmark results as a downloadable CSV file. UTF-8 with BOM, semicolon-delimited, Swedish locale (`sv-SE`) for decimal formatting.

| Query Parameter | Type | Default | Description                                                                 |
| --------------- | ---- | ------- | --------------------------------------------------------------------------- |
| `detail`        | bool | `false` | `false` — one row per run aggregate; `true` — one row per measured message. |

**Summary mode (`detail=false`)** — filename `benchmark-summary.csv`. One row per run. Columns: run metadata, per-phase avg timing (µs), round-trip percentiles (Min/P50/P95/P99/Max), avg wire sizes (bytes), GC totals, correctness rate, and optional resource-monitoring columns (`Res_*`) when a resource capture was active during the run.

**Detail mode (`detail=true`)** — filename `benchmark-message-results.csv`. One row per measured message. Columns: run metadata, per-message timing (all five phases in µs), per-message byte sizes (`PlaintextBytes`, `CiphertextBytes`, `EncapsulatedKeyBytes`, `MacTagBytes`, `TotalWireBytes`, `EncryptionOverheadBytes`), GC allocation bytes, GC gen-0 count, and decrypt/verify success flags.

### `GET /api/encryptionbenchmark/export/totals`

Exports benchmark results as a downloadable CSV file (`benchmark-totals.csv`) that extends the summary format with per-run byte totals and timing totals computed directly from the stored message results. Same encoding and delimiter as the standard export.

One row per run. Includes all columns from the summary export plus:

| Added Column                 | Description                                                            |
| ---------------------------- | ---------------------------------------------------------------------- |
| `PlaintextBytesPerMessage`   | Plaintext size for one representative message in the run (bytes).      |
| `TotalWireBytesPerMessage`   | Wire size for one representative message in the run (bytes).           |
| `TotalPlaintextBytes`        | Sum of `PlaintextBytes` across all measured messages in the run.       |
| `TotalWireBytes`             | Sum of `TotalWireBytes` across all measured messages in the run.       |
| `TotalEncryptMicroseconds`   | Sum of `EncryptMicroseconds` across all measured messages (µs).        |
| `TotalDecryptMicroseconds`   | Sum of `DecryptMicroseconds` across all measured messages (µs).        |
| `TotalSignMicroseconds`      | Sum of `SignMicroseconds` across all measured messages (µs).           |
| `TotalVerifyMicroseconds`    | Sum of `VerifyMicroseconds` across all measured messages (µs).         |
| `TotalRoundTripMicroseconds` | Sum of `TotalRoundTripMicroseconds` across all measured messages (µs). |

The totals are computed by summing the individual `BenchmarkMessageResult` rows rather than multiplying the stored averages, so rounding error does not accumulate.

### `GET /api/encryptionbenchmark/export/combined`

Exports a cross-run aggregate CSV (`benchmark-combined.csv`). Runs are grouped by the combination of `AlgorithmId`, `AuthId`, `TlsVersion`, `MessageSizeBytes`, `WarmupCount`, and `MessageCount`. Each row represents one unique setup and holds the average-of-per-run-averages for all metric columns. Same UTF-8 with BOM, semicolon-delimited, `sv-SE` encoding as the other export endpoints.

One row per unique setup. Columns:

| Column                            | Description                                                                                                                       |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `AlgorithmId`                     | Algorithm identifier string.                                                                                                      |
| `AlgorithmFamily`                 | Algorithm family (from the earliest run in the group).                                                                            |
| `Generation`                      | Algorithm generation field (from the earliest run in the group).                                                                  |
| `AuthId`                          | Authenticator identifier; empty when no authenticator is used.                                                                    |
| `TlsVersion`                      | TLS version constraint used; empty when `SystemDefault`.                                                                          |
| `MessageCount`                    | Number of measured messages per run.                                                                                              |
| `MessageSizeBytes`                | Plaintext message size (bytes).                                                                                                   |
| `WarmupCount`                     | Number of warmup messages per run.                                                                                                |
| `RunCount`                        | Number of runs in this group.                                                                                                     |
| `TotalMessageCount`               | Total measured messages across all runs in the group (`RunCount × MessageCount`).                                                 |
| `FirstRunAt` / `LastRunAt`        | UTC timestamps of the earliest and latest run in the group (ISO 8601).                                                            |
| `AvgEncryptMicroseconds`          | Average of per-run `AvgEncryptMicroseconds` (µs).                                                                                 |
| `AvgDecryptMicroseconds`          | Average of per-run `AvgDecryptMicroseconds` (µs).                                                                                 |
| `AvgSignMicroseconds`             | Average of per-run `AvgSignMicroseconds` (µs). Zero when no authenticator is used.                                                |
| `AvgVerifyMicroseconds`           | Average of per-run `AvgVerifyMicroseconds` (µs). Zero when no authenticator is used.                                              |
| `AvgSignalRTransitMicroseconds`   | Average of per-run `AvgSignalRTransitMicroseconds` (µs). Zero on in-process paths.                                                |
| `AvgTotalRoundTripMicroseconds`   | Average of per-run `AvgTotalRoundTripMicroseconds` (µs).                                                                          |
| `AvgMinRoundTripMicroseconds`     | Average of per-run minimum round-trip (µs).                                                                                       |
| `AvgP50RoundTripMicroseconds`     | Average of per-run P50 round-trip (µs).                                                                                           |
| `AvgP95RoundTripMicroseconds`     | Average of per-run P95 round-trip (µs).                                                                                           |
| `AvgP99RoundTripMicroseconds`     | Average of per-run P99 round-trip (µs).                                                                                           |
| `AvgMaxRoundTripMicroseconds`     | Average of per-run maximum round-trip (µs).                                                                                       |
| `OverallMinRoundTripMicroseconds` | True minimum round-trip seen across all runs in the group (µs).                                                                   |
| `OverallMaxRoundTripMicroseconds` | True maximum round-trip seen across all runs in the group (µs).                                                                   |
| `AvgCiphertextBytes`              | Average of per-run `AvgCiphertextBytes` (bytes).                                                                                  |
| `AvgEncapsulatedKeyBytes`         | Average of per-run `AvgEncapsulatedKeyBytes` (bytes). Zero for non-KEM algorithms.                                                |
| `AvgMacTagBytes`                  | Average of per-run `AvgMacTagBytes` (bytes). Zero when no authenticator is used.                                                  |
| `AvgTotalWireBytes`               | Average of per-run `AvgTotalWireBytes` (bytes).                                                                                   |
| `AvgEncryptionOverheadBytes`      | Average of per-run `AvgEncryptionOverheadBytes` (bytes).                                                                          |
| `AvgTotalGcAllocatedBytes`        | Average of per-run total GC allocated bytes.                                                                                      |
| `AvgGcAllocatedBytesPerMessage`   | Average of per-run per-message GC allocated bytes.                                                                                |
| `AvgTotalGcGen0Collections`       | Average of per-run Gen-0 GC collection count.                                                                                     |
| `AvgSuccessRate`                  | Average of per-run decrypt+verify success rate (0–1).                                                                             |
| `Res_Avg*` / `Res_Overall*`       | Average of per-run resource-monitoring aggregates (CPU %, memory MB, GC deltas, etc.). Empty when no resource capture was active. |
| `Res_RunsWithData`                | Number of runs in the group that have resource-monitoring data.                                                                   |

### `DELETE /api/encryptionbenchmark/data`

Deletes all rows from every benchmark table in FK-safe order: payloads → message results → aggregates → runs. Returns the total number of rows deleted. Irreversible — use only to reset the database during development.

---

## SignalR Integration

There are two distinct ways SignalR is involved in the benchmark, controlled by separate query parameters.

### Mode 1 — Side-channel echo (`includeSignalREcho=true`)

The standard in-process path (`EncryptionBenchmarkDemo`) can optionally attach a SignalR echo call after each measured message. Cipher operations are performed in-process as normal; SignalR latency is measured independently and appended to the response. The echo payload is the ciphertext already encrypted in-process — it is not sent through decrypt on return. `SignalRTransitMicroseconds` is **not** set by this mode.

### Mode 2 — Full transport (`useSignalRTransport=true`)

The `EncryptionBenchmarkSignalRDemo` path makes SignalR the actual encrypted-message transport. The encrypt → network transit → decrypt pipeline is fully serialised per message so `SignalRTransitMicroseconds` and `TotalRoundTripMicroseconds` reflect true end-to-end per-message latency including real network overhead. The hub-echoed bytes are passed directly to `grain.DecryptMessageAsync` — the grain decrypts whatever the network returned.

### `BenchmarkSignalRClient`

`1.Benchmark.Client/Services/BenchmarkSignalRClient.cs`

A lightweight SignalR client with no chat/session logic. Its only hub interaction is calling `BenchmarkEcho` and waiting for the server to reflect the payload back as `BenchmarkEchoResponse`.

`SendEchoAsync(base64Payload)` returns a `SignalRRoundTripResult`:

| Field                   | Description                                                                                                                                                                      |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `RoundTripMicroseconds` | Wall-clock time from `SendAsync` returning to the `BenchmarkEchoResponse` handler firing. Includes SignalR serialisation, HTTP framing, TLS overhead, and server echo execution. |
| `PayloadBytes`          | UTF-8 byte count of the base64 string on the wire — approximates per-message serialised size.                                                                                    |
| `ReceivedPayload`       | The base64 string echoed back by the hub. Decoded by the SignalR-transport slice to obtain the wire bytes passed to `grain.DecryptMessageAsync`.                                 |

The round-trip time does **not** include client-side cipher operations — those are isolated inside the grain calls.

### TLS Version Control

`BenchmarkTlsVersion` enum lets the test constrain which TLS version is negotiated:

| Value           | Behaviour                                                  |
| --------------- | ---------------------------------------------------------- |
| `Tls12`         | Force TLS 1.2 only.                                        |
| `Tls13`         | Force TLS 1.3 only.                                        |
| `SystemDefault` | Let OS/runtime negotiate best available version (default). |

This allows direct comparison of TLS 1.2 vs TLS 1.3 latency overhead without changing the cipher under test.

### `BenchmarkHub`

`0.App.WebApiEncryption/Hubs/BenchmarkHub.cs`

Minimal anonymous SignalR hub. Only one method:

```csharp
public async Task BenchmarkEcho(string payload)
{
    await Clients.Caller.SendAsync("BenchmarkEchoResponse", payload);
}
```

Mapped at `/benchmarkhub`. No authentication required.

---

## Database Schema (`benchmark`)

Four tables in the `benchmark` PostgreSQL schema:

| Table                       | Purpose                                                                                                                                                                                                                                              |
| --------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `BenchmarkRun`              | One row per algorithm per `GET /run` call. Stores algorithm ID, family, generation, message count, payload size, warmup count, TLS version, timestamp, and notes.                                                                                    |
| `BenchmarkMessageResult`    | One row per measured message. Stores all `MetricsSnapshot` fields including `SignalRTransitMicroseconds` (timing in µs, sizes in bytes, GC allocations). `SignalRTransitMicroseconds` is `0` for the standard in-process path. FK to `BenchmarkRun`. |
| `BenchmarkSessionAggregate` | Pre-computed statistics per run: Min/Max/P50/P95/P99 for total round-trip µs; averages for all five timing dimensions including `AvgSignalRTransitMicroseconds`. FK to `BenchmarkRun`.                                                               |
| `BenchmarkMessagePayload`   | Raw ciphertext bytes per message. Only populated when `savePayloads=true`. Stores `Ciphertext`, `EncapsulatedKey` (nullable), `MacTag` (nullable), `PlaintextBytes`, `TotalWireBytes`, on-disk `FilePath`. FK to `BenchmarkRun`.                     |

---

## Configuration

### `appsettings.json`

```jsonc
{
    "Chat": {
        // SignalR hub URL used by BenchmarkSignalRClient when includeSignalREcho=true
        "HubUrl": "https://localhost:7258/benchmarkhub",
    },
    "BenchmarkPayload": {
        // Relative path (from AppContext.BaseDirectory) or absolute path for .bin file storage
        "StorageRoot": "benchmark-payloads",
    },
}
```

### `BenchmarkPayloadOptions`

`0.App.WebApiEncryption/Controllers/BenchmarkPayloadOptions.cs`

Bound from the `BenchmarkPayload` section. `ResolvedStorageRoot` converts a relative path to an absolute path anchored at `AppContext.BaseDirectory`, so the storage location is predictable regardless of working directory.

---

---

## Resource Monitoring

The project includes a live resource monitoring system made up of three parts: a background broadcaster in the API, a dedicated SignalR hub, and a standalone console dashboard application.

### Architecture Overview

```
App.WebApiEncryption (process)
  ├── ResourceMonitorBroadcaster  (BackgroundService)
  │     samples Process metrics every 1 s
  │     → pushes ResourceMetricsSnapshot to MonitorHub
  │
  ├── MonitorHub  (SignalR hub — /hubs/monitor)
  │     push-only, no authentication required
  │     tracks connections via ConnectionTracker
  │
  └── ConnectionTracker  (singleton)
        shared across ChatHub and MonitorHub
        used to report SignalRConnectionCount

EncryptionMonitor (console app — separate process)
  └── connects to /hubs/monitor via SignalR
        receives "Metrics" messages
        renders live Spectre.Console dashboard
```

### `ResourceMonitorBroadcaster`

`0.App.WebApiEncryption/Services/ResourceMonitorBroadcaster.cs`

A `BackgroundService` registered with `builder.Services.AddHostedService<ResourceMonitorBroadcaster>()`. It runs a loop that:

1. Calls `BuildSnapshotAsync()` to collect all metrics from the current process.
2. Pushes the resulting `ResourceMetricsSnapshot` to all connected monitor clients via `_hubContext.Clients.All.SendAsync("Metrics", snapshot)`.
3. Waits `BroadcastIntervalMicroS` (currently **1000 µs**) before repeating.

**What is measured:**

| Metric                    | Source                                                        | Description                                                           |
| ------------------------- | ------------------------------------------------------------- | --------------------------------------------------------------------- |
| `CpuPercent`              | `Process.TotalProcessorTime`                                  | `delta(cpuTime) / (wallClockDelta × coreCount) × 100`. Clamped 0–100. |
| `MemoryUsedMb`            | `Process.WorkingSet64`                                        | Physical RAM pages currently mapped into this process, in MB.         |
| `MemoryLimitMb`           | `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`              | Container or OS memory ceiling visible to this process.               |
| `MemoryPercent`           | Computed                                                      | `MemoryUsedMb / MemoryLimitMb × 100`.                                 |
| `GcHeapMb`                | `GC.GetTotalMemory(false)`                                    | Managed GC heap size (does not force a collection).                   |
| `GcGen0Collections`       | `GC.CollectionCount(0)`                                       | Cumulative Gen-0 collections since process start.                     |
| `GcGen1Collections`       | `GC.CollectionCount(1)`                                       | Cumulative Gen-1 collections since process start.                     |
| `GcGen2Collections`       | `GC.CollectionCount(2)`                                       | Cumulative Gen-2 collections since process start.                     |
| `ThreadPoolWorkerThreads` | `ThreadPool.GetMaxThreads` − `ThreadPool.GetAvailableThreads` | Active thread-pool worker thread count.                               |
| `UptimeSeconds`           | `DateTimeOffset.UtcNow − _startedAt`                          | Seconds since the broadcaster was constructed.                        |
| `SignalRConnectionCount`  | `ConnectionTracker.Count`                                     | Total active SignalR connections across all hubs.                     |
| `OrleansGrainCount`       | `IManagementGrain.GetRuntimeStatistics`                       | Total active Orleans grain activations in the cluster.                |

> All CPU and memory figures are scoped to **this process only**. They reflect what the benchmark server itself is consuming, not system-wide load.

### `ResourceMetricsSnapshot`

`0.App.WebApiEncryption/Models/ResourceMetricsSnapshot.cs`

A C# `record` with all the fields listed in the table above. Serialised as JSON by SignalR and deserialised by `EncryptionMonitor` using an identical copy of the record (property names and order must stay in sync between the two projects).

### `MonitorHub`

`0.App.WebApiEncryption/Hubs/MonitorHub.cs`

A push-only SignalR hub mapped at `/hubs/monitor`. Clients connect and listen; there are no client-to-server methods. The hub is **unauthenticated** so `EncryptionMonitor` can connect without a JWT token. `OnConnectedAsync` / `OnDisconnectedAsync` update the shared `ConnectionTracker` so the broadcaster can include the current connection count in every snapshot.

### `ConnectionTracker`

`0.App.WebApiEncryption/Services/ConnectionTracker.cs`

A thread-safe singleton that tracks the total number of active SignalR connections across all hubs (chat and monitor). `ResourceMonitorBroadcaster` reads `ConnectionTracker.Count` each tick to populate `SignalRConnectionCount` in the snapshot.

### `EncryptionMonitor` Console Application

`0.EncryptionMonitor/`

A standalone .NET console application that connects to the monitor hub and renders a live terminal dashboard using **Spectre.Console**.

**How it starts:**

1. Reads `HubUrl` (hardcoded to `https://localhost:7258/hubs/monitor` — change to point at another host).
2. Builds a `HubConnection` with `WithAutomaticReconnect` (3 s delay) and optional certificate bypass for local dev.
3. Registers `connection.On<ResourceMetricsSnapshot>("Metrics", snapshot => dashboard.Update(snapshot))`.
4. Calls `connection.StartAsync()`. Exits immediately with an error message if the server is unreachable.
5. Starts an `AnsiConsole.Live(...)` render loop that calls `dashboard.Refresh()` every 200 ms.

**Dashboard layout (`MonitorDashboard`):**

`0.EncryptionMonitor/MonitorDashboard.cs`

The live display is split into three horizontal bands:

```
┌─────────────────────────────────────────────────────────┐
│  EncryptionProject — Live Resource Monitor              │
├────────────────────────────┬────────────────────────────┤
│  Process Metrics           │  GC Collection Distribution│
│  ──────────────────────    │  (BreakdownChart)           │
│  Timestamp  / Uptime       │  Gen 0  ████████████ 82%   │
│  CPU        ████  12.4 %   │  Gen 1  ██           9%    │
│  Memory     ████  340 MB   │  Gen 2  █            9%    │
│  GC Heap    28.1 MB        │                            │
│  Gen 0/1/2  counts         │                            │
│  Workers    4              │                            │
│  SignalR    3 connections  │                            │
│  Orleans    7 activations  │                            │
├────────────────────────────┴────────────────────────────┤
│  Connected · last update 14:22:05.337 UTC               │
└─────────────────────────────────────────────────────────┘
```

CPU and memory rows include an inline ASCII bar built proportionally from the 0–100 % range. The GC chart shows each generation's share of total collections as a percentage breakdown.

**Reconnection:** If the server drops, the status line changes to `Reconnecting…` (yellow) or `Connection closed` (red). On reconnect it reverts to `Connected` (green) and resumes receiving snapshots automatically.

---

## Step-by-Step Benchmark Walkthrough

This section traces exactly what happens when a single `GET /api/encryptionbenchmark/run` request is processed, using a concrete example:

```
GET /api/encryptionbenchmark/run
    ?algorithms=AES-256-GCM
    &algorithms=ML-KEM-1024-Hybrid-AES-256-GCM
    &authId=HMAC-SHA256
    &messageCount=5
    &messageSizeBytes=256
    &warmupCount=3
    &includeSignalREcho=true
    &tlsVersion=Tls13
```

### Step 1 — Controller validates the request

`EncryptionBenchmarkController.RunAsync` receives the request. It:

- Looks up each algorithm ID in `AlgorithmCatalog`. Unknown IDs → 400.
- Clamps `messageSizeBytes` to the algorithm's `MaxPayloadBytes` for RSA ciphers (no effect here).
- Resolves the `signalRHubUrl` from the `Chat:HubUrl` appsetting if not overridden.
- Calls into the appropriate slice (`EncryptionBenchmarkDemo` since `savePayloads=false`).

### Step 2 — Slice iterates over algorithms

`EncryptionBenchmarkDemo.RunAsync` loops over `["AES-256-GCM", "ML-KEM-1024-Hybrid-AES-256-GCM"]`. For each algorithm:

#### Step 2a — Orleans grain activation

A new `IBenchmarkSessionGrain` is obtained, keyed by a freshly generated `runId` (GUID string). Because each run uses a unique key, grains from previous runs are never reused.

`grain.ConfigureAsync("AES-256-GCM", "HMAC-SHA256")` is called. Inside the grain:

- `CipherFactory.Create("AES-256-GCM")` instantiates an `AesGcmCipher` and generates a 256-bit random key.
- `AuthenticatorFactory.Create("HMAC-SHA256")` instantiates an `HmacAuthenticator` and generates a 256-bit random HMAC key.
- State is written to in-memory Orleans grain storage.

#### Step 2b — Message generation

The slice generates `messageCount + warmupCount` = **8** random byte arrays of `messageSizeBytes` = **256** bytes each. These are the plaintext payloads that will flow through the cipher.

#### Step 2c — Benchmark execution inside the grain

`grain.RunBenchmarkAsync(messages, warmupCount: 3)` is called. Inside `BenchmarkMetricsCollector.RunBenchmark`:

**Warmup phase (3 iterations, results discarded):**

For each of the 3 warmup rounds, all 8 messages are run through `Measure(cipher, authenticator, plaintext)`. This forces the .NET tiered JIT to compile the cipher and MAC code paths to native steady-state before measurement begins.

**Measurement phase (5 messages, results kept):**

For each of the 5 measured messages, `Measure` executes the following sequence:

| Step | Action                                                       | What is recorded                                                                          |
| ---- | ------------------------------------------------------------ | ----------------------------------------------------------------------------------------- |
| 1    | `gen0Before = GC.CollectionCount(0)`                         | Gen-0 baseline                                                                            |
| 2    | `allocBefore = GC.GetAllocatedBytesForCurrentThread()`       | Heap allocation baseline                                                                  |
| 3    | `t0 = Stopwatch.GetTimestamp()`                              | —                                                                                         |
| 4    | `encrypted = cipher.Encrypt(plaintext)`                      | AES-256-GCM generates a random 12-byte nonce, encrypts plaintext, appends 16-byte GCM tag |
| 5    | `t1 = Stopwatch.GetTimestamp()`                              | `EncryptMicroseconds = (t1−t0) × µs/tick`                                                 |
| 6    | `t2 = Stopwatch.GetTimestamp()`                              | —                                                                                         |
| 7    | `tag = authenticator.Sign(ciphertext)`                       | HMAC-SHA256 signs `ciphertext`. Produces 32-byte tag                                      |
| 8    | `t3 = Stopwatch.GetTimestamp()`                              | `SignMicroseconds = (t3−t2) × µs/tick`                                                    |
| 9    | `allocAfter = GC.GetAllocatedBytesForCurrentThread()`        | `GcAllocatedBytes = allocAfter − allocBefore` (encrypt+sign only)                         |
| 10   | `gen0After = GC.CollectionCount(0)`                          | `GcGen0Collections = gen0After − gen0Before`                                              |
| 11   | `t4 = Stopwatch.GetTimestamp()`                              | —                                                                                         |
| 12   | `recovered = cipher.Decrypt(ciphertext, null)`               | AES-256-GCM verifies GCM tag and decrypts                                                 |
| 13   | `t5 = Stopwatch.GetTimestamp()`                              | `DecryptMicroseconds = (t5−t4) × µs/tick`                                                 |
| 14   | `decryptSuccess = CryptographicEquals(recovered, plaintext)` | Constant-time equality — should always be `true`                                          |
| 15   | `t6 = Stopwatch.GetTimestamp()`                              | —                                                                                         |
| 16   | `authenticator.Verify(ciphertext, tag)`                      | HMAC-SHA256 recomputes and compares — should always be `true`                             |
| 17   | `t7 = Stopwatch.GetTimestamp()`                              | `VerifyMicroseconds = (t7−t6) × µs/tick`                                                  |

A `MetricsSnapshot` is constructed from these values and added to the results list. The grain returns `IReadOnlyList<MetricsSnapshot>` (5 snapshots) to the slice.

After `RunBenchmarkAsync` returns, the grain calls `DeactivateOnIdle()` so cipher key material is released from memory as soon as Orleans garbage-collects the activation.

#### Step 2d — Aggregate computation

The slice computes `BenchmarkSessionAggregateDaM` over the 5 snapshots:

- **Min / Max / P50 / P95 / P99** for each timing dimension (encrypt, decrypt, sign, verify, total round-trip µs).
- Average ciphertext bytes, encapsulated key bytes, MAC tag bytes, wire overhead.
- Total GC-allocated bytes and Gen-0 collections summed across all messages.

#### Step 2e — Database persistence

Three repository calls persist the results:

1. `IBenchmarkRepository.SaveRunAsync(run)` — inserts one `BenchmarkRun` row. Also computes:
    - `RunNumber` — global counter: `MAX(RunNumber) + 1` across all rows (or 1 if the table is empty).
    - `AlgorithmRunNumber` — per-algorithm counter: `MAX(AlgorithmRunNumber) + 1` for rows with the same `AlgorithmId` (or 1 if none exist yet).
2. `IBenchmarkRepository.AddMessageResultsAsync(results)` — bulk-inserts 5 `BenchmarkMessageResult` rows in a single round-trip.
3. `IBenchmarkRepository.SaveAggregateAsync(aggregate)` — inserts one `BenchmarkSessionAggregate` row.

#### Step 2f — SignalR echo (optional, `includeSignalREcho=true`)

After the cipher metrics are collected, a `BenchmarkSignalRClient` connects to `BenchmarkHub` over HTTPS using TLS 1.3 (as requested). For each of the 5 measured messages:

1. The ciphertext bytes are base64-encoded to produce a string identical in format to a real chat message.
2. `_sendTimestamp = Stopwatch.GetTimestamp()` captures the send baseline.
3. `hub.SendAsync("BenchmarkEcho", base64Payload)` sends the payload to the server.
4. The server's `BenchmarkHub.BenchmarkEcho` method immediately calls `Clients.Caller.SendAsync("BenchmarkEchoResponse", payload)`.
5. The `BenchmarkEchoResponse` handler fires on the client. It calls `Stopwatch.GetTimestamp()` as its **first line** to minimise jitter, then resolves `_pendingEcho` with the receive timestamp.
6. `SendEchoAsync` computes `(receiveTimestamp − _sendTimestamp) × µs/tick` and returns a `SignalRRoundTripResult`.

The 5 round-trip values are averaged and included in the response alongside the cipher metrics.

The `BenchmarkSignalRClient` is disposed after the algorithm's echo phase completes; a new client is created for each algorithm to avoid session bleed-over.

### Step 3 — Response

After both algorithms complete, the controller returns a JSON response containing:

- One summary object per algorithm with `runId`, `algorithmId`, `runNumber`, `algorithmRunNumber`, per-message metric arrays, aggregate statistics, and (if SignalR was enabled) average round-trip microseconds.
- HTTP 200 OK.

### How Resource Monitoring Integrates

While the benchmark runs, `ResourceMonitorBroadcaster` continues its sampling loop independently. Any connected `EncryptionMonitor` instance will show CPU spiking during cipher operations, `GcGen0Collections` incrementing as ciphertext buffers are allocated and collected, `ThreadPoolWorkerThreads` rising briefly during the parallel DB writes, and `OrleansGrainCount` increasing by one per active grain and dropping back after `DeactivateOnIdle()` is processed.

---

## Step-by-Step: SignalR-Transport Benchmark Walkthrough

This section traces exactly what happens when a single `GET /api/encryptionbenchmark/run?useSignalRTransport=true` request is processed, using a concrete example:

```
GET /api/encryptionbenchmark/run
    ?algorithms=AES-256-GCM
    &algorithms=ML-KEM-1024-Hybrid-AES-256-GCM
    &authId=HMAC-SHA256
    &messageCount=5
    &messageSizeBytes=256
    &warmupCount=3
    &useSignalRTransport=true
    &tlsVersion=Tls13
```

### Step 1 — Controller validates and routes

`EncryptionBenchmarkController.RunAsync` receives the request. It:

- Validates each algorithm ID against `AlgorithmCatalog`. Unknown IDs → 400.
- Clamps `messageSizeBytes` to cipher `MaxPayloadBytes` for RSA ciphers (no effect here).
- Resolves `hubUrl` from `Chat:HubUrl` in appsettings (or the `signalRHubUrl` query param if provided).
- Detects `UseSignalRTransport=true` and calls `EncryptionBenchmarkSignalRDemo.Execute(...)`, bypassing the standard `EncryptionBenchmarkDemo` path entirely.

### Step 2 — Slice iterates; per-algorithm SignalR connect

`Execute` loops over `["AES-256-GCM", "ML-KEM-1024-Hybrid-AES-256-GCM"]`. For **each algorithm** a fresh `BenchmarkSignalRClient` is created (`await using var signalRClient = new BenchmarkSignalRClient()`) and connected before the grain is configured. It is disposed automatically at the end of that algorithm's loop body. This ensures no connection state, pending callbacks, or buffered messages from one algorithm can influence the next.

`ConnectAsync(hubUrl, BenchmarkTlsVersion.Tls13, bypassCertificateValidation: true)` works as follows:

1. A `SocketsHttpHandler` is built with `SslOptions.EnabledSslProtocols = SslProtocols.Tls13`.
2. The `HubConnectionBuilder` wires `HttpMessageHandlerFactory` to that handler.
3. `connection.On<string>("BenchmarkEchoResponse", payload => ...)` registers the receive handler, which calls `Stopwatch.GetTimestamp()` as its first line and then resolves `_pendingEcho.TrySetResult((timestamp, payload))`.
4. `_hubConnection.StartAsync()` performs the WebSocket upgrade over HTTPS/TLS 1.3.

If the connection fails for a given algorithm, that algorithm is added to the `errors` list and the slice continues to the next one — the request is not aborted.

### Step 3 — Grain + measurement loop (per algorithm)

For each of `["AES-256-GCM", "ML-KEM-1024-Hybrid-AES-256-GCM"]`:

#### Step 3a — Orleans grain activation

A new `IBenchmarkSessionGrain` is obtained, keyed by a freshly generated `runId` GUID.

`grain.ConfigureAsync("AES-256-GCM", "HMAC-SHA256")` runs inside the grain:

- `CipherFactory.Create("AES-256-GCM")` instantiates an `AesGcmCipher` and generates a 256-bit random key.
- `AuthenticatorFactory.Create("HMAC-SHA256")` instantiates an `HmacAuthenticator` and generates a 256-bit random HMAC key.
- Keys are held as non-serialisable fields on the grain — they never leave the silo process.

#### Step 3b — Message generation

The slice generates `messageCount + warmupCount` = **8** random byte arrays of 256 bytes each using `RandomNumberGenerator.Fill`.

#### Step 3c — Warmup phase (cipher JIT, no SignalR)

For each of the 3 warmup messages:

1. `grain.EncryptMessageAsync(plaintext)` — exercises the cipher + HMAC-SHA256 code paths.
2. `grain.DecryptMessageAsync(warmupResult.WirePayload)` — exercises decrypt + verify.

Results are discarded. This warms up the .NET tiered JIT so cipher code is compiled to native steady-state before measurement begins.

#### Step 3d — Measured loop (5 messages)

For each of the 5 measured messages, the following sequence runs fully serialised (one message at a time):

| Step | Actor          | Action                                                                                                                                                                                               | Timing recorded                                                            |
| ---- | -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| 1    | Grain          | `EncryptMessageAsync(plaintext)` — `AesGcmCipher.Encrypt` generates a random 12-byte nonce, encrypts, appends 16-byte GCM tag; then `HmacAuthenticator.Sign(ciphertext)` produces a 32-byte HMAC tag | `EncryptMicroseconds`, `SignMicroseconds`                                  |
| 2    | Grain          | `BenchmarkWireFormat.Encode(ciphertext, null, hmacTag)` packs the segments into a single byte array                                                                                                  | (inside grain call)                                                        |
| 3    | Slice          | `Convert.ToBase64String(wirePayload)` — produces the SignalR-safe string                                                                                                                             | —                                                                          |
| 4    | SignalR client | `_sendTimestamp = Stopwatch.GetTimestamp()` then `hub.SendAsync("BenchmarkEcho", base64)`                                                                                                            | —                                                                          |
| 5    | Hub            | `BenchmarkHub.BenchmarkEcho` fires: `Clients.Caller.SendAsync("BenchmarkEchoResponse", payload)`                                                                                                     | —                                                                          |
| 6    | SignalR client | `BenchmarkEchoResponse` handler: `Stopwatch.GetTimestamp()` → `_pendingEcho.TrySetResult((ts, payload))`                                                                                             | `SignalRTransitMicroseconds = (t_recv − t_send) × µs/tick`                 |
| 7    | Slice          | `Convert.FromBase64String(signalRResult.ReceivedPayload)` — identical bytes to what was sent for a transparent echo                                                                                  | —                                                                          |
| 8    | Grain          | `BenchmarkWireFormat.Decode(receivedWireBytes)` → `AesGcmCipher.Decrypt` verifies GCM tag and decrypts → `HmacAuthenticator.Verify(ciphertext, hmacTag)`                                             | `DecryptMicroseconds`, `VerifyMicroseconds`                                |
| 9    | Slice          | `MetricsSnapshot` built with all five timing fields                                                                                                                                                  | `TotalRoundTripMicroseconds = encrypt + sign + signalR + decrypt + verify` |

> **What `SignalRTransitMicroseconds` captures:** SignalR serialisation on the sender side, HTTP/WebSocket framing, TLS 1.3 record overhead in both directions, server-side `BenchmarkEcho` execution (a single `SendAsync` call), and SignalR deserialisation on the receive side. It does **not** include the grain cipher operations — those are isolated to steps 1 and 8 above.

#### Step 3e — Aggregate computation

The 5 `MetricsSnapshot` values are used to compute `BenchmarkSessionAggregateDaM`:

- **Min / Max / P50 / P95 / P99** for `TotalRoundTripMicroseconds`.
- `AvgEncryptMicroseconds`, `AvgDecryptMicroseconds`, `AvgSignMicroseconds`, `AvgVerifyMicroseconds`, `AvgSignalRTransitMicroseconds`.
- Total GC-allocated bytes and Gen-0 collections (encrypt + sign phase only).
- `SuccessRate` — fraction of messages where both `DecryptSuccess` and `VerifySuccess` are `true`.

#### Step 3f — Grain cleanup

`grain.CompleteSessionAsync()` calls `DeactivateOnIdle()`, scheduling the activation for garbage collection. AES-GCM and HMAC key material is released from memory.

#### Step 3g — Database persistence

Three repository calls (same pattern as the standard path):

1. `SaveRunAsync(run)` — inserts a `BenchmarkRun` row. The `Notes` field contains `"SignalR-transport TLS=Tls13"` appended automatically by the slice.
2. `AddMessageResultsAsync(results)` — bulk-inserts 5 `BenchmarkMessageResult` rows, each with its individual `SignalRTransitMicroseconds` value.
3. `SaveAggregateAsync(aggregate)` — inserts one `BenchmarkSessionAggregate` row including `AvgSignalRTransitMicroseconds`.

### Step 4 — Response

After both algorithms complete, the controller returns HTTP 200 with JSON:

```json
{
    "message": "SignalR-transport benchmark complete",
    "algorithmsRun": 2,
    "algorithmsFailed": 0,
    "signalRHubUrl": "https://localhost:7258/benchmarkhub",
    "tlsVersion": "Tls13",
    "runs": [
        {
            "runId": "...",
            "algorithmId": "AES-256-GCM",
            "avgEncryptMicroseconds": 4.2,
            "avgDecryptMicroseconds": 3.8,
            "avgSignMicroseconds": 1.1,
            "avgVerifyMicroseconds": 1.0,
            "avgSignalRTransitMicroseconds": 312.5,
            "avgRoundTripMicroseconds": 322.6,
            "p95RoundTripMicroseconds": 380.1,
            "successRate": 1.0
        },
        { "...": "ML-KEM-1024-Hybrid-AES-256-GCM results" }
    ],
    "errors": []
}
```

### Comparison: Standard vs. SignalR-Transport paths

| Aspect                       | Standard (`includeSignalREcho=true`)            | SignalR Transport (`useSignalRTransport=true`)                             |
| ---------------------------- | ----------------------------------------------- | -------------------------------------------------------------------------- |
| Cipher operations            | In-process, batched via `RunBenchmarkAsync`     | One grain call per message (`EncryptMessageAsync` / `DecryptMessageAsync`) |
| Network transit              | Side-channel after cipher measurement           | Main loop — decrypt receives the hub-echoed bytes                          |
| `SignalRTransitMicroseconds` | Not set (always 0)                              | Measured per message                                                       |
| `TotalRoundTripMicroseconds` | Cipher phases only                              | All five phases including network transit                                  |
| Grain interface              | `RunBenchmarkAsync` (bulk)                      | `EncryptMessageAsync` + `DecryptMessageAsync` (per-message)                |
| Grain cleanup                | `DeactivateOnIdle()` inside `RunBenchmarkAsync` | `CompleteSessionAsync()` called by slice after loop                        |
| SignalR client lifecycle     | Single client, shared across all algorithms     | New `BenchmarkSignalRClient` per algorithm — disposed between algorithms   |
| Resource monitoring          | Supported                                       | Supported                                                                  |

---

## Project Structure Reference

| Project                      | Role                                                                                                                                                                        |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `0.App.WebApiEncryption`     | ASP.NET Core host, controllers, hubs, DI wiring.                                                                                                                            |
| `0.EncryptionMonitor`        | Standalone console dashboard. Connects to MonitorHub and renders live process metrics.                                                                                      |
| `0.Slices`                   | Use-case orchestration slices (`EncryptionBenchmarkDemo`, `EncryptionBenchmarkPayloadDemo`, `EncryptionBenchmarkSignalRDemo`).                                              |
| `1.Benchmark.Client`         | `BenchmarkSignalRClient`, `ChatSettings`.                                                                                                                                   |
| `1.Benchmark.Grains`         | Orleans grain interfaces + implementations (`BenchmarkSessionGrain`, `BenchmarkPayloadGrain`).                                                                              |
| `1.Domain.Services`          | Algorithm catalog, cipher/authenticator implementations, `BenchmarkMetricsCollector`, `MetricsSnapshot`, `BenchmarkWireFormat`, `EncryptPhaseResult`, `DecryptPhaseResult`. |
| `2.Domain.Models`            | (Not used by benchmark — kept for future use.)                                                                                                                              |
| `3a.DataAccess.Repositories` | Repository interfaces + EF Core implementations for the `benchmark` schema.                                                                                                 |
| `3b.DataAccess.DbContext`    | `MainDbContext`, EF migrations, PostgreSQL connection factory.                                                                                                              |
| `3c.DataAccess.Models`       | EF Core entity models (`BenchmarkRunDaM`, `BenchmarkMessageResultDaM`, etc.).                                                                                               |
| `4a.CrossCut.Concerns`       | Shared converters, extensions, logging utilities.                                                                                                                           |
| `4b.CrossCut.Secrets`        | Connection string and secrets loading (`AddSecrets()`).                                                                                                                     |
