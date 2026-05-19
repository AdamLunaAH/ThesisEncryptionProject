using Domain.Services.Encryptions.Benchmark.Authenticators;

namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Creates <see cref="IMessageAuthenticator"/> instances from a canonical auth ID string.
///
/// Each authenticator generates its own key on construction so the key-generation
/// cost can be measured separately from the per-message sign/verify latency.
///
/// Unlike ciphers, authenticators hold only a byte-array key — no native handles —
/// so they do not implement <see cref="IDisposable"/>.
/// </summary>
public static class AuthenticatorFactory
{
    /// <summary>
    /// Creates a new keyed authenticator for the given <paramref name="authId"/>.
    /// </summary>
    /// <param name="authId">
    /// One of the string constants defined in <see cref="AuthIds"/>,
    /// e.g. <c>"HMAC-SHA256"</c> or <c>"KMAC-256"</c>.
    /// </param>
    /// <returns>A freshly keyed <see cref="IMessageAuthenticator"/>.</returns>
    /// <exception cref="ArgumentException">Unknown auth ID.</exception>
    public static IMessageAuthenticator Create(string authId) => authId switch
    {
        // ── SHA-2 HMAC – Legacy ───────────────────────────────────────────────
        AuthIds.HmacSha256 => new HmacSha256Authenticator(),
        AuthIds.HmacSha512 => new HmacSha512Authenticator(),

        // ── SHA-3 HMAC – Modern ───────────────────────────────────────────────
        AuthIds.HmacSha3_256 => new HmacSha3_256Authenticator(),
        AuthIds.HmacSha3_512 => new HmacSha3_512Authenticator(),

        // ── KMAC – Modern ─────────────────────────────────────────────────────
        AuthIds.Kmac128 => new Kmac128Authenticator(),
        AuthIds.Kmac256 => new Kmac256Authenticator(),

        _ => throw new ArgumentException(
            $"Unknown authenticator '{authId}'. " +
            $"Valid values: {string.Join(", ", AlgorithmCatalog.AllAuthIds)}",
            nameof(authId))
    };

    /// <summary>
    /// Creates one fresh authenticator instance for every auth algorithm in the catalogue.
    /// Useful for "compare all MACs" benchmark runs.
    /// </summary>
    public static IReadOnlyList<IMessageAuthenticator> CreateAll()
        => AlgorithmCatalog.AllAuthIds
            .Select(Create)
            .ToList();
}
