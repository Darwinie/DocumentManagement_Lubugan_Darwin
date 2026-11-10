namespace DocumentManagement.Api.Services;

public sealed class DocumentOperationException(string message, int statusCode, string errorCode, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;

    public string ErrorCode { get; } = errorCode;
}
