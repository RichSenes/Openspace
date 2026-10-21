using Microsoft.AspNetCore.Diagnostics;
using Openspace.Application;
using Openspace.Domain.Common;

namespace Openspace.Api;

/// <summary>Turns application and domain errors into problem details.</summary>
internal sealed class ErrorHandling(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Not allowed"),
            RejectedException => (StatusCodes.Status422UnprocessableEntity, "Not possible"),
            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        var context = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = exception.Message },
        };
        if (exception is RejectedException rejected)
        {
            context.ProblemDetails.Extensions["code"] = rejected.Code;
        }

        return await problemDetails.TryWriteAsync(context).ConfigureAwait(false);
    }
}
