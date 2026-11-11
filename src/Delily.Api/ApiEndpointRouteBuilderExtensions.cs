using Delily.Api.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Delily.Api;

public static class ApiEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the HTTPS submission surface. The endpoints intentionally return
    /// 501 until Phase 3b wires authentication, idempotency, MIME building,
    /// DKIM signing, and the durable queue.
    /// </summary>
    public static IEndpointRouteBuilder MapDelilyApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1").WithTags("Messages");

        api.MapPost("/messages", () => Results.Problem(
                title: "HTTP submission is not implemented yet",
                detail: "The authenticated message-submission API is implemented in Phase 3b.",
                statusCode: StatusCodes.Status501NotImplemented))
            .WithName("SendMessage")
            .Produces<SendMessageResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        api.MapGet("/messages/{id}", (string id) => Results.Problem(
                title: "Message status is not implemented yet",
                detail: $"Status lookup for '{id}' is implemented in Phase 3b.",
                statusCode: StatusCodes.Status501NotImplemented))
            .WithName("GetMessageStatus")
            .Produces<MessageStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        return endpoints;
    }
}
