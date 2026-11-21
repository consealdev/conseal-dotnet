// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

using ConsealDev.Conseal.Storage;
using Microsoft.AspNetCore.Http;

namespace ConsealDev.Conseal.Endpoints;

/// <summary>
/// Answers <c>503</c> with <c>Retry-After</c> when a store couldn't commit for other writes, which the contract makes
/// a transient answer. A push's writes before the busy one stand: retried, each gets a <c>409</c> carrying the
/// version it made, which the client merges with nothing left to change.
/// </summary>
internal sealed class StoreBusyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (ConsealSyncStoreBusyException)
        {
            return ApiResults.Busy(context.HttpContext);
        }
    }
}
