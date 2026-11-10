namespace DocumentManagement.Api.DTOs;


/// Metadata returned after a document is uploaded.

public sealed record DocumentUploadResponseDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    DateTime UploadedAt);
