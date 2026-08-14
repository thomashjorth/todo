using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Todo.Host.Endpoints;

internal static class ApiErrors
{
    public static BadRequest<ApiError> BadRequest(string code, string message)
        => TypedResults.BadRequest(new ApiError { Code = code, Message = message });
}
