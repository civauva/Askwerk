// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.HealthProbe;

using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

return await Probe.RunAsync(client, Probe.DefaultUrl, CancellationToken.None);
