using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocumentManagement.Api.Services;

public sealed class DocumentExceptionHandler(ILogger<DocumentExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Document API request was canceled by the client.");
            return true;
        }

        var (statusCode, title, detail, errorCode) = exception is DocumentOperationException operationException
            ? (operationException.StatusCode, "Document request failed", operationException.Message, operationException.ErrorCode)
            : (StatusCodes.Status500InternalServerError, "An unexpected error occurred", "The request could not be completed.", "unexpected_error");

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Document API request failed with status {StatusCode}.", statusCode);
        }
        else
        {
            logger.LogInformation(exception, "Document API request was rejected with status {StatusCode}.", statusCode);
        }

        httpContext.Response.StatusCode = statusCode;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = errorCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
