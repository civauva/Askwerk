// SPDX-License-Identifier: AGPL-3.0-only
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Askwerk.Api.Identity;

/// <summary>
/// A small LDAP v3 client over LDAPS (AWK-12, W22 step 3): simple bind and one search, in managed code, so the chiseled
/// image needs no native libldap. Filters are built as BER, never parsed from text, so a user name cannot change them.
/// </summary>
public sealed class LdapConnection : IAsyncDisposable
{
    /// <summary>Result code of a successful operation.</summary>
    public const int Success = 0;

    /// <summary>Result code of a wrong password or unknown bind name.</summary>
    public const int InvalidCredentials = 49;

    private readonly TcpClient _tcp;
    private readonly SslStream _stream;
    private int _messageId;

    private LdapConnection(TcpClient tcp, SslStream stream)
    {
        _tcp = tcp;
        _stream = stream;
    }

    /// <summary>Connects with TLS; the server's certificate must chain to the system's roots or to the given CA.</summary>
    public static async Task<LdapConnection> ConnectAsync(string host, int port, X509Certificate2? ca, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient();

        try
        {
            await tcp.ConnectAsync(host, port, cancellationToken);
            var stream = new SslStream(tcp.GetStream(), false, (_, certificate, _, errors) => Trusted(certificate, errors, ca));
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cancellationToken);

            return new LdapConnection(tcp, stream);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>A simple bind; returns the LDAP result code. An empty password is refused here: LDAP would take it as anonymous.</summary>
    public async Task<int> BindAsync(string dn, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dn);

        if (string.IsNullOrEmpty(password))
        {
            return InvalidCredentials;
        }

        var bind = Ber.Tlv(0x60, Ber.Integer(3), Ber.OctetString(dn), Ber.Tlv(0x80, Encoding.UTF8.GetBytes(password)));
        var id = await SendAsync(bind, cancellationToken);
        var (op, content) = await ReadAsync(id, cancellationToken);

        return op == 0x61 ? Ber.ResultCode(content) : throw new LdapException("Unexpected answer to bind.");
    }

    /// <summary>Searches the subtree for entries whose attributes equal the given values (all of them), at most sizeLimit.</summary>
    public async Task<IReadOnlyList<LdapEntry>> SearchAsync(string baseDn, IReadOnlyList<(string Attribute, string Value)> equals, IReadOnlyList<string> attributes, int sizeLimit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(equals);
        ArgumentNullException.ThrowIfNull(attributes);
        var filters = equals.Select(each => Ber.Tlv(0xA3, Ber.OctetString(each.Attribute), Ber.OctetString(each.Value))).ToArray();
        var filter = filters.Length == 1 ? filters[0] : Ber.Tlv(0xA0, filters);
        var search = Ber.Tlv(
            0x63,
            Ber.OctetString(baseDn),
            Ber.Tlv(0x0A, new byte[] { 2 }),
            Ber.Tlv(0x0A, new byte[] { 0 }),
            Ber.Integer(sizeLimit),
            Ber.Integer(10),
            Ber.Tlv(0x01, new byte[] { 0 }),
            filter,
            Ber.Tlv(0x30, [.. attributes.Select(Ber.OctetString)]));
        var id = await SendAsync(search, cancellationToken);
        var entries = new List<LdapEntry>();

        while (true)
        {
            var (op, content) = await ReadAsync(id, cancellationToken);

            switch (op)
            {
                case 0x64:
                    entries.Add(Ber.Entry(content));
                    break;
                case 0x73:
                    break;
                case 0x65:
                    var code = Ber.ResultCode(content);

                    // 4: size limit exceeded; the entries so far still came.
                    return code is Success or 4 ? entries : throw new LdapException($"Search failed with result code {code}.");
                default:
                    throw new LdapException("Unexpected answer to search.");
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await SendAsync(Ber.Tlv(0x42), CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or SocketException)
        {
            // already closed
        }

        await _stream.DisposeAsync();
        _tcp.Dispose();
    }

    private static bool Trusted(X509Certificate? certificate, SslPolicyErrors errors, X509Certificate2? ca)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (ca is null || certificate is null || errors != SslPolicyErrors.RemoteCertificateChainErrors)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        using var server = new X509Certificate2(certificate);

        return chain.Build(server);
    }

    private async Task<int> SendAsync(byte[] operation, CancellationToken cancellationToken)
    {
        var id = ++_messageId;
        await _stream.WriteAsync(Ber.Tlv(0x30, Ber.Integer(id), operation), cancellationToken);
        await _stream.FlushAsync(cancellationToken);

        return id;
    }

    private async Task<(byte Op, byte[] Content)> ReadAsync(int id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var (tag, message) = await Ber.ReadAsync(_stream, cancellationToken);

            if (tag != 0x30)
            {
                throw new LdapException("Not an LDAP message.");
            }

            var reader = new Ber.Reader(message);
            var messageId = (int)reader.Integer();
            var (op, content) = reader.Next();

            // Unsolicited notices (message id 0), e.g. a disconnection, end the conversation.
            if (messageId == 0)
            {
                throw new LdapException("The server ended the connection.");
            }

            if (messageId == id)
            {
                return (op, content);
            }
        }
    }
}

/// <summary>An entry found by a search: its DN and attributes (names without case).</summary>
/// <param name="Dn">The distinguished name.</param>
/// <param name="Attributes">Every attribute's values as bytes.</param>
public sealed record LdapEntry(string Dn, IReadOnlyDictionary<string, IReadOnlyList<byte[]>> Attributes)
{
    /// <summary>The first value of an attribute as text, if it has one.</summary>
    public string? Text(string attribute) => Attributes.TryGetValue(attribute, out var values) && values.Count > 0 ? Encoding.UTF8.GetString(values[0]) : null;

    /// <summary>Every value of an attribute as text.</summary>
    public IEnumerable<string> Texts(string attribute) => Attributes.TryGetValue(attribute, out var values) ? values.Select(value => Encoding.UTF8.GetString(value)) : [];
}

/// <summary>The directory did not answer as LDAP should.</summary>
public sealed class LdapException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="LdapException"/> class.</summary>
    public LdapException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LdapException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public LdapException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LdapException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public LdapException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The bits of BER (X.690) that LDAP needs.</summary>
internal static class Ber
{
    private const int MaxMessage = 1 << 20;

    public static byte[] Tlv(byte tag, params byte[][] parts)
    {
        var content = parts.SelectMany(part => part).ToArray();
        using var output = new MemoryStream();
        output.WriteByte(tag);
        WriteLength(output, content.Length);
        output.Write(content);

        return output.ToArray();
    }

    public static byte[] OctetString(string value) => Tlv(0x04, Encoding.UTF8.GetBytes(value));

    public static byte[] Integer(long value)
    {
        var bytes = new List<byte>();

        do
        {
            bytes.Insert(0, (byte)(value & 0xFF));
            value >>= 8;
        }
        while (value != 0 && value != -1);

        if (value == 0 && (bytes[0] & 0x80) != 0)
        {
            bytes.Insert(0, 0);
        }

        return Tlv(0x02, [.. bytes]);
    }

    public static int ResultCode(byte[] ldapResult) => (int)new Reader(ldapResult).Integer();

    public static LdapEntry Entry(byte[] content)
    {
        var reader = new Reader(content);
        var dn = Encoding.UTF8.GetString(reader.Next().Content);
        var attributes = new Dictionary<string, IReadOnlyList<byte[]>>(StringComparer.OrdinalIgnoreCase);
        var list = new Reader(reader.Next().Content);

        while (!list.Done)
        {
            var attribute = new Reader(list.Next().Content);
            var type = Encoding.UTF8.GetString(attribute.Next().Content);
            var values = new Reader(attribute.Next().Content);
            var all = new List<byte[]>();

            while (!values.Done)
            {
                all.Add(values.Next().Content);
            }

            attributes[type] = all;
        }

        return new LdapEntry(dn, attributes);
    }

    public static async Task<(byte Tag, byte[] Content)> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var tag = await ByteAsync(stream, cancellationToken);
        var first = await ByteAsync(stream, cancellationToken);
        var length = first;

        if ((first & 0x80) != 0)
        {
            var count = first & 0x7F;

            if (count is 0 or > 4)
            {
                throw new LdapException("Unsupported BER length.");
            }

            length = 0;

            for (var index = 0; index < count; index++)
            {
                length = (length << 8) | await ByteAsync(stream, cancellationToken);
            }
        }

        if (length > MaxMessage)
        {
            throw new LdapException("LDAP message too large.");
        }

        var content = new byte[length];
        await stream.ReadExactlyAsync(content, cancellationToken);

        return ((byte)tag, content);
    }

    private static async Task<int> ByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var one = new byte[1];
        await stream.ReadExactlyAsync(one, cancellationToken);

        return one[0];
    }

    private static void WriteLength(Stream output, int length)
    {
        if (length < 0x80)
        {
            output.WriteByte((byte)length);
            return;
        }

        var bytes = BitConverter.GetBytes(length).Reverse().SkipWhile(each => each == 0).ToArray();
        output.WriteByte((byte)(0x80 | bytes.Length));
        output.Write(bytes);
    }

    /// <summary>Reads the TLVs inside a constructed value one after another.</summary>
    internal sealed class Reader(byte[] data)
    {
        private int _at;

        public bool Done => _at >= data.Length;

        public (byte Tag, byte[] Content) Next()
        {
            if (_at + 2 > data.Length)
            {
                throw new LdapException("Truncated BER value.");
            }

            var tag = data[_at++];
            int length = data[_at++];

            if ((length & 0x80) != 0)
            {
                var count = length & 0x7F;

                if (count is 0 or > 4 || _at + count > data.Length)
                {
                    throw new LdapException("Unsupported BER length.");
                }

                length = 0;

                for (var index = 0; index < count; index++)
                {
                    length = (length << 8) | data[_at++];
                }
            }

            if (length < 0 || _at + length > data.Length)
            {
                throw new LdapException("Truncated BER value.");
            }

            var content = data[_at..(_at + length)];
            _at += length;

            return (tag, content);
        }

        public long Integer()
        {
            var (_, content) = Next();
            long value = (content.Length > 0 && (content[0] & 0x80) != 0) ? -1 : 0;

            foreach (var each in content)
            {
                value = (value << 8) | each;
            }

            return value;
        }
    }
}
