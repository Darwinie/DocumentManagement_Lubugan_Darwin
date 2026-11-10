using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DocumentManagement.Api.Data;
using DocumentManagement.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentManagement.Tests.Integration;

public sealed class DocumentsApiTests
{
    private static readonly string PdfContent = "%PDF-1.7\n";
    private static readonly byte[] PdfSignature = Encoding.ASCII.GetBytes("%PDF-");

    [Fact]
    public async Task Upload_valid_pdf_returns_created_and_persists_metadata_and_file()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);

        using var response = await UploadPdfAsync(client, "assessment.pdf");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var uploaded = await ReadJsonAsync<DocumentUploadResponseDto>(response);
        Assert.Equal("assessment.pdf", uploaded.OriginalFileName);
        Assert.Equal("application/pdf", uploaded.ContentType);
        Assert.Equal(Encoding.ASCII.GetByteCount(PdfContent), uploaded.FileSize);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = await database.Documents.SingleAsync(document => document.Id == uploaded.Id);
        Assert.Equal(uploaded.OriginalFileName, record.OriginalFileName);
        Assert.True(File.Exists(Path.Combine(factory.StoragePath, record.StoredFileName)));
    }

    [Fact]
    public async Task Get_by_id_existing_document_returns_ok_and_metadata()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        using var uploadResponse = await UploadPdfAsync(client, "existing.pdf");
        var uploaded = await ReadJsonAsync<DocumentUploadResponseDto>(uploadResponse);

        using var response = await client.GetAsync($"/api/documents/{uploaded.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await ReadJsonAsync<DocumentResponseDto>(response);
        Assert.Equal(uploaded.Id, details.Id);
        Assert.Equal("existing.pdf", details.OriginalFileName);
        Assert.Equal("application/pdf", details.ContentType);
        Assert.Equal(Encoding.ASCII.GetByteCount(PdfContent), details.FileSize);
    }

    [Fact]
    public async Task Get_all_documents_returns_uploaded_metadata()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        using var uploadResponse = await UploadPdfAsync(client, "listed.pdf");
        var uploaded = await ReadJsonAsync<DocumentUploadResponseDto>(uploadResponse);

        using var response = await client.GetAsync("/api/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documents = await ReadJsonAsync<List<DocumentResponseDto>>(response);
        var listed = Assert.Single(documents);
        Assert.Equal(uploaded.Id, listed.Id);
        Assert.Equal("listed.pdf", listed.OriginalFileName);
    }

    [Fact]
    public async Task Get_by_id_nonexistent_document_returns_not_found()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);

        using var response = await client.GetAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_file_over_10_mib_returns_payload_too_large()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        var bytes = new byte[(10 * 1024 * 1024) + 1];
        PdfSignature.CopyTo(bytes, 0);
        using var content = CreateFileContent("large.pdf", "application/pdf", bytes);

        using var response = await client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Upload_empty_file_returns_bad_request()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        using var content = CreateFileContent("empty.pdf", "application/pdf", []);

        using var response = await client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_unsupported_extension_returns_bad_request()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        using var content = CreateFileContent("notes.txt", "text/plain", Encoding.UTF8.GetBytes("plain text"));

        using var response = await client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_without_file_returns_bad_request()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        using var content = new MultipartFormDataContent();

        using var response = await client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Download_existing_document_returns_file_with_original_name()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        using var uploadResponse = await UploadPdfAsync(client, "download.pdf");
        var uploaded = await ReadJsonAsync<DocumentUploadResponseDto>(uploadResponse);

        using var response = await client.GetAsync($"/api/documents/{uploaded.Id}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("download.pdf", response.Content.Headers.ContentDisposition?.FileName ?? string.Empty);
        Assert.Equal(PdfContent, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Download_after_deletion_returns_not_found()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        using var uploadResponse = await UploadPdfAsync(client, "deleted.pdf");
        var uploaded = await ReadJsonAsync<DocumentUploadResponseDto>(uploadResponse);
        using var deleteResponse = await client.DeleteAsync($"/api/documents/{uploaded.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var response = await client.GetAsync($"/api/documents/{uploaded.Id}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_duplicate_original_filenames_get_distinct_ids_and_stored_files()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        using var firstResponse = await UploadPdfAsync(client, "duplicate.pdf");
        using var secondResponse = await UploadPdfAsync(client, "duplicate.pdf");
        var first = await ReadJsonAsync<DocumentUploadResponseDto>(firstResponse);
        var second = await ReadJsonAsync<DocumentUploadResponseDto>(secondResponse);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.NotEqual(first.Id, second.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var records = await database.Documents.Where(document => document.OriginalFileName == "duplicate.pdf").ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal(2, records.Select(document => document.StoredFileName).Distinct().Count());
        Assert.All(records, record => Assert.True(File.Exists(Path.Combine(factory.StoragePath, record.StoredFileName))));
    }

    [Fact]
    public async Task Delete_existing_document_returns_no_content_and_removes_metadata_and_file()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);
        using var uploadResponse = await UploadPdfAsync(client, "delete-me.pdf");
        var uploaded = await ReadJsonAsync<DocumentUploadResponseDto>(uploadResponse);
        string storedFileName;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            storedFileName = (await database.Documents.SingleAsync(document => document.Id == uploaded.Id)).StoredFileName;
        }

        using var response = await client.DeleteAsync($"/api/documents/{uploaded.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await verificationDatabase.Documents.AnyAsync(document => document.Id == uploaded.Id));
        Assert.False(File.Exists(Path.Combine(factory.StoragePath, storedFileName)));
    }

    [Fact]
    public async Task Delete_nonexistent_document_returns_not_found()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateTestClient(factory);

        using var response = await client.DeleteAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<DocumentApiFactory> CreateFactoryAsync()
    {
        var factory = new DocumentApiFactory();
        await factory.InitializeDatabaseAsync();
        return factory;
    }

    private static HttpClient CreateTestClient(DocumentApiFactory factory)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static async Task<HttpResponseMessage> UploadPdfAsync(HttpClient client, string fileName)
    {
        using var content = CreateFileContent(fileName, "application/pdf", Encoding.ASCII.GetBytes(PdfContent));
        return await client.PostAsync("/api/documents", content);
    }

    private static MultipartFormDataContent CreateFileContent(string fileName, string contentType, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        return content;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The response body was empty.");
    }
}
