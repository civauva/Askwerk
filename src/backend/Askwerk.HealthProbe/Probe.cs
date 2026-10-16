// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.HealthProbe;

/// <summary>
/// The container healthcheck: asks the local Askwerk process whether it is ready. A tiny native binary,
/// so the runtime image needs neither curl nor a second .NET start every few seconds.
/// </summary>
public static class Probe
{
    /// <summary>The readiness endpoint of the Askwerk process in the same container.</summary>
    public static readonly Uri DefaultUrl = new("http://localhost:8080/health/ready");

    /// <summary>Calls the readiness endpoint.</summary>
    /// <param name="client">The HTTP client to use.</param>
    /// <param name="url">The readiness URL.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>0 when ready, 1 otherwise (Docker healthcheck convention).</returns>
    public static async Task<int> RunAsync(HttpClient client, Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);

            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
