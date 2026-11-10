using DocumentManagement.Api.Data;
using DocumentManagement.Api.DTOs;
using DocumentManagement.Api.Models;
using DocumentManagement.Api.Services.Interfaces;
using DocumentManagement.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocumentManagement.Api.Services;

public sealed class DocumentService(
    ApplicationDbContext dbContext,
    IFileStorage fileStorage,
    IOptions<FileStorageOptions> storageOptions,
    ILogger<DocumentService> logger) : IDocumentService
{
    private const int MaximumOriginalFileNameLength = 255;
    private static readonly IReadOnlyDictionary<string, (string ContentType, byte[] Signature)> SupportedTypes =
        new Dictionary<string, (string, byte[])>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", [0x25, 0x50, 0x44, 0x46, 0x2D]),
            [".png"] = ("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            [".jpg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
            [".jpeg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF])
        };

    public async Task<DocumentUploadResponseDto> UploadDocumentAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw Validation("A file is required.", "file_required");
        }

        if (file.Length <= 0)
        {
            throw Validation("The uploaded file is empty.", "empty_file");
        }

        if (file.Length > storageOptions.Value.MaxFileSizeBytes)
        {
            throw new DocumentOperationException("The file exceeds the 10 MB upload limit.", StatusCodes.Status413PayloadTooLarge, "file_too_large");
        }

        var safeOriginalFileName = ValidateAndSanitizeFileName(file.FileName);
        var extension = Path.GetExtension(safeOriginalFileName);
        if (!SupportedTypes.TryGetValue(extension, out var type))
        {
            throw Validation("Supported file types are PDF, PNG, and JPEG.", "unsupported_file_type");
        }

        if (!string.Equals(file.ContentType, type.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw Validation("The file content type does not match its extension.", "invalid_content_type");
        }

        await using (var input = file.OpenReadStream())
        {
            var header = new byte[type.Signature.Length];
            var read = await input.ReadAsync(header, cancellationToken);
            if (read != header.Length || !header.SequenceEqual(type.Signature))
            {
                throw Validation("The file signature does not match its declared type.", "invalid_file_signature");
            }
        }

        var id = Guid.NewGuid();
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using (var input = file.OpenReadStream())
        {
            try
            {
                await fileStorage.SaveAsync(storedFileName, input, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new DocumentOperationException("The uploaded file could not be stored.", StatusCodes.Status500InternalServerError, "storage_failure", exception);
            }
        }

        var document = new Document
        {
            Id = id,
            OriginalFileName = safeOriginalFileName,
            StoredFileName = storedFileName,
            ContentType = type.ContentType,
            FileSize = file.Length,
            UploadedAt = DateTime.UtcNow
        };

        dbContext.Documents.Add(document);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            try
            {
                await fileStorage.DeleteAsync(storedFileName, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogError(cleanupException, "Failed to remove orphaned stored document {StoredFileName} after database persistence failed.", storedFileName);
            }

            dbContext.Entry(document).State = EntityState.Detached;
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogError(exception, "Failed to persist metadata for uploaded document {DocumentId}.", id);
            throw new DocumentOperationException("The document could not be saved.", StatusCodes.Status500InternalServerError, "persistence_failure", exception);
        }

        var metadata = ToResponse(document);
        return new DocumentUploadResponseDto(metadata.Id, metadata.OriginalFileName, metadata.ContentType, metadata.FileSize, metadata.UploadedAt);
    }

    public async Task<IReadOnlyList<DocumentResponseDto>> GetAllDocumentsAsync(CancellationToken cancellationToken)
    {
        var documents = await dbContext.Documents.AsNoTracking().OrderByDescending(document => document.UploadedAt)
            .ToListAsync(cancellationToken);
        return documents.Select(ToResponse).ToList();
    }

    public async Task<DocumentResponseDto?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await dbContext.Documents.AsNoTracking().SingleOrDefaultAsync(document => document.Id == id, cancellationToken);
        return document is null ? null : ToResponse(document);
    }

    public async Task<DocumentDownloadResult?> DownloadDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await dbContext.Documents.AsNoTracking().SingleOrDefaultAsync(document => document.Id == id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var content = await fileStorage.OpenReadAsync(document.StoredFileName, cancellationToken);
        if (content is null)
        {
            logger.LogWarning("Document metadata {DocumentId} exists, but stored file {StoredFileName} is missing.", id, document.StoredFileName);
            return null;
        }

        return new DocumentDownloadResult(ToResponse(document), content);
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await dbContext.Documents.SingleOrDefaultAsync(document => document.Id == id, cancellationToken);
        if (document is null)
        {
            return false;
        }

        try
        {
            var existed = await fileStorage.ExistsAsync(document.StoredFileName, cancellationToken);
            await fileStorage.DeleteAsync(document.StoredFileName, cancellationToken);
            if (!existed)
            {
                logger.LogWarning("Document metadata {DocumentId} points to missing stored file {StoredFileName}; deleting metadata.", id, document.StoredFileName);
            }
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Could not delete stored file for document {DocumentId}.", id);
            throw new DocumentOperationException("The document file could not be deleted.", StatusCodes.Status500InternalServerError, "storage_failure", exception);
        }

        dbContext.Documents.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string ValidateAndSanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw Validation("A valid filename is required.", "invalid_filename");
        }

        var safeName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > MaximumOriginalFileNameLength || safeName is "." or "..")
        {
            throw Validation("The filename is invalid or too long.", "invalid_filename");
        }

        return safeName;
    }

    private static DocumentOperationException Validation(string message, string errorCode) =>
        new(message, StatusCodes.Status400BadRequest, errorCode);

    private static DocumentResponseDto ToResponse(Document document) =>
        new(document.Id, document.OriginalFileName, document.ContentType, document.FileSize, document.UploadedAt);
}
