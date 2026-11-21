// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConsealDev.Conseal.Wire;

/// <summary>
/// The JSON every route answers with, whatever the host app's own JSON settings: camelCase names, and nulls written
/// out, since the contract has fields such as <c>ciphertext</c> and <c>current</c> that are null rather than absent.
/// </summary>
internal static class WireJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
