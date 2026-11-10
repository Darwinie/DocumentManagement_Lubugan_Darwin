namespace DocumentManagement.Api.Storage;

public interface IFileStorage
{
    Task SaveAsync(string storedFileName, Stream content, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string storedFileName, CancellationToken cancellationToken);
    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken);
}
