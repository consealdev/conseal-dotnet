// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

namespace ConsealDev.Conseal.Wire;

/// <summary>The contract's error body. Clients act on <paramref name="Code"/>; the message is for developers.</summary>
internal sealed record ErrorBody(string Code, string Message);
