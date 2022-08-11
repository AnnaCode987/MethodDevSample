
namespace Authentication.Okta;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Web;

/// <summary>
/// Represents a authentication hander for the Okta workflow.
/// </summary>
public class OktaAuthenticationHandler : AuthenticationHandler<OktaAuthenticationOptions>, IAuthenticationSignOutHandler
{
    private readonly OktaAuthenticationOptions options;
    private readonly UrlEncoder encoder;
    private readonly ISystemClock clock;
    private readonly IHttpClientFactory httpClientFactory;
    private string authCode = string.Empty;
    private string interactionCode = string.Empty;
    private AuthenticationProperties properties;

    /// <summary>
    /// Initializes a new instance of the <see cref="OktaAuthenticationHandler"/> class.
    /// </summary>
    /// <param name="options">The <see cref="OktaAuthenticationOptions"/> for the authentication.</param>
    /// <param name="httpClientFactory">The http client factory.</param>
    /// <param name="logger">The <see cref="ILoggerFactory"/> for the authentication.</param>
    /// <param name="encoder">The <see cref="UrlEncoder"/> for the authentication.</param>
    /// <param name="clock">The <see cref="ISystemClock"/> for the authentication.</param>
    public OktaAuthenticationHandler(
        IOptionsMonitor<OktaAuthenticationOptions> options,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        this.options = options.Get(OktaAuthenticationOptions.Name) ?? throw new ArgumentNullException(nameof(options));
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        this.encoder = encoder;
        this.clock = clock;
        this.properties = new AuthenticationProperties();
    }

    /// <summary>
    /// Gets or sets the events.
    /// </summary>
    protected new OpenIdConnectEvents Events
    {
        get
        {
            return (OpenIdConnectEvents)(this.options.Events ?? new OpenIdConnectEvents());
        }

        set
        {
            this.Events = value;
        }
    }

    private string PathBase => this.Request.Scheme + "://" + this.Request.Host + this.Request.PathBase;

    /// <inheritdoc/>
    public Task SignOutAsync(AuthenticationProperties? properties)
    {
        var cookie = this.Context.Request.Cookies[OktaAuthenticationOptions.Name];

        if (cookie != null)
        {
            this.Response.Cookies.Delete(OktaAuthenticationOptions.Name);
        }

        // delete cookie created under the legacy report by setting the expiration date back.
        var oldCookie = this.GetCookieOptions(true, true);
        this.Response.Cookies.Append(OktaAuthenticationOptions.Name, string.Empty, oldCookie);

        var codeVerifierCookie = this.GetCookieOptions(false, false);
        this.Response.Cookies.Delete(OktaAuthenticationOptions.CodeVerifierCookieName, codeVerifierCookie);

        this.Response.Redirect($"{this.PathBase}");

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string tokenResponse = string.Empty;

        this.authCode = this.Request.Query["code"];
        this.interactionCode = this.Request.Query["interaction_code"];

        if (!string.IsNullOrEmpty(this.authCode))
        {
            tokenResponse = await this.ExchangeAuthCodeAsync(this.authCode);
        }
        else if (!string.IsNullOrEmpty(this.interactionCode))
        {
            tokenResponse = await this.ExchangeInteractionCodeAsync(this.interactionCode);
        }

        if (string.IsNullOrEmpty(tokenResponse))
        {
            var message = "Handler: {0} - Error exchanging the authorization code for access and id tokens.";
            Log.Error(message, nameof(OktaAuthenticationHandler));

            return AuthenticateResult.Fail(string.Format(message, nameof(OktaAuthenticationHandler)));
        }

        var tokens = JsonSerializer.Deserialize<OidcTokens>(tokenResponse);

        if (tokens == null)
        {
            Log.Error("Handler: {0} - Error reading token response from Authorization server.", nameof(OktaAuthenticationHandler));

            return AuthenticateResult.Fail("Invalid token response");
        }

  
        this.StoreTokens(tokens);

        this.CreateLegacyReportsCookie();

        var principal = this.AddUserClaims(tokens);
        if (principal == null)
        {
            return HandleRequestResult.Fail("Handler: OktaAuthenticationHandler - Failed to add user claim from authorization server.", this.properties);
        }

        var ticket = await this.CreateTicketAsync(principal);

        if (ticket != null)
        {
            return HandleRequestResult.Success(ticket);
        }
        else
        {
            return HandleRequestResult.Fail("Handler: OktaAuthenticationHandler - Failed to create Auth ticket for user from authorization server.", this.properties);
        }
    }

    /// <inheritdoc/>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var codeVerifier = Pkce.GenerateCodeVerifier();
        var codeChallenge = Pkce.GenerateCodeChallenge(codeVerifier);

        var codeVerifierCookie = this.GetCookieOptions(false, false);
        this.Response.Cookies.Delete(OktaAuthenticationOptions.CodeVerifierCookieName);
        this.Response.Cookies.Append(OktaAuthenticationOptions.CodeVerifierCookieName, codeVerifier, codeVerifierCookie);

        this.properties = properties;
        this.Response.Redirect($"{this.Request.PathBase}{this.Options.LoginPath}?ReturnUrl={this.encoder.Encode(this.CurrentUri)}&challenge={codeChallenge}");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        this.Response.Redirect($"{this.Request.PathBase}/Public/AccessDenied");
        return Task.CompletedTask;
    }

    private async Task<string> ExchangeInteractionCodeAsync(string authCode)
    {
        string tokenData = string.Empty;

        if (!this.AreOptionsValid())
        {
            return tokenData;
        }

        var codeVerifier = this.Request.Cookies[OktaAuthenticationOptions.CodeVerifierCookieName];

        if (codeVerifier == null)
        {
            Log.Error("Required code verifier does not exist in HTTP request cookies under the name '{CookieName}'", OktaAuthenticationOptions.CodeVerifierCookieName);
            return string.Empty;
        }

        var url = $"{this.options.Authority}{this.options.TokenEndPoint}";
        using var httpClient = this.httpClientFactory.CreateClient();
        using (var request = new HttpRequestMessage(new HttpMethod("POST"), url))
        {
            var byteArray = Encoding.ASCII.GetBytes($"{this.options.ClientId}:{this.options.ClientSecret}");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            var postData = new List<KeyValuePair<string, string>>();
            postData.Add(new KeyValuePair<string, string>("redirect_uri", $"{this.PathBase}{this.options.CallbackPath}"));
            postData.Add(new KeyValuePair<string, string>("interaction_code", authCode));
            postData.Add(new KeyValuePair<string, string>("grant_type", "interaction_code"));
            postData.Add(new KeyValuePair<string, string>("code_verifier", codeVerifier));

            request.Content = new FormUrlEncodedContent(postData);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            // TODO: add resilence (e.g with Polly)
            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                tokenData = await response.Content.ReadAsStringAsync();
            }
            else
            {
                var message = "Error in the response from {Url}";
                Log.Error(message, url);
            }
        }

        return tokenData;
    }

    private async Task<string> ExchangeAuthCodeAsync(string authCode)
    {
        string tokenData = string.Empty;

        if (!this.AreOptionsValid())
        {
            return tokenData;
        }

        var url = $"{this.options.Authority}{this.options.TokenEndPoint}";
        using var httpClient = this.httpClientFactory.CreateClient();
        using (var request = new HttpRequestMessage(new HttpMethod("POST"), url))
        {
            var byteArray = Encoding.ASCII.GetBytes($"{this.options.ClientId}:{this.options.ClientSecret}");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            var postData = new List<KeyValuePair<string, string>>();
            postData.Add(new KeyValuePair<string, string>("redirect_uri", $"{this.PathBase}{this.options.CallbackPath}"));
            postData.Add(new KeyValuePair<string, string>("code", Uri.EscapeDataString(authCode)));
            postData.Add(new KeyValuePair<string, string>("grant_type", "authorization_code"));

            request.Content = new FormUrlEncodedContent(postData);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                tokenData = await response.Content.ReadAsStringAsync();
            }
            else
            {
                var message = "Error in the response from {Url}";
                Log.Error(message, url);
            }
        }

        return tokenData;
    }

    private bool AreOptionsValid()
    {
        if (string.IsNullOrEmpty(this.authCode) && string.IsNullOrEmpty(this.interactionCode))
        {
            Log.Error("Controller: {0} - Invalid authcode.  ExchangeCodeAsync will not be executed.", nameof(OktaAuthenticationHandler));
            Log.Error("Controller: {0} - Invalid interaction code.  ExchangeCodeAsync will not be executed.", nameof(OktaAuthenticationHandler));
            return false;
        }

        if (string.IsNullOrEmpty(this.options.Authority))
        {
            Log.Error("Controller: {0} - Invalid Authority.  ExchangeCodeAsync will not be executed.", nameof(OktaAuthenticationHandler));
            return false;
        }

        if (string.IsNullOrEmpty(this.options.ClientId))
        {
            Log.Error("Controller: {0} - Invalid ClientId.  ExchangeCodeAsync will not be executed.", nameof(OktaAuthenticationHandler));
            return false;
        }

        if (string.IsNullOrEmpty(this.options.ClientSecret))
        {
            Log.Error("Controller: {0} - Invalid ClientSecret.  ExchangeCodeAsync will not be executed.", nameof(OktaAuthenticationHandler));
            return false;
        }

        if (string.IsNullOrEmpty(this.options.TokenEndPoint))
        {
            Log.Error("Controller: {0} - Invalid TokenEndPoint.  ExchangeCodeAsync will not be executed.", nameof(OktaAuthenticationHandler));
            return false;
        }

        if (string.IsNullOrEmpty(this.options.CallbackPath))
        {
            Log.Error("Controller: {0} - Invalid CallbackPath.  ExchangeCodeAsync will not be executed.", nameof(OktaAuthenticationHandler));
            return false;
        }

        return true;
    }

    private ClaimsPrincipal AddUserClaims(OidcTokens tokens)
    {
        var principal = new ClaimsPrincipal();
        var claimsIdentity = new ClaimsIdentity(OktaAuthenticationOptions.Name);
        var tokenHandler = new JwtSecurityTokenHandler();
        var decodedToken = tokenHandler.ReadJwtToken(tokens.AccessToken);

        if (decodedToken != null)
        {
            var uid = decodedToken.Claims.FirstOrDefault(c => c.Type == "sub");

            if (uid != null)
            {
                claimsIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, uid.Value));
            }

            claimsIdentity.AddClaim(new Claim(ClaimTypes.Authentication, this.Scheme.Name));
        }

        principal.AddIdentity(claimsIdentity);

        if (this.Context.User != null)
        {
            this.Context.User = principal;
        }

        return principal;
    }

    private void StoreTokens(OidcTokens tokens)
    {
        var authTokens = new List<AuthenticationToken>();
        if (!string.IsNullOrEmpty(tokens.AccessToken))
        {
            authTokens.Add(new AuthenticationToken { Name = "access_token", Value = tokens.AccessToken });
        }

        if (!string.IsNullOrEmpty(tokens.IdToken))
        {
            authTokens.Add(new AuthenticationToken { Name = "id_token", Value = tokens.IdToken });
        }

        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            authTokens.Add(new AuthenticationToken { Name = "refresh_token", Value = tokens.RefreshToken });
        }

        if (!string.IsNullOrEmpty(tokens.TokenType))
        {
            authTokens.Add(new AuthenticationToken { Name = "token_type", Value = tokens.TokenType });
        }

        var expiresAt = this.clock.UtcNow + TimeSpan.FromSeconds(tokens.ExpiresIn);
        authTokens.Add(new AuthenticationToken
        {
            Name = "expires_at",
            Value = expiresAt.ToString("o", CultureInfo.InvariantCulture)
        });

        this.properties.StoreTokens(authTokens);
        this.properties.IsPersistent = true;
    }

    private void CreateLegacyReportsCookie()
    {
        var idToken = this?.properties?.GetTokenValue("id_token");

        if (string.IsNullOrEmpty(idToken))
        {
            Log.Error("Handler: {0} - Error creating Okta cookie for legacy reports.", nameof(OktaAuthenticationHandler));
            return;
        }

        var options = this.GetCookieOptions(false, true);
        this.Response.Cookies.Append(OktaAuthenticationOptions.Name, idToken, options);
    }

    private async Task<AuthenticationTicket> CreateTicketAsync(ClaimsPrincipal principal)
    {
        var ticket = new AuthenticationTicket(principal, this.properties, OktaAuthenticationOptions.Name);
        var context = new TicketReceivedContext(this.Context, this.Scheme, this.Options, ticket);

        await this.Events.TicketReceived(context);
        return ticket;
    }

    private CookieOptions GetCookieOptions(bool isExpired, bool domainOnly)
    {
        var domain = this.GetDomain();
        var options = new CookieOptions();

        options.HttpOnly = true;
        options.Secure = true;
        options.Domain = domainOnly ? domain : this.Request.Host.Host; 
        options.SameSite = SameSiteMode.None; 
        if (isExpired)
        {
            options.Expires = DateTime.Now.AddDays(-1);
        }

        return options;
    }

    private string GetDomain()
    {
        var host = this.Request.Host.Host;
        var segments = host.Split('.');

        if (segments.Length <= 2)
        {
            return host;
        }

        var domain = $".{segments[segments.Length - 2]}.{segments[segments.Length - 1]}";
        return domain;
    }
}
