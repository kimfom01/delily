using System.ComponentModel.DataAnnotations;

namespace Delily.Storage;

/// <summary>
/// Storage configuration. The blob path is intentionally the only required
/// value at scaffold time; the database connection string lives in
/// <c>ConnectionStrings:Delily</c> so it can be supplied as a secret.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Delily:Storage";

    [Required]
    public string BlobPath { get; init; } = "data/blobs";

    [Range(1024, 1073741824)]
    public int MaxBlobSizeBytes { get; init; } = 50 * 1024 * 1024;
}
