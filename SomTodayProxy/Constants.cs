namespace SomtodayProxy;

public static class Constants
{
    public static string Version = "0.2.1";
        
    public static string MainPage = $@"SomToday App Proxy (STAP) is running!

Dankjewel Micha (https://argv.nl & https://github.com/FurriousFox) voor het vinden van de originele manier om mensen the authenticaten met SomToday!
Dit is een proxy die requests doorstuurt naar SomToday en de responses terugstuurt naar de client.
Op deze manier kunnen we de authenticatie van SomToday tegen hun gebruiken en de responses (voornamelijk de token) opvangen.
Ik zou persoonlijk nooit op deze manier zijn gekomen, dus nogmaals bedankt!

Wil je een login sessie aanvragen? Gebruik dan /requestUrl

Current version: {Version}";

    public static string MissingParametersMessage = @"Missing parameter(s)
- user=<username> this is the user you want to authenticate, this can be any value and is not used by STAP in any way, it is just a value i send back to you so you know who was authenticated
- callbackUrl=<callback url> this is the url where the token will be send to, this can be any url you want, it is not validated by STAP. The token will be send as a POST request with a JSON body

Check the docs: /Docs for more information";
    
    public static string BaseVanitUrl = "somtoday.kronk.tech/";
    
    public static int SessionDuration = 10; // in minutes
}