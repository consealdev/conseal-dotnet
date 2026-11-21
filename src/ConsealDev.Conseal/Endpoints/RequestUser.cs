// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

using System.Security.Claims;
using System.Text;
using ConsealDev.Conseal.Configuration;
using Microsoft.AspNetCore.Http;

namespace ConsealDev.Conseal.Endpoints;

/// <summary>The signed-in user, as the host app's sign-in names them. Conseal Sync has no identity of its own.</summary>
internal static class RequestUser
{
    /// <summary>
    /// The longest user id the routes take, in UTF-8 bytes. It is part of every storage key and every cursor, which
    /// the contract caps at 1024 characters.
    /// </summary>
    public const int MaxUserIdBytes = 256;

    private const string SubClaim = "sub";

    /// <summary>The user's id; null when the configured claim holds none the routes can use.</summary>
    public static string? Find(HttpContext context, ConsealSyncOptions options)
    {
        var user = context.User;
        var userId = user.FindFirst(options.UserIdClaim)?.Value;
        if (userId is null && options.UserIdClaim == SubClaim)
        {
            // ASP.NET's JWT handler renames sub to NameIdentifier unless the host turns inbound claim mapping off.
            userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
        return string.IsNullOrEmpty(userId) || Encoding.UTF8.GetByteCount(userId) > MaxUserIdBytes ? null : userId;
    }
}
