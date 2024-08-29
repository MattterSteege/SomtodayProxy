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
        var keyValuePairs = body.Split('&')
            .Select(part => part.Split('='))
            .ToDictionary(split => split[0], split => split[1]);

        SomtodayAuthenticatieModel model = new()
        {
            grant_type = keyValuePairs["grant_type"],
            code = keyValuePairs["code"],
            redirect_uri = keyValuePairs["redirect_uri"],
            code_verifier = keyValuePairs["code_verifier"],
            client_id = keyValuePairs["client_id"],
            claims = keyValuePairs["claims"]
        };


        var sessionManager = context.RequestServices.GetRequiredService<SessionManager>();
        var session = sessionManager.GetSession(Constants.BaseVanitUrl + vanityUrl);

        if (session != null)
        {
            model.vanityUrl = session.VanityUrl;
            model.callbackUrl = session.CallbackUrl;
            model.user = session.User;
            model.expires = session.Expires;

            sessionManager.RemoveSession(session.VanityUrl);

            // Send the model to the callbackUrl
            var client = new HttpClient();
            var content = new StringContent(JsonSerializer.Serialize(model), Encoding.UTF8, "application/json");
            await client.PostAsync(session.CallbackUrl, content);
        }
        else
        {
            //send the model to the callbackUrl
            var client2 = new HttpClient();
            var content2 =
                new StringContent(
                    "{\"error\": \"Failed to authenticate user, you can discard the login attempt at you side, I sure as hell deleted it on my side\"}");
            await client2.PostAsync(model.callbackUrl, content2);
        }

        model.code = "";

        context.Response.StatusCode = 200;
        await context.Response.WriteAsync(JsonSerializer.Serialize(model));
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
        var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
        var response = await httpClient.GetAsync("https://inloggen.somtoday.nl/.well-known/openid-configuration");
        var responseBody = await response.Content.ReadAsStringAsync();
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(responseBody);
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
            context.Request.Query["callbackUrl"]
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