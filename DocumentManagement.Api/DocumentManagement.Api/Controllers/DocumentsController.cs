using DocumentManagement.Api.DTOs;
using DocumentManagement.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocumentManagement.Api.Controllers;


[ApiController]
[Route("api/documents")]
[Produces("application/json")]
public sealed class DocumentsController(IDocumentService documentService) : ControllerBase
{
    // Uploads a supported document file
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(DocumentUploadResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DocumentUploadResponseDto>> Upload(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var result = await documentService.UploadDocumentAsync(file, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    // Returns metadata for all stored documents
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentResponseDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await documentService.GetAllDocumentsAsync(cancellationToken));

    // Returns metadata for one document
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentResponseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var document = await documentService.GetDocumentByIdAsync(id, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    // Downloads the physical content of one document.
    [HttpGet("{id:guid}/download")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var download = await documentService.DownloadDocumentAsync(id, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, download.Metadata.ContentType, download.Metadata.OriginalFileName, enableRangeProcessing: true);
    }

    // Deletes one document and its stored file
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await documentService.DeleteDocumentAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
