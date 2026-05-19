# Key Management — Documentation

This document explains how cryptographic key material is created, used, and destroyed across all cipher and authenticator implementations in the encryption benchmark system.

---

## Overview

Every benchmark run generates its own isolated key material. Keys are created inside an Orleans grain at session configuration time and held as in-memory-only fields for the duration of the run. Key material never leaves the grain process: no keys are serialised, returned in API responses, or written to the database. The only bytes that travel over the wire are ciphertext and any encapsulated key material that the receiving side needs to decrypt (described per cipher below).

---

## Key Creation

### Where keys are created

`CipherFactory.Create(algorithmId)` and `AuthenticatorFactory.Create(authId)` are called from `BenchmarkSessionGrain.ConfigureAsync` (or `BenchmarkPayloadGrain.ConfigureAsync`). Each factory call instantiates a new cipher or authenticator and the constructor generates fresh key material immediately.

```
ConfigureAsync(algorithmId, authId)
    └── BuildCipherAndAuthenticator(algorithmId, authId)
            ├── CipherFactory.Create(algorithmId)   → new cipher + key generation
            └── AuthenticatorFactory.Create(authId) → new authenticator + key generation
```

Key generation is a one-time cost per run, not per message. For slow generators like RSA (which can take ~500 ms for a 4096-bit key), this cost is paid during `ConfigureAsync` and is separate from the per-message timing captured by the benchmark.

### Per-cipher key generation

| Cipher family                                                | Key generated at                                           | Key type                                                                      | Reused across messages?                                                        |
| ------------------------------------------------------------ | ---------------------------------------------------------- | ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| Symmetric (AES-CBC, AES-GCM, ChaCha20-Poly1305, Twofish-CBC) | Construction                                               | Random symmetric key via `RandomNumberGenerator.GetBytes(keyBytes)`           | Yes — same key, fresh nonce/IV per message                                     |
| Non-hybrid RSA                                               | Construction                                               | RSA key pair via `RSA.Create(keySizeBits)`                                    | Yes — same key pair for all messages                                           |
| RSA Hybrid                                                   | Construction                                               | RSA key pair via `RSA.Create(keySizeBits)`                                    | RSA key: yes. AES session key: no — fresh per message                          |
| ECDH Hybrid                                                  | Construction (static pair) + Encrypt call (ephemeral pair) | Static ECDH key pair (server side); fresh ephemeral ECDH key pair per message | Static pair: yes. Ephemeral pair: no                                           |
| ML-KEM Hybrid                                                | Construction                                               | ML-KEM key pair via `MLKem.GenerateKey(algorithm)`                            | Key pair: yes. Shared secret and derived AES key: no — fresh per encapsulation |

#### Symmetric ciphers

```csharp
// AesGcmCipher constructor — called once per run
_key = RandomNumberGenerator.GetBytes(keySizeBits / 8);  // 16 or 32 bytes

// Encrypt — called per message
byte[] nonce = RandomNumberGenerator.GetBytes(12);  // fresh 96-bit nonce
// nonce framed into ciphertext: [nonce | GCM tag | ciphertext]
```

The symmetric key is fixed for the life of the run. The nonce is always fresh and random, embedded in the ciphertext bytes so the receiver can extract it without any extra channel.

#### Non-hybrid RSA

```csharp
// RsaOaepCipher constructor — called once per run
_rsa = RSA.Create(keySizeBits);  // 2048 or 4096

// Encrypt — called per message
byte[] ciphertext = _rsa.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256);
```

The same RSA key pair encrypts every message in the run. There is no encapsulated key in the wire payload — the receiver decrypts using the same `_rsa` private key held in the grain.

#### RSA Hybrid

```csharp
// Constructor — RSA key pair, once per run
_rsa = RSA.Create(keySizeBits);

// Encrypt — per message
byte[] sessionKey = RandomNumberGenerator.GetBytes(32);         // fresh AES-256 key
byte[] encapsulatedKey = _rsa.Encrypt(sessionKey, OaepSHA256); // RSA-wrapped session key
byte[] ciphertext = HybridAesGcmHelper.Encrypt(plaintext, sessionKey);
// Returns CipherResult(ciphertext, encapsulatedKey)
```

Each message is encrypted with a unique AES-256-GCM session key. Compromising one session key does not expose others — this gives per-message forward secrecy. The static RSA key pair only ever wraps these session keys; it never encrypts plaintext directly.

#### ECDH Hybrid

```csharp
// Constructor — static "server" key pair, once per run
_serverKey = ECDiffieHellman.Create(_curve);  // P-256 or P-384

// Encrypt — per message
using var ephemeral = ECDiffieHellman.Create(_curve);  // fresh ephemeral pair
byte[] ephemeralPublicKeyBytes = ephemeral.PublicKey.ExportSubjectPublicKeyInfo();
byte[] rawSecret = ephemeral.DeriveRawSecretAgreement(_serverKey.PublicKey);
byte[] aesKey = HKDF.DeriveKey(SHA256, rawSecret, 32, info: "benchmark-ecdh-v1");
byte[] ciphertext = HybridAesGcmHelper.Encrypt(plaintext, aesKey);
// Returns CipherResult(ciphertext, encapsulatedKey: ephemeralPublicKeyBytes)

// Decrypt — per message
// Grain imports ephemeral public key from encapsulatedKey, recomputes shared secret
byte[] rawSecret = _serverKey.DeriveRawSecretAgreement(ephemeralPub.PublicKey);
byte[] aesKey = HKDF.DeriveKey(SHA256, rawSecret, 32, info: "benchmark-ecdh-v1");
```

The static server private key never leaves the grain. Per-message forward secrecy comes from the ephemeral pair — each message is encrypted to a distinct ECDH-derived AES key. The ephemeral public key travels as `encapsulatedKey` in the wire payload so the grain can reproduce the shared secret on the decrypt side.

HKDF is used rather than the raw shared point coordinate to prevent related-key attacks.

#### ML-KEM Hybrid

```csharp
// Constructor — ML-KEM key pair, once per run
_key = MLKem.GenerateKey(algorithm);  // ML-KEM-768 or ML-KEM-1024

// Encrypt — per message (encapsulation)
byte[] kemCiphertext = new byte[algorithm.CiphertextSizeInBytes];  // 1088 or 1568 bytes
byte[] sharedSecret  = new byte[algorithm.SharedSecretSizeInBytes]; // 32 bytes
_key.Encapsulate(kemCiphertext, sharedSecret);
byte[] aesKey = HKDF.DeriveKey(SHA256, sharedSecret, 32, info: "benchmark-mlkem-v1");
byte[] ciphertext = HybridAesGcmHelper.Encrypt(plaintext, aesKey);
// Returns CipherResult(ciphertext, encapsulatedKey: kemCiphertext)

// Decrypt — per message (decapsulation)
byte[] sharedSecret = new byte[algorithm.SharedSecretSizeInBytes];
_key.Decapsulate(encapsulatedKey, sharedSecret);  // recovers same 32-byte secret
byte[] aesKey = HKDF.DeriveKey(SHA256, sharedSecret, 32, info: "benchmark-mlkem-v1");
```

ML-KEM provides quantum-safe key encapsulation (FIPS 203). The ML-KEM private key stays in the grain for the entire run. Each message triggers a fresh `Encapsulate` call which produces a new KEM ciphertext and a unique 32-byte shared secret, providing per-message forward secrecy. The KEM ciphertext is the `encapsulatedKey` in the wire payload.

---

## MAC Authenticator Keys

All authenticators generate a random key at construction time and reuse it for every message in the run.

```csharp
// HmacSha256Authenticator constructor — called once per run
_key = RandomNumberGenerator.GetBytes(32);  // 256-bit HMAC key

// Sign (per message) — encrypt-then-MAC
byte[] tag = HMACSHA256.HashData(_key, ciphertext);

// Verify (per message) — constant-time comparison
byte[] expected = HMACSHA256.HashData(_key, ciphertext);
return CryptographicOperations.FixedTimeEquals(expected, tag);
```

The MAC covers the ciphertext bytes. For hybrid ciphers, the encapsulated key material is also included:

```csharp
// In BenchmarkSessionGrain.EncryptMessageAsync
byte[] dataToSign = encrypted.EncapsulatedKey is null
    ? encrypted.Ciphertext
    : Combine(encrypted.Ciphertext, encrypted.EncapsulatedKey);
tag = _authenticator.Sign(dataToSign);
```

This means the MAC protects both the encrypted payload and the key exchange material, preventing an attacker from substituting a different encapsulated key without detection.

---

## Key Containment in Orleans Grains

Cipher and authenticator instances are held as **non-serialisable private fields** on the grain:

```csharp
// BenchmarkSessionGrain — non-serialisable, in-memory only
private IMessageCipher? _cipher;
private IMessageAuthenticator? _authenticator;
```

The grain's **persisted state** contains only the algorithm ID strings (`AlgorithmId`, `AuthId`, `IsConfigured`). Key material itself is never written to Orleans storage.

### Grain reactivation

If the Orleans silo restarts and reactivates a grain from persisted state, `OnActivateAsync` calls `BuildCipherAndAuthenticator(State.AlgorithmId, State.AuthId)` which regenerates entirely **new** key material from the stored algorithm IDs. This means a reactivated grain cannot decrypt ciphertexts produced by the previous activation — the keys are incompatible. In practice this is not a problem because each run is self-contained: encrypt and decrypt always happen within the same grain activation.

---

## Per-Run Isolation

Each benchmark run gets its own grain keyed by a freshly generated GUID:

```csharp
var runId = Guid.NewGuid();
var grain = grainFactory.GetGrain<IBenchmarkSessionGrain>(runId.ToString());
await grain.ConfigureAsync(algorithmId, authId);
```

- Two concurrent `/run` requests never share key material, even for the same algorithm.
- A multi-algorithm request (e.g. `algorithms=AES-256-GCM&algorithms=ML-KEM-1024`) creates one grain and one key set per algorithm.
- Warmup messages and measured messages use the same grain and therefore the same keys.

---

## What Travels Over the Wire

The wire payload is produced by `BenchmarkWireFormat.Encode` and carries only the data the receiver needs to decrypt. The private key material always stays inside the grain.

```
Wire format: [4B ciphertextLen][ciphertext][4B encKeyLen][encKey][4B tagLen][tag]
```

| Cipher type                         | `ciphertext` contents                           | `encKey` contents                                                     | `tag` contents               |
| ----------------------------------- | ----------------------------------------------- | --------------------------------------------------------------------- | ---------------------------- |
| Symmetric (AES-GCM, ChaCha20, etc.) | `[nonce \| GCM/Poly1305 tag \| encrypted data]` | Empty (0 bytes)                                                       | HMAC/KMAC tag if MAC enabled |
| Non-hybrid RSA                      | RSA-OAEP ciphertext                             | Empty (0 bytes)                                                       | HMAC/KMAC tag if MAC enabled |
| RSA Hybrid                          | AES-256-GCM ciphertext                          | RSA-OAEP-wrapped AES session key                                      | HMAC/KMAC tag if MAC enabled |
| ECDH Hybrid                         | AES-256-GCM ciphertext                          | Ephemeral ECDH public key (SubjectPublicKeyInfo DER, 91 or 120 bytes) | HMAC/KMAC tag if MAC enabled |
| ML-KEM Hybrid                       | AES-256-GCM ciphertext                          | ML-KEM KEM ciphertext (1088 or 1568 bytes)                            | HMAC/KMAC tag if MAC enabled |

The `encKey` field is what the `EncapsulatedKeyBytes` metric in benchmark results refers to. Its size directly determines the wire overhead introduced by each hybrid scheme.

In the SignalR-transport path, the wire payload is base64-encoded before being sent as a string to `BenchmarkHub.BenchmarkEcho` and base64-decoded after the hub echoes it back. The encoding is transparent — the grain decrypts the exact same bytes it encrypted.

---

## Key Lifetime and Cleanup

```
grain.ConfigureAsync()           → key generated, stored in _cipher / _authenticator
  ↓
grain.RunBenchmarkAsync()        → key used for all warmup + measured messages
  or grain.EncryptMessageAsync() ┐
  and grain.DecryptMessageAsync()┘
  ↓
DeactivateOnIdle()               → scheduled immediately after run completes
  ↓
OnDeactivateAsync()              → DisposeCipherAndAuthenticator()
                                    _cipher?.Dispose()   → RSA / ECDH / MLKem handles released
                                    _cipher = null
                                    _authenticator = null → byte[] _key eligible for GC
```

For symmetric ciphers and HMAC authenticators, `Dispose` is a no-op — the key material is a managed `byte[]` that becomes eligible for garbage collection once the field is set to null. For RSA, ECDH, and ML-KEM ciphers, `Dispose` releases the underlying native crypto handles (via `RSA.Dispose()`, `ECDiffieHellman.Dispose()`, `MLKem.Dispose()`).

There is no key rotation within a run. Once the grain deactivates, the keys are gone and cannot be recovered.

---

## Key Size Reference

| Algorithm                         | Static key                    | Per-message key / material                                         |
| --------------------------------- | ----------------------------- | ------------------------------------------------------------------ |
| AES-128-CBC / AES-128-GCM         | 128-bit symmetric key         | 128-bit IV/nonce (embedded)                                        |
| AES-256-CBC / AES-256-GCM         | 256-bit symmetric key         | 96-bit nonce (GCM, embedded)                                       |
| ChaCha20-Poly1305                 | 256-bit symmetric key         | 96-bit nonce (embedded)                                            |
| Twofish-128/192/256-CBC           | 128/192/256-bit symmetric key | 128-bit IV (embedded)                                              |
| RSA-2048 / RSA-4096               | 2048 or 4096-bit RSA key pair | —                                                                  |
| RSA-2048-Hybrid / RSA-4096-Hybrid | 2048 or 4096-bit RSA key pair | 256-bit AES session key (RSA-wrapped in encKey)                    |
| ECDH-P256-Hybrid                  | P-256 static ECDH key pair    | P-256 ephemeral key pair → 256-bit AES key via HKDF                |
| ECDH-P384-Hybrid                  | P-384 static ECDH key pair    | P-384 ephemeral key pair → 256-bit AES key via HKDF                |
| ML-KEM-768-Hybrid                 | ML-KEM-768 key pair           | 32-byte shared secret per encapsulation → 256-bit AES key via HKDF |
| ML-KEM-1024-Hybrid                | ML-KEM-1024 key pair          | 32-byte shared secret per encapsulation → 256-bit AES key via HKDF |
| HMAC-SHA256 / HMAC-SHA3-256       | 256-bit HMAC key              | —                                                                  |
| HMAC-SHA512 / HMAC-SHA3-512       | 512-bit HMAC key              | —                                                                  |
| KMAC-128 / KMAC-256               | 256-bit KMAC key              | —                                                                  |

---

## Project Structure Reference

| Project              | Key management role                                                                                                                                  |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `1.Domain.Services`  | `IMessageCipher`, `IMessageAuthenticator`, all cipher/authenticator implementations, `CipherFactory`, `AuthenticatorFactory`, `AlgorithmCatalog`.    |
| `1.Benchmark.Grains` | `BenchmarkSessionGrain`, `BenchmarkPayloadGrain` — grain lifecycle, key containment, `OnActivateAsync`/`OnDeactivateAsync`, `BenchmarkSessionState`. |
| `0.Slices`           | Orchestrates grain configuration and run sequencing; generates plaintext messages; never touches key material directly.                              |
| `1.Benchmark.Client` | `BenchmarkSignalRClient` — handles wire payload transport only; no knowledge of key material.                                                        |
