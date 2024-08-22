using System.Net.Http.Headers;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text;
using System.Text.Json;
using System.Web;

namespace SomtodayProxy
{
    
    /*
        Dankjewel Micha (argv.nl & https://github.com/FurriousFox) voor het vinden van de originele manier om mensen the authenticaten met SomToday!
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
            services.AddSingleton<SessionManager>();
            services.AddHostedService(sp => sp.GetRequiredService<SessionManager>());
            services.AddSingleton<ProxyHandler>();
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
            endpoints.MapGet("/", async context => await context.Response.WriteAsync(Constants.MainPage + "\n\n" + context.RequestServices.GetRequiredService<SessionManager>().GetSessionCount() + " active sessions"));
            endpoints.MapGet("/requestUrl", RequestLoginUrl);
            endpoints.MapGet("/docs", RedirectToDocs);
            endpoints.MapGet("/{vanityUrl}/oauth2/logout", HandleLogoutRequest);
            endpoints.MapFallback(HandleProxyRequest);
        }

        private async Task RequestLoginUrl(HttpContext context)
        {
            var proxyHandler = context.RequestServices.GetRequiredService<ProxyHandler>();
            await proxyHandler.RequestLoginUrl(context);
        }
        
        private async Task RedirectToDocs(HttpContext context)
        {
            context.Response.Redirect("https://github.com/matttersteege/SomtodayProxy");
        }

        private async Task HandleProxyRequest(HttpContext context)
        {
            var proxyHandler = context.RequestServices.GetRequiredService<ProxyHandler>();
            await proxyHandler.HandleProxyRequest(context);
        }
        
        private async Task HandleLogoutRequest(HttpContext context)
        {
            var proxyHandler = context.RequestServices.GetRequiredService<ProxyHandler>();
            await proxyHandler.HandleLogoutRequest(context);
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
        });
    }
}