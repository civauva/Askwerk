// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Askwerk.Tests.Api;

/// <summary>
/// An OpenID Connect provider in memory (AWK-12): discovery, keys and the token endpoint, reached through the handler's
/// back channel. The test plays the browser's part at the authorize endpoint and sets the ID token to give out.
/// </summary>
internal sealed class FakeOidcProvider : IDisposable
{
    public const string Issuer = "https://idp.example.org";

    private readonly RSA _key = RSA.Create(2048);

    public FakeOidcProvider()
    {
        Handler = new Endpoints(this);
    }

    public HttpMessageHandler Handler { get; }

    /// <summary>Gets the forms posted to the token endpoint.</summary>
    public List<Dictionary<string, string>> TokenRequests { get; } = [];

    /// <summary>Gets or sets the claims of the next ID token; nonce and aud are filled in.</summary>
    public Dictionary<string, object> NextUser { get; set; } = [];

    public string? Nonce { get; set; }

    public string ClientId { get; set; } = "askwerk";

    public void Dispose()
    {
        _key.Dispose();
        Handler.Dispose();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string IdToken()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>(NextUser) { ["iss"] = Issuer, ["aud"] = ClientId, ["iat"] = now, ["exp"] = now + 300, ["nonce"] = Nonce ?? "" };
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = "k1" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = _key.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return header + "." + payload + "." + Base64Url(signature);
    }

    private sealed class Endpoints(FakeOidcProvider provider) : HttpMessageHandler
    {
        private static readonly string[] Code = ["code"];
        private static readonly string[] Public = ["public"];
        private static readonly string[] Rs256 = ["RS256"];
        private static readonly string[] S256 = ["S256"];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/.well-known/openid-configuration")
            {
                return JsonAnswer(new Dictionary<string, object>
                {
                    ["issuer"] = Issuer,
                    ["authorization_endpoint"] = Issuer + "/authorize",
                    ["token_endpoint"] = Issuer + "/token",
                    ["jwks_uri"] = Issuer + "/jwks",
                    ["response_types_supported"] = Code,
                    ["subject_types_supported"] = Public,
                    ["id_token_signing_alg_values_supported"] = Rs256,
                    ["code_challenge_methods_supported"] = S256,
                });
            }

            if (path == "/jwks")
            {
                var parameters = provider._key.ExportParameters(false);

                return JsonAnswer(new { keys = new[] { new { kty = "RSA", use = "sig", kid = "k1", alg = "RS256", n = Base64Url(parameters.Modulus!), e = Base64Url(parameters.Exponent!) } } });
            }

            if (path == "/token")
            {
                var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&').Select(part => part.Split('=', 2)).ToDictionary(part => Uri.UnescapeDataString(part[0]), part => Uri.UnescapeDataString(part.Length > 1 ? part[1].Replace('+', ' ') : ""));
                provider.TokenRequests.Add(form);

                return JsonAnswer(new { access_token = "at", token_type = "Bearer", expires_in = 300, id_token = provider.IdToken() });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonAnswer(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
