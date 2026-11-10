using DocumentManagement.Api.DTOs;
using Microsoft.AspNetCore.Http;

namespace DocumentManagement.Api.Services.Interfaces;

public sealed record DocumentDownloadResult(DocumentResponseDto Metadata, Stream Content);

public interface IDocumentService
{
    Task<DocumentUploadResponseDto> UploadDocumentAsync(IFormFile? file, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentResponseDto>> GetAllDocumentsAsync(CancellationToken cancellationToken);
    Task<DocumentResponseDto?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<DocumentDownloadResult?> DownloadDocumentAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken);
}
