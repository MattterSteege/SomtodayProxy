using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace SomtodayProxy;

public class ProxyHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SessionManager _sessionManager;

    public ProxyHandler(IHttpClientFactory httpClientFactory, SessionManager sessionManager)
    {
        _httpClientFactory = httpClientFactory;
        _sessionManager = sessionManager;
    }

    public async Task HandleProxyRequest(HttpContext context)
    {
        var request = context.Request;
        string vanityUrl = request.Path.Value?.Substring(1, 4) ?? string.Empty;
        context.Request.Path = request.Path.Value?[5..];

        if (context.Request.Path == "")
        {
            await HandleErrorPage(context);
            return;
        }

        var targetHost = request.Path.StartsWithSegments("/rest/") ? "api.somtoday.nl" : "inloggen.somtoday.nl";
        var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();

        var proxyRequest = CreateProxyRequest(request, targetHost);

        if (request.ContentLength > 0)
        {
            await HandleRequestWithBody(context, request, proxyRequest, vanityUrl);
            return;
        }
        else if (request.Headers.ContainsKey("Authorization"))
        {
            Console.WriteLine(request.Headers["Authorization"].ToString());
            proxyRequest.Headers.Authorization =
                AuthenticationHeaderValue.Parse(request.Headers["Authorization"].ToString());
        }

        if (request.Path == "/oauth2/authorize")
        {
            context.Response.Redirect($"https://{targetHost}{request.Path}{request.QueryString}");
            return;
        }

        if (request.Path == "/.well-known/openid-configuration")
        {
            await ServeOpenIdConfiguration(context);
            return;
        }

        await SendProxyRequestAndHandleResponse(context, httpClient, proxyRequest);
    }

    private HttpRequestMessage CreateProxyRequest(HttpRequest request, string targetHost)
    {
        var proxyRequest = new HttpRequestMessage(new HttpMethod(request.Method),
            $"https://{targetHost}{request.Path}{request.QueryString}");
        CopyHeaders(request.Headers, proxyRequest.Headers, targetHost);
        return proxyRequest;
    }

    private void CopyHeaders(IHeaderDictionary sourceHeaders, HttpRequestHeaders targetHeaders, string targetHost)
    {
        foreach (var header in sourceHeaders)
        {
            if (header.Key.ToLower() != "host")
            {
                targetHeaders.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        targetHeaders.Host = targetHost;
        //targetHeaders.Add("x-forwarded-host", sourceHeaders["x-forwarded-host"].ToString());
        //targetHeaders.Add("x-forwarded-server", sourceHeaders["x-forwarded-host"].ToString());
    }

    private async Task HandleRequestWithBody(HttpContext context, HttpRequest request, HttpRequestMessage proxyRequest,
        string vanityUrl)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        if (request.Path != "/oauth2/token")
        {
            proxyRequest.Content = new StringContent(body, Encoding.UTF8);
            proxyRequest.Content.Headers.ContentType =
                MediaTypeHeaderValue.Parse(request.Headers["Content-Type"].ToString());
        }

        if (request.Path == "/oauth2/token")
        {
            await HandleTokenRequest(context, body, vanityUrl);
        }
    }

    private async Task HandleTokenRequest(HttpContext context, string body, string vanityUrl)
    {
        var model = ParseRequestBody(body);
        var sessionManager = context.RequestServices.GetRequiredService<SessionManager>();
        var session = sessionManager.GetSession(Constants.BaseVanitUrl + vanityUrl);

        if (session == null)
        {
            await SendErrorResponse(model.callbackUrl, "Failed to authenticate user. Login attempt has been discarded.");
            await SendResponseToClient(context, model);
            return;
        }

        UpdateModelWithSessionData(model, session);
        sessionManager.RemoveSession(session.VanityUrl);

        if (session.SpoonFeed)
        {
            await HandleSpoonFeedScenario(model, session);
            return;
        }

        await SendModelToCallback(session.CallbackUrl, model);
        await SendResponseToClient(context, model);
    }

    private SomtodayAuthenticatieModel ParseRequestBody(string body)
    {
        var keyValuePairs = body.Split('&')
            .Select(part => part.Split('='))
            .ToDictionary(split => split[0], split => split[1]);

        return new SomtodayAuthenticatieModel
        {
            grant_type = keyValuePairs["grant_type"],
            code = keyValuePairs["code"],
            redirect_uri = HttpUtility.UrlDecode(keyValuePairs["redirect_uri"]),
            code_verifier = keyValuePairs["code_verifier"],
            client_id = keyValuePairs["client_id"],
            claims = HttpUtility.UrlDecode(keyValuePairs["claims"])
        };
    }

    private void UpdateModelWithSessionData(SomtodayAuthenticatieModel model, UserSession session)
    {
        model.vanityUrl = session.VanityUrl;
        model.callbackUrl = session.CallbackUrl;
        model.user = session.User;
        model.expires = session.Expires;
    }

    private async Task HandleSpoonFeedScenario(SomtodayAuthenticatieModel model, UserSession session)
    {
        using var spoonFeedClient = _httpClientFactory.CreateClient();
        var tokenResponse = await RequestSomtodayToken(spoonFeedClient, model);

        if (!tokenResponse.IsSuccessStatusCode)
        {
            await SendErrorResponse(model.callbackUrl, "Could not authenticate user. Spoonfeeding failed.");
            return;
        }

        var somtodayAuthentication = await DeserializeResponse<SomtodayAuthenticatieModel>(tokenResponse);
        model.access_token = somtodayAuthentication.access_token;
        model.refresh_token = somtodayAuthentication.refresh_token;
        model.somtoday_api_url = somtodayAuthentication.somtoday_api_url;
        model.somtoday_oop_url = somtodayAuthentication.somtoday_oop_url;
        model.scope = somtodayAuthentication.scope;
        model.somtoday_organisatie_afkorting = somtodayAuthentication.somtoday_organisatie_afkorting;
        model.id_token = somtodayAuthentication.id_token;
        model.token_type = somtodayAuthentication.token_type;
        model.expires_in = somtodayAuthentication.expires_in;
        
        //remove used properties
        model.code = null;
        model.code_verifier = null;
        
        if (model.access_token == null)
        {
            await SendErrorResponse(model.callbackUrl, "Could not authenticate user. Spoonfeeding failed.");
            return;
        }

        await SendModelToCallback(session.CallbackUrl, model);
    }

    private async Task<HttpResponseMessage> RequestSomtodayToken(HttpClient client, SomtodayAuthenticatieModel model)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://inloggen.somtoday.nl/oauth2/token");
        request.Headers.Add("accept", "application/json, text/plain, */*");
        request.Headers.Add("origin", "https://leerling.somtoday.nl");

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", model.code),
            new KeyValuePair<string, string>("redirect_uri", "somtoday://nl.topicus.somtoday.leerling/oauth/callback"),
            new KeyValuePair<string, string>("code_verifier", model.code_verifier),
            new KeyValuePair<string, string>("client_id", "somtoday-leerling-native"),
            new KeyValuePair<string, string>("claims", "{\"id_token\":{\"given_name\":null, \"leerlingen\":null, \"orgname\": null, \"affiliation\":{\"values\":[\"student\",\"parent/guardian\"]} }}")
        });

        request.Content = content;
        return await client.SendAsync(request);
    }
    
    
    //SEND RESPONSES
    private async Task SendModelToCallback(string callbackUrl, SomtodayAuthenticatieModel model)
    {
        using var client = new HttpClient();
        var content = new StringContent(JsonSerializer.Serialize(model), Encoding.UTF8, "application/json");
        await client.PostAsync(callbackUrl, content);
    }

    private async Task SendErrorResponse(string callbackUrl, string errorMessage)
    {
        using var client = new HttpClient();
        var content = new StringContent($"{{\"error\": \"{errorMessage}\"}}", Encoding.UTF8, "application/json");
        await client.PostAsync(callbackUrl, content);
    }

    private async Task SendResponseToClient(HttpContext context, SomtodayAuthenticatieModel model)
    {
        model.code = "";
        context.Response.StatusCode = 200;
        await context.Response.WriteAsync(JsonSerializer.Serialize(model));
    }

    
    
    
    private async Task<T?> DeserializeResponse<T>(HttpResponseMessage response)
    {
        var responseString = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(responseString);
    }

    private async Task SendProxyRequestAndHandleResponse(HttpContext context, HttpClient httpClient,
        HttpRequestMessage proxyRequest)
    {
        var response = await httpClient.SendAsync(proxyRequest);
        context.Response.StatusCode = (int) response.StatusCode;

        CopyResponseHeaders(response.Headers, context.Response.Headers);
        SetCorsHeaders(context.Response.Headers);

        var responseBody = await response.Content.ReadAsStringAsync();
        await context.Response.WriteAsync(responseBody);
    }

    private void CopyResponseHeaders(HttpResponseHeaders sourceHeaders, IHeaderDictionary targetHeaders)
    {
        foreach (var header in sourceHeaders)
        {
            targetHeaders[header.Key] = header.Value.ToArray();
        }
    }

    private void SetCorsHeaders(IHeaderDictionary headers)
    {
        headers.Remove("Access-Control-Allow-Origin");
        headers.Remove("Access-Control-Allow-Headers");
        headers.Remove("Access-Control-Allow-Methods");
        headers.Remove("Access-Control-Allow-Credentials");
        headers.Remove("Access-Control-Expose-Headers");

        headers.Add("Access-Control-Allow-Origin", "*");
        headers.Add("Access-Control-Allow-Methods", "*");
        headers.Add("Access-Control-Allow-Headers", "Authorization,*");
        headers.Add("Access-Control-Allow-Credentials", "true");
        headers.Add("Access-Control-Expose-Headers", "*");
    }

    private async Task ServeOpenIdConfiguration(HttpContext context)
    {
        //return JSON string
        //return JSON header and 200
        context.Response.StatusCode = 200;
        context.Response.Headers.Add("Content-Type", "application/json");
        await context.Response.WriteAsync("{\n  \"authorization_endpoint\": \"https://inloggen.somtoday.nl/oauth2/authorize\",\n  \"token_endpoint\": \"https://inloggen.somtoday.nl/oauth2/token\",\n  \"introspection_endpoint\": \"https://inloggen.somtoday.nl/oauth2/introspect\",\n  \"revocation_endpoint\": \"https://inloggen.somtoday.nl/oauth2/revoke\",\n  \"issuer\": \"https://somtoday.nl\",\n  \"jwks_uri\": \"https://inloggen.somtoday.nl/oauth2/jwks.json\",\n  \"scopes_supported\": [\n    \"openid\",\n    \"address\",\n    \"email\",\n    \"phone\",\n    \"profile\"\n  ],\n  \"response_types_supported\": [\n    \"code\",\n    \"id_token\",\n    \"code token\",\n    \"code id_token\",\n    \"id_token token\",\n    \"code id_token token\"\n  ],\n  \"response_modes_supported\": [\n    \"fragment\",\n    \"query\",\n    \"form_post\"\n  ],\n  \"grant_types_supported\": [\n    \"authorization_code\",\n    \"client_credentials\",\n    \"implicit\",\n    \"password\",\n    \"refresh_token\",\n    \"urn:ietf:params:oauth:grant-type:token-exchange\",\n    \"urn:ietf:params:oauth:grant-type:jwt-bearer\"\n  ],\n  \"code_challenge_methods_supported\": [\n    \"plain\",\n    \"S256\"\n  ],\n  \"token_endpoint_auth_methods_supported\": [\n    \"client_secret_basic\",\n    \"client_secret_post\"\n  ],\n  \"introspection_endpoint_auth_methods_supported\": [\n    \"client_secret_basic\",\n    \"client_secret_post\"\n  ],\n  \"revocation_endpoint_auth_methods_supported\": [\n    \"client_secret_basic\",\n    \"client_secret_post\"\n  ],\n  \"request_object_signing_alg_values_supported\": [\n    \"none\"\n  ],\n  \"ui_locales_supported\": [\n    \"nl-NL\"\n  ],\n  \"request_parameter_supported\": true,\n  \"request_uri_parameter_supported\": true,\n  \"authorization_response_iss_parameter_supported\": true,\n  \"subject_types_supported\": [\n    \"public\"\n  ],\n  \"userinfo_endpoint\": \"https://inloggen.somtoday.nl/oauth2/userinfo\",\n  \"end_session_endpoint\": \"https://inloggen.somtoday.nl/oauth2/logout\",\n  \"id_token_signing_alg_values_supported\": [\n    \"RS256\"\n  ],\n  \"userinfo_signing_alg_values_supported\": [\n    \"RS256\"\n  ],\n  \"display_values_supported\": [\n    \"page\"\n  ],\n  \"claim_types_supported\": [\n    \"normal\"\n  ],\n  \"claims_supported\": [\n    \"sub\",\n    \"name\",\n    \"given_name\",\n    \"family_name\",\n    \"middle_name\",\n    \"nickname\",\n    \"preferred_username\",\n    \"picture\",\n    \"email\",\n    \"email_verified\",\n    \"gender\",\n    \"birthdate\",\n    \"zoneinfo\",\n    \"locale\",\n    \"phone_number\",\n    \"phone_number_verified\",\n    \"address\",\n    \"updated_at\"\n  ],\n  \"claims_parameter_supported\": true\n}");
    }
    
    private async Task HandleErrorPage(HttpContext context)
    {
        context.Response.StatusCode = 302;
        context.Response.Headers.Add("Location", "somtoday://nl.topicus.somtoday.leerling/oauth/callback");
        await context.Response.WriteAsync("Hmm, this is not right, you didn't follow the instructions, did you?");
    }
    
    public async Task RequestLoginUrl(HttpContext context)
    {
        if (!context.Request.Query.ContainsKey("user") || !context.Request.Query.ContainsKey("callbackUrl"))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(Constants.MissingParametersMessage);
            return;
        }
    
        var session = _sessionManager.CreateSession(
            context.Request.Query["user"],
            context.Request.Query["callbackUrl"],
            context.Request.Query["spoonfeed"] == "true"
        );
        
        if (session == null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(Constants.MissingParametersMessage);
            return;
        }

        await context.Response.WriteAsJsonAsync(new
        {
            session.User,
            session.VanityUrl,
            session.Expires,
            session.CallbackUrl
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
    }

    public async Task HandleLogoutRequest(HttpContext context)
    {
        var vanityUrl = context.Request.Path.Value?.Substring(1, 4) ?? string.Empty;
        _sessionManager.RemoveSession(vanityUrl);
        
        context.Response.StatusCode = 302;
        
        //get the ?post_logout_redirect_uri= from the query string
        var query = context.Request.QueryString.Value;
        var queryDict = HttpUtility.ParseQueryString(query);
        var postLogoutRedirectUri = queryDict["post_logout_redirect_uri"];
        
        if (postLogoutRedirectUri != null)
            context.Response.Headers.Add("Location", postLogoutRedirectUri);
        else
            context.Response.Headers.Add("Location", "somtoday://nl.topicus.somtoday.leerling/oauth/logout");
    }
}