namespace DocumentManagement.Api.DTOs;


/// Public document metadata returned by the API.

public sealed record DocumentResponseDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    DateTime UploadedAt);
