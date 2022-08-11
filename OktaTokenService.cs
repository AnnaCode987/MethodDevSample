

namespace Authentication.Okta;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Authentication;
using Identity.Okta;

/// <summary>
/// Represents a token service for Okta.
/// </summary>
public class OktaTokenService : IOktaTokenService
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptions<OktaUserManagementOptions> optionsAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="OktaTokenService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The Http client factory.</param>
    /// <param name="optionsAccessor">The accessor to user management options.</param>
    public OktaTokenService(IHttpClientFactory httpClientFactory, IOptions<OktaUserManagementOptions> optionsAccessor)
    {
        this.httpClientFactory = httpClientFactory;
        this.optionsAccessor = optionsAccessor;
    }

    /// <inheritdoc/>
    public async Task<OidcTokens> GetAccessTokensAsync(string username, string password)
    {
        var options = this.optionsAccessor.Value;

        var endpoint = new Uri(options.BaseApiUri, options.AccessTokenPath);

        using var httpClient = this.httpClientFactory.CreateOktaAuthenticationClient();

        var bodyDictionary = new Dictionary<string, string>
        {
            { "grant_type", "password" },
            { "username", username },
            { "password", password },
        };

        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ClientId}:{options.ClientSecret}"));

        // Send the request
        using var content = new FormUrlEncodedContent(bodyDictionary);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
        var response = await httpClient.PostAsync(endpoint, content).ConfigureAwait(false);

        var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var tokens = JsonSerializer.Deserialize<OidcTokens>(result);

        if (tokens is null)
        {
            throw new AuthenticationException("Could not deserialize OIDC tokens");
        }

        return tokens;
    }

    /// <inheritdoc/>
    public async Task<OidcTokens> GetAdminAccessTokensAsync()
    {
        var options = this.optionsAccessor.Value;

        var endpoint = new Uri(options.BaseApiUri, options.AccessTokenPath);

        using var httpClient = this.httpClientFactory.CreateOktaAuthenticationClient();

        var bodyDictionary = new Dictionary<string, string>
        {
            { "grant_type", "password" },
            { "username", options.SystemUsername },
            { "password", options.SystemPassword },
        };

        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ClientId}:{options.ClientSecret}"));

        // Send the request
        using var content = new FormUrlEncodedContent(bodyDictionary);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
        var response = await httpClient.PostAsync(endpoint, content).ConfigureAwait(false);

        var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var tokens = JsonSerializer.Deserialize<OidcTokens>(result);

        if (tokens is null)
        {
            throw new AuthenticationException("Could not deserialize OIDC tokens");
        }

        return tokens;
    }
}
