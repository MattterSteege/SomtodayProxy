using System.Net.Http.Headers;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text;
using System.Text.Json;
using System.Web;

namespace SomTodayProxy
{
    
    /*
        Dankjewel Micha (micha.ga & https://github.com/FurriousFox) voor het vinden van de originele manier om mensen the authenticaten met SomToday!
        Dit is een proxy die requests doorstuurt naar SomToday en de responses terugstuurt naar de client.
        Op deze manier kunnen we de authenticatie van SomToday tegen hun gebruiken en de responses (voornamelijk de token) opvangen.
        Ik zou persoonlijk nooit op deze manier zijn gekomen, dus nogmaals bedankt!
     */
    
    /*
     
     
     
     */
    
    public class Startup
    {
        //keep a list of every current user trying to authenticate
        List<UserAuthenticatingModel> users = new List<UserAuthenticatingModel>();

        private string mainPage = "SomToday Proxy is running!\n" +
                                  "\n" +
                                  "Dankjewel Micha (https://micha.ga & https://github.com/FurriousFox) voor het vinden van de originele manier om mensen the authenticaten met SomToday!\n" +
                                  "Dit is een proxy die requests doorstuurt naar SomToday en de responses terugstuurt naar de client.\n" +
                                  "Op deze manier kunnen we de authenticatie van SomToday tegen hun gebruiken en de responses (voornamelijk de token) opvangen.\n" +
                                  "Ik zou persoonlijk nooit op deze manier zijn gekomen, dus nogmaals bedankt!\n" +
                                  "\n" +
                                  "Wil je een login sessie aanvragen? Gebruik dan /requestUrl?user=gebruiker1234&callbackUrl=https://example.com/callback\n" +
                                  "Documentatie komt spoedig!";
            
                          
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
            endpoints.MapGet("/requestUrl", RequestLoginUrl);
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
        
        private async Task RequestLoginUrl(HttpContext context)
        {
            if (!context.Request.Query.ContainsKey("user") || !context.Request.Query.ContainsKey("callbackUrl"))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Missing parameters user= and/or callbackUrl=");
                return;
            }
            
            UserAuthenticatingModel model = new UserAuthenticatingModel();
            model.user = context.Request.Query["user"];
            //4 random numbers (0000 - 9999) + .somtoday.kronk.tech and check if it's available
            model.vanityUrl = "somtoday.kronk.tech/" + new Random().Next(0, 9999).ToString("D4");

            //check if the url is already in use
            while (users.Any(u => u.vanityUrl == model.vanityUrl))
            {
                model.vanityUrl = "somtoday.kronk.tech/" + new Random().Next(0, 9999).ToString("D4");
            }
            
            model.expires = DateTime.Now.AddMinutes(5);
            model.callbackUrl = context.Request.Query["callbackUrl"];
            
            //add the user to the list
            users.Add(model);
            
            //return the model
            await context.Response.WriteAsync(JsonSerializer.Serialize(model));
        }

        private async Task HandleProxyRequest(HttpContext context)
        {
            var request = context.Request;
            string vanityUrl = request.Path.Value?.Substring(1, 4) ?? string.Empty;
            context.Request.Path = request.Path.Value?[5..];
            
            var targetHost = request.Path.StartsWithSegments("/rest/") ? "api.somtoday.nl" : "inloggen.somtoday.nl";
            var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();

            var proxyRequest = CreateProxyRequest(request, targetHost);

            if (request.ContentLength > 0)
            {
                await HandleRequestWithBody(context, request, proxyRequest, vanityUrl);
            }
            else if (request.Headers.ContainsKey("Authorization"))
            {
                Console.WriteLine(request.Headers["Authorization"].ToString());
                proxyRequest.Headers.Authorization = AuthenticationHeaderValue.Parse(request.Headers["Authorization"].ToString());
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
            //targetHeaders.Add("x-forwarded-host", sourceHeaders["x-forwarded-host"].ToString());
            //targetHeaders.Add("x-forwarded-server", sourceHeaders["x-forwarded-host"].ToString());
        }

        private async Task HandleRequestWithBody(HttpContext context, HttpRequest request, HttpRequestMessage proxyRequest, string vanityUrl)
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            proxyRequest.Content = new StringContent(body, Encoding.UTF8);
            proxyRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.Headers["Content-Type"].ToString());

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


            //get the loginRequestModel from the list with the vanityUrl
            var loginRequestModel = users.FirstOrDefault(u => u.vanityUrl == "somtoday.kronk.tech/" + vanityUrl);
            if (loginRequestModel != null)
            {
                //remove the user from the list
                users.Remove(loginRequestModel);
                
                //send the model to the callbackUrl
                var client = new HttpClient();
                var content = new StringContent(JsonSerializer.Serialize(model), Encoding.UTF8, "application/json");
                await client.PostAsync(loginRequestModel.callbackUrl, content);
            }
            
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("{\"ok\": true}");
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
            webBuilder.UseUrls("https://localhost:5001", "http://localhost:5000");
            webBuilder.UseUrls("https://192.168.178.22:5001", "http://192.168.178.22:5000");
        });
    }
    
    public class SomtodayAuthenticatieModel
    {
        public string grant_type { get; set; }
        public string code { get; set; }
        public string redirect_uri { get; set; }
        public string code_verifier { get; set; }
        public string client_id { get; set; }
        public string claims { get; set; }
    }
    
    public class UserAuthenticatingModel
    {
        public string user { get; set; }
        public string vanityUrl { get; set; }
        public DateTime expires { get; set; }
        public string callbackUrl { get; set; }
    }
}