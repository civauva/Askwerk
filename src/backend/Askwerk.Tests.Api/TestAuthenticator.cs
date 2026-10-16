// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Askwerk.Tests.Api;

/// <summary>
/// A passkey authenticator in software (WebAuthn level 3, "none" attestation, ES256), so the API tests can
/// create and use passkeys the way a browser would: it answers creation and request options with the JSON
/// that <c>PublicKeyCredential.toJSON()</c> produces.
/// </summary>
internal sealed class TestAuthenticator(string origin) : IDisposable
{
    private static readonly string[] InternalTransport = ["internal"];
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _credentialId = RandomNumberGenerator.GetBytes(16);
    private byte[] _userHandle = [];
    private uint _signCount;

    public string CredentialId => Base64Url(_credentialId);

    /// <summary>Answers navigator.credentials.create() options with a new credential.</summary>
    public string Create(string optionsJson)
    {
        var options = JsonNode.Parse(optionsJson)!;
        var rpId = options["rp"]?["id"]?.GetValue<string>() ?? new Uri(origin).Host;
        _userHandle = FromBase64Url(options["user"]!["id"]!.GetValue<string>());
        var clientData = ClientData("webauthn.create", options["challenge"]!.GetValue<string>());
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        var coseKey = Cbor.Map(
            (Cbor.Int(1), Cbor.Int(2)),
            (Cbor.Int(3), Cbor.Int(-7)),
            (Cbor.Int(-1), Cbor.Int(1)),
            (Cbor.Int(-2), Cbor.Bytes(parameters.Q.X!)),
            (Cbor.Int(-3), Cbor.Bytes(parameters.Q.Y!)));
        var authenticatorData = Concat(
            SHA256.HashData(Encoding.UTF8.GetBytes(rpId)),
            [0x45], // user present, user verified, attested credential data
            BigEndian(_signCount),
            new byte[16],
            [(byte)(_credentialId.Length >> 8), (byte)_credentialId.Length],
            _credentialId,
            coseKey);
        var attestation = Cbor.Map(
            (Cbor.Text("fmt"), Cbor.Text("none")),
            (Cbor.Text("attStmt"), Cbor.Map()),
            (Cbor.Text("authData"), Cbor.Bytes(authenticatorData)));

        return JsonSerializer.Serialize(new
        {
            id = CredentialId,
            rawId = CredentialId,
            type = "public-key",
            response = new
            {
                clientDataJSON = Base64Url(clientData),
                attestationObject = Base64Url(attestation),
                transports = InternalTransport,
            },
            clientExtensionResults = new { },
            authenticatorAttachment = "platform",
        });
    }

    /// <summary>Answers navigator.credentials.get() options with a signed assertion.</summary>
    public string Get(string optionsJson, bool tamper = false)
    {
        var options = JsonNode.Parse(optionsJson)!;
        var rpId = options["rpId"]?.GetValue<string>() ?? new Uri(origin).Host;
        var clientData = ClientData("webauthn.get", options["challenge"]!.GetValue<string>());
        _signCount++;
        var authenticatorData = Concat(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), [0x05], BigEndian(_signCount));
        var signature = _key.SignData(Concat(authenticatorData, SHA256.HashData(clientData)), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        if (tamper)
        {
            signature[^1] ^= 0xff;
        }

        return JsonSerializer.Serialize(new
        {
            id = CredentialId,
            rawId = CredentialId,
            type = "public-key",
            response = new
            {
                clientDataJSON = Base64Url(clientData),
                authenticatorData = Base64Url(authenticatorData),
                signature = Base64Url(signature),
                userHandle = Base64Url(_userHandle),
            },
            clientExtensionResults = new { },
            authenticatorAttachment = "platform",
        });
    }

    public void Dispose() => _key.Dispose();

    private byte[] ClientData(string type, string challenge) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type, challenge, origin, crossOrigin = false });

    private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }

    /// <summary>Just enough CBOR (RFC 8949) for keys and attestation objects.</summary>
    private static class Cbor
    {
        public static byte[] Int(long value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Bytes(byte[] value) => [.. Head(2, (ulong)value.Length), .. value];

        public static byte[] Text(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            return [.. Head(3, (ulong)bytes.Length), .. bytes];
        }

        public static byte[] Map(params (byte[] Key, byte[] Value)[] entries) =>
            [.. Head(5, (ulong)entries.Length), .. entries.SelectMany(entry => entry.Key.Concat(entry.Value))];

        private static byte[] Head(byte major, ulong length) => length switch
        {
            < 24 => [(byte)((major << 5) | (byte)length)],
            <= byte.MaxValue => [(byte)((major << 5) | 24), (byte)length],
            _ => [(byte)((major << 5) | 25), (byte)(length >> 8), (byte)length],
        };
    }
}
