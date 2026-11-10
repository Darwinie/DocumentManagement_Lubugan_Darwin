using Microsoft.Extensions.Options;

namespace DocumentManagement.Api.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(
        IOptions<FileStorageOptions> options,
        IWebHostEnvironment environment,
        ILogger<LocalFileStorage> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredPath, environment.ContentRootPath));
        _logger = logger;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(string storedFileName, Stream content, CancellationToken cancellationToken)
    {
        var path = GetSafePath(storedFileName);
        var createdFile = false;
        try
        {
            await using var destination = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            createdFile = true;
            await content.CopyToAsync(destination, cancellationToken);
        }
        catch
        {
            if (createdFile)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError(cleanupException, "Failed to remove partial stored file {StoredFileName} after a storage error.", storedFileName);
                }
            }

            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSafePath(storedFileName);
        try
        {
            Stream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task<bool> ExistsAsync(string storedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSafePath(storedFileName);
        try
        {
            _ = File.GetAttributes(path);
            return Task.FromResult(true);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult(false);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult(false);
        }
    }

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSafePath(storedFileName);
        File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetSafePath(string storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName) ||
            !string.Equals(Path.GetFileName(storedFileName), storedFileName, StringComparison.Ordinal) ||
            storedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Invalid storage file name.", nameof(storedFileName));
        }

        var path = Path.GetFullPath(Path.Combine(_rootPath, storedFileName));
        var relativePath = Path.GetRelativePath(_rootPath, path);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid storage file name.", nameof(storedFileName));
        }

        return path;
    }
}
