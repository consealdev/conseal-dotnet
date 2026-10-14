using ConsealDev.ConsealSync.Wire;
using Microsoft.AspNetCore.Http;

namespace ConsealDev.ConsealSync.Endpoints;

/// <summary>The routes' answers, all written with <see cref="WireJson"/>.</summary>
internal static class ApiResults
{
    /// <summary>How long a client waits before retrying a write the store couldn't commit.</summary>
    private const string BusyRetryAfterSeconds = "1";

    public static IResult Ok<T>(T body) => Results.Json(body, WireJson.Options);

    public static IResult Created<T>(T body) => Results.Json(body, WireJson.Options, statusCode: StatusCodes.Status201Created);

    public static IResult Error(int status, string code, string message) =>
        Results.Json(new ErrorBody(code, message), WireJson.Options, statusCode: status);

    public static IResult Invalid(string message) => Error(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, message);

    /// <summary>
    /// The host's authorization let the request through, but the configured claim holds no usable user id: absent,
    /// or longer than <see cref="RequestUser.MaxUserIdBytes"/>.
    /// </summary>
    public static IResult Unauthenticated() =>
        Error(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, "The signed-in user has no usable user id claim.");

    /// <summary>Transient: other writes to the collection kept the store from committing. The client retries.</summary>
    public static IResult Busy(HttpContext context)
    {
        context.Response.Headers.RetryAfter = BusyRetryAfterSeconds;
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
