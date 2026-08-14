using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Todo.Core.Errors;
using Todo.Host.Links;

namespace Todo.Host.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystem(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/system/open-link", Results<NoContent, BadRequest<ApiError>> (
            OpenLinkRequest request, ILinkLauncher launcher) =>
        {
            // The launcher asks the operating system to open the link with whatever program is
            // registered for it, and a note holds text somebody else wrote. Only the web is
            // allowed through, so a note can never start a program.
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
                || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.SystemUnsupportedScheme,
                    "Only http and https links can be opened.");
            }

            launcher.Open(url);

            return TypedResults.NoContent();
        })
        .WithName("openLink")
        .WithTags("System");

        return app;
    }
}
