// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Askwerk.Application.Domains;

namespace Askwerk.Infrastructure.Dns;

/// <summary>
/// Asks the DNS servers the machine uses, over UDP, three seconds each (AWK-7). Any failure counts as "not found":
/// the domain stays pending and the next check tries again.
/// </summary>
public sealed class DnsLookup : IDnsLookup
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public async Task<string?> CnameAsync(string name, CancellationToken cancellationToken) =>
        (await AskAsync(name, DnsMessage.Cname, cancellationToken)).FirstOrDefault(record => record.Type == DnsMessage.Cname)?.Data;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> TxtAsync(string name, CancellationToken cancellationToken) =>
        [.. (await AskAsync(name, DnsMessage.Txt, cancellationToken)).Where(record => record.Type == DnsMessage.Txt).Select(record => record.Data)];

    private static IEnumerable<IPAddress> Servers() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().DnsAddresses)
            .Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal)
            .Distinct();

    private static async Task<IReadOnlyList<DnsRecord>> AskAsync(string name, ushort type, CancellationToken cancellationToken)
    {
        foreach (var server in Servers())
        {
            try
            {
                var id = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1);
                using var udp = new UdpClient(server.AddressFamily);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Timeout);
                await udp.SendAsync(DnsMessage.Query(id, name, type), new IPEndPoint(server, 53), timeout.Token);

                while (true)
                {
                    var answer = await udp.ReceiveAsync(timeout.Token);

                    if (answer.RemoteEndPoint.Address.Equals(server))
                    {
                        return DnsMessage.Parse(answer.Buffer, id);
                    }
                }
            }
            catch (Exception exception) when (exception is SocketException or FormatException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // The next server, if any.
            }
        }

        return [];
    }
}
