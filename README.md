# Document Management REST API

**GitHub repository name:** `DocumentManagement_Lubugan_Darwin`

## 1. Project Overview

This ASP.NET Core Web API stores document metadata in SQL Server and document content in a local filesystem directory. It supports upload, metadata listing and retrieval, download, and deletion.

## 2. Technologies Used

- C# and ASP.NET Core on .NET 10
- Entity Framework Core 10 with SQL Server
- Swagger UI through Swashbuckle
- Local filesystem storage
- xUnit and ASP.NET Core `WebApplicationFactory` integration tests

## 3. Solution Architecture

Controllers handle HTTP requests and delegate to `IDocumentService`. The service validates uploaded files, coordinates EF Core metadata persistence, and uses `IFileStorage` for physical file operations. The database contains metadata only; uploaded bytes are kept outside `wwwroot`.

## 4. Project Structure

```text
DocumentManagement/
  .gitignore
  README.md
  DocumentManagement.Api/
    DocumentManagement.Api.slnx
    DocumentManagement.Api/
      Controllers/       HTTP endpoints
      Data/              EF Core context
      DTOs/              Public API response models
      Migrations/         EF Core schema migrations
      Models/             Database entities
      Services/           Document business logic and error handling
      Storage/            Local filesystem implementation and options
  DocumentManagement.Tests/
    Integration/         WebApplicationFactory API tests
```

## 5. Prerequisites

- .NET 10 SDK
- SQL Server Express with the `SQLEXPRESS` instance (or another SQL Server instance configured in the connection string)
- Visual Studio 2022 or another .NET-compatible editor (optional)
- EF Core command-line tool 10.0.13 for creating and applying migrations

## 6. Installation and Setup

From the repository root:

```powershell
dotnet restore DocumentManagement.Api\DocumentManagement.Api.slnx
```

The API project is `DocumentManagement.Api\DocumentManagement.Api`; the test project is `DocumentManagement.Tests`.

If `dotnet ef` is not installed, install the version matching the project:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.13
```

## 7. Database Configuration

`ConnectionStrings:DefaultConnection` in `DocumentManagement.Api\DocumentManagement.Api\appsettings.json` defaults to SQL Server Express instance `SQLEXPRESS`:

```text
Server=.\SQLEXPRESS;Database=DocumentManagementDb;Trusted_Connection=True;TrustServerCertificate=True;
```

Override it with user secrets, an environment variable (`ConnectionStrings__DefaultConnection`), or deployment configuration. Do not put credentials in source control. File storage is configured with `FileStorage:RootPath` (default `Storage`, relative to the API content root) and `FileStorage:MaxFileSizeBytes` (10 MiB).

## 8. Database Migration Instructions

The initial migration is checked into `DocumentManagement.Api\DocumentManagement.Api\Migrations`. From the repository root, apply it with:

```powershell
dotnet ef database update --project DocumentManagement.Api\DocumentManagement.Api\DocumentManagement.Api.csproj --startup-project DocumentManagement.Api\DocumentManagement.Api\DocumentManagement.Api.csproj
```

Create later migrations with `dotnet ef migrations add <MigrationName>` using the same project and startup project arguments. The API does not create or migrate the database automatically at startup.

## 9. Running the Application

After configuring a reachable SQL Server and applying migrations:

```powershell
dotnet run --project DocumentManagement.Api\DocumentManagement.Api\DocumentManagement.Api.csproj
```

## 10. Swagger Documentation

In the Development environment, open Swagger UI at `https://localhost:7202/swagger` (HTTPS) or `http://localhost:5184/swagger` (HTTP) when using the included Visual Studio launch profiles. Swagger UI includes all five document endpoints and a multipart file selector for upload.

## 11. API Endpoint Reference

| Method | Route | Purpose | Success |
|---|---|---|---|
| `POST` | `/api/documents` | Upload multipart field `file` | `201 Created` |
| `GET` | `/api/documents` | List metadata | `200 OK` |
| `GET` | `/api/documents/{id}` | Get metadata by GUID | `200 OK`, `404 Not Found` |
| `GET` | `/api/documents/{id}/download` | Download content using the original filename | `200 OK`, `404 Not Found` |
| `DELETE` | `/api/documents/{id}` | Delete content and metadata | `204 No Content`, `404 Not Found` |

Validation failures return `400 Bad Request`; uploads over the configured maximum return `413 Payload Too Large`. Errors use Problem Details with a stable `code` extension.

Example requests (run the API first; replace the sample file path and ID as needed):

```bash
BASE_URL="http://localhost:5184"
# Replace this with the GUID returned by the upload request.
DOCUMENT_ID="replace-with-returned-guid"

# Upload a document
curl -X POST "$BASE_URL/api/documents" \
  -H "accept: application/json" \
  -F "file=@./sample.pdf"

# List metadata
curl "$BASE_URL/api/documents"

# Retrieve metadata
curl "$BASE_URL/api/documents/$DOCUMENT_ID"

# Download the original file name
curl -OJ "$BASE_URL/api/documents/$DOCUMENT_ID/download"

# Delete the document
curl -X DELETE "$BASE_URL/api/documents/$DOCUMENT_ID"
```

## 12. Upload Validation and Supported File Types

Uploads are limited to 10 MiB. The current allowlist is PDF (`.pdf`, `application/pdf`), PNG (`.png`, `image/png`), and JPEG (`.jpg`/`.jpeg`, `image/jpeg`). Extension and MIME type must agree, and the request content is checked for the corresponding leading signature bytes. This is a lightweight format check and does not scan for malware or validate every internal structure of a document.

Empty files, unsupported extensions, mismatched MIME types, invalid signatures, and invalid or excessively long names are rejected. The original filename is reduced to a basename for metadata; it is never used as a storage path.

## 13. File Storage Approach

Files are written asynchronously to the configured absolute storage directory. Each stored name is generated from a GUID and the validated extension. SQL Server stores only the generated name and metadata, never file content. The storage root is created automatically. The `Storage` directory should be included in deployment backups and access-controlled by the host.

## 14. Error Handling and Security Considerations

- API errors use Problem Details and do not return stack traces.
- Generated storage names and path checks prevent client-controlled path traversal.
- Files are stored outside the web root and downloaded only through the API.
- Database persistence failures trigger best-effort removal of the newly stored file.
- An unexpected physical deletion failure prevents metadata deletion. If the file is already absent, metadata deletion is allowed and logged.
- Authentication, authorization, malware scanning, quotas, and remote/object storage are outside this assessment's scope.

## 15. Running Automated Tests

```powershell
dotnet test DocumentManagement.Tests\DocumentManagement.Tests.csproj
```

In Visual Studio, open `DocumentManagement.Api.slnx`, build the solution, open **Test > Test Explorer**, and select **Run All Tests**.

The integration test factory replaces the production SQL Server registration with an isolated SQLite in-memory database and configures a unique temporary storage directory for each test. Each test owns and removes its database connection and files. SQLite exercises relational behavior without accessing the configured development database; migration application should still be checked against the intended SQL Server environment.

## 16. Known Limitations and Assumptions

- Only PDF, PNG, and JPEG are accepted; validation is signature-based and is not a malware scan.
- Local filesystem storage is intended for a single application instance with persistent disk. Multi-instance deployments need shared or object storage.
- Filesystem and database operations cannot participate in one atomic transaction. Upload cleanup is best effort; deletion removes the file before metadata, so a subsequent database failure can leave metadata pointing to a missing file.
- The API does not automatically apply migrations on startup.
- Applying the migration requires the configured SQL Server instance (`.\SQLEXPRESS` by default) to be installed and running on the development machine.

## 17. AI-Assisted Development Disclosure

OpenAI Codex was used to inspect the existing project, draft and modify the model, data access, storage, service, controller, test, and documentation code, and run build and test commands. The generated changes should be reviewed by the submitting developer before assessment, especially SQL Server-specific behavior, the migration, file validation policy, exception handling, deletion consistency, and integration-test coverage.
