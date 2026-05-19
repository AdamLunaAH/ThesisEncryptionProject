namespace App.WebApiEncryption.Controllers;

/// <summary>
/// Configuration options bound from <c>BenchmarkPayload</c> in <c>appsettings.json</c>.
/// </summary>
public class BenchmarkPayloadOptions
{
    public const string SectionName = "BenchmarkPayload";

    /// <summary>
    /// Root directory where benchmark payload files are written.
    /// May be an absolute path or a path relative to the application base directory.
    /// Default: <c>benchmark-payloads</c> (a folder in <see cref="AppContext.BaseDirectory"/>).
    /// </summary>
    public string StorageRoot { get; set; } = "benchmark-payloads";

    /// <summary>
    /// Returns the absolute path of <see cref="StorageRoot"/>, resolving
    /// relative paths against <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string ResolvedStorageRoot =>
        Path.IsPathRooted(StorageRoot)
            ? StorageRoot
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, StorageRoot));
}
