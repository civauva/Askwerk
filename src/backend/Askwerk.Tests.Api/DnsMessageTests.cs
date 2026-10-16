// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Infrastructure.Dns;

namespace Askwerk.Tests.Api;

/// <summary>Our own small DNS client (AWK-7): queries as RFC 1035 writes them, answers with name compression.</summary>
public sealed class DnsMessageTests
{
    private static byte[] Name(params string[] labels) => [.. labels.SelectMany(label => (byte[])[(byte)label.Length, .. label.Select(character => (byte)character)]), 0];

    [Fact]
    public void Query_AsksOneQuestion_WithRecursion()
    {
        var query = DnsMessage.Query(0x1234, "_askwerk.umfragen.acme.at", DnsMessage.Txt);

        Assert.Equal(
            [0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0, .. Name("_askwerk", "umfragen", "acme", "at"), 0, 16, 0, 1],
            query);
    }

    [Fact]
    public void Parse_ReadsCnameAndTxt_WithCompressedNames()
    {
        byte[] question = [.. Name("umfragen", "acme", "at"), 0, 5, 0, 1];
        byte[] answer =
        [
            0x12, 0x34, 0x81, 0x80, 0, 1, 0, 2, 0, 0, 0, 0,
            .. question,
            // CNAME umfragen.acme.at -> surveys.<pointer to acme.at at offset 12 + 9>
            0xC0, 12, 0, 5, 0, 1, 0, 0, 1, 0, 0, 10, 7, (byte)'s', (byte)'u', (byte)'r', (byte)'v', (byte)'e', (byte)'y', (byte)'s', 0xC0, 21,
            // TXT with two strings, read as one text
            0xC0, 12, 0, 16, 0, 1, 0, 0, 1, 0, 0, 12, 5, (byte)'a', (byte)'s', (byte)'k', (byte)'-', (byte)'v', 5, (byte)'e', (byte)'r', (byte)'i', (byte)'f', (byte)'y',
        ];

        var records = DnsMessage.Parse(answer, 0x1234);

        Assert.Equal([(DnsMessage.Cname, "surveys.acme.at"), (DnsMessage.Txt, "ask-verify")], records.Select(record => (record.Type, record.Data)));
    }

    [Fact]
    public void Parse_AnotherQuerysAnswer_OrAPointerLoop_IsRefused()
    {
        Assert.Throws<FormatException>(() => DnsMessage.Parse([0x99, 0x99, 0x81, 0x80, 0, 0, 0, 0, 0, 0, 0, 0], 0x1234));
        Assert.Throws<FormatException>(() => DnsMessage.Parse([0x12, 0x34, 0x81, 0x80, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 12, 0, 5, 0, 1], 0x1234));
    }
}
