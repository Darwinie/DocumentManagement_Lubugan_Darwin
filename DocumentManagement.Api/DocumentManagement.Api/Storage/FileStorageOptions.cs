namespace DocumentManagement.Api.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string RootPath { get; set; } = "Storage";

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}
