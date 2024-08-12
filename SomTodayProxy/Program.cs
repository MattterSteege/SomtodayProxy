using System.Net.Http.Headers;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text;

namespace SomTodayProxy
{
    
    /*
        Dankjewel Micha (micha.ga & https://github.com/FurriousFox) voor het vinden van de originele manier om mensen the authenticaten met SomToday!
        Dit is een proxy die requests doorstuurt naar SomToday en de responses terugstuurt naar de client.
        Op deze manier kunnen we de authenticatie van SomToday tegen hun gebruiken en de responses (voornamelijk de token) opvangen.
        Ik zou persoonlijk nooit op deze manier zijn gekomen, dus nogmaals bedankt!
     */
    
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddHttpsRedirection(options => options.HttpsPort = 443);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedProto
            });
            
            app.UseRouting();
            app.UseEndpoints(ConfigureEndpoints);
        }

        private void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/", async context => await context.Response.WriteAsync("SomToday Proxy is running!\n\nDankjewel Micha (https://micha.ga & https://github.com/FurriousFox) voor het vinden van de originele manier om mensen the authenticaten met SomToday!\nDit is een proxy die requests doorstuurt naar SomToday en de responses terugstuurt naar de client.\nOp deze manier kunnen we de authenticatie van SomToday tegen hun gebruiken en de responses (voornamelijk de token) opvangen.\nIk zou persoonlijk nooit op deze manier zijn gekomen, dus nogmaals bedankt!"));
            endpoints.MapGet("/.well-known/openid-configuration", HandleOpenIdConfiguration);
            endpoints.MapFallback(HandleProxyRequest);
        }

        private async Task HandleOpenIdConfiguration(HttpContext context)
        {
            var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
            var response = await httpClient.GetAsync("https://inloggen.somtoday.nl/.well-known/openid-configuration");
            var responseBody = await response.Content.ReadAsStringAsync();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(responseBody);
        }

        private async Task HandleProxyRequest(HttpContext context)
        {
            var request = context.Request;
            var targetHost = request.Path.StartsWithSegments("/rest/") ? "api.somtoday.nl" : "inloggen.somtoday.nl";
            var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();

            var proxyRequest = CreateProxyRequest(request, targetHost);

            if (request.ContentLength > 0)
            {
                await HandleRequestWithBody(context, request, proxyRequest);
                if (request.Path == "/oauth2/token")
                {
                    return; // Early return for token requests
                }
            }
            else if (request.Headers.ContainsKey("Authorization"))
            {
                Console.WriteLine(request.Headers["Authorization"].ToString());
            }

            if (request.Path == "/oauth2/authorize")
            {
                context.Response.Redirect($"https://{targetHost}{request.Path}{request.QueryString}");
                return;
            }

            await SendProxyRequestAndHandleResponse(context, httpClient, proxyRequest);
        }

        private HttpRequestMessage CreateProxyRequest(HttpRequest request, string targetHost)
        {
            var proxyRequest = new HttpRequestMessage(new HttpMethod(request.Method), $"https://{targetHost}{request.Path}{request.QueryString}");
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
            targetHeaders.Add("x-forwarded-host", sourceHeaders["x-forwarded-host"].ToString() ?? sourceHeaders["Host"].ToString());
            targetHeaders.Add("x-forwarded-server", sourceHeaders["x-forwarded-host"].ToString() ?? sourceHeaders["Host"].ToString());
        }

        private async Task HandleRequestWithBody(HttpContext context, HttpRequest request, HttpRequestMessage proxyRequest)
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            proxyRequest.Content = new StringContent(body, Encoding.UTF8, request.Headers["Content-Type"].ToString());

            if (request.Path == "/oauth2/token")
            {
                await HandleTokenRequest(context, body);
            }
        }

        private async Task HandleTokenRequest(HttpContext context, string body)
        {
            var keyValuePairs = body.Split('&')
                .Select(part => part.Split('='))
                .ToDictionary(split => split[0], split => split[1]);

            foreach (var kvp in keyValuePairs)
            {
                Console.WriteLine(System.Web.HttpUtility.UrlDecode($"{kvp.Key}: {kvp.Value}"));
            }

            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("{\"Success!\":\"Je bent met succes ingelogd bij Zermos, lekker bezig!\"}");
        }

        private async Task SendProxyRequestAndHandleResponse(HttpContext context, HttpClient httpClient, HttpRequestMessage proxyRequest)
        {
            var response = await httpClient.SendAsync(proxyRequest);
            context.Response.StatusCode = (int)response.StatusCode;

            CopyResponseHeaders(response.Headers, context.Response.Headers);
            SetCorsHeaders(context.Response.Headers);

            if (context.Request.Path == "/.well-known/openid-configuration")
            {
                await ServeOpenIdConfiguration(context);
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                await context.Response.WriteAsync(responseBody);
            }
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
            string content = await File.ReadAllTextAsync("wwwroot/.well-known/openid-configuration.json");
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(content);
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseUrls("https://192.168.178.22:5001", "http://192.168.178.22:5000");
                });
    }
}