namespace DocumentManagement.Api.Models;

/// <summary>
/// Stores metadata for a document whose content is kept on the local filesystem.
/// </summary>
public sealed class Document
{
    public Guid Id { get; set; }

    public required string OriginalFileName { get; set; }

    public required string StoredFileName { get; set; }

    public required string ContentType { get; set; }

    public long FileSize { get; set; }

    public DateTime UploadedAt { get; set; }
}
