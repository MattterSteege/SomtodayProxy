namespace SomtodayProxy;

public class SomtodayAuthenticatieModel
{
    public string grant_type { get; set; }
    public string code { get; set; }
    public string redirect_uri { get; set; }
    public string code_verifier { get; set; }
    public string client_id { get; set; }
    public string claims { get; set; }
    
    // Spoonfeed
    public string access_token { get; set; }
    public string refresh_token { get; set; }
    public string somtoday_api_url { get; set; }
    public string somtoday_oop_url { get; set; }
    public string scope { get; set; }
    public string somtoday_organisatie_afkorting { get; set; }
    public string id_token { get; set; }
    public string token_type { get; set; }
    public int expires_in { get; set; }
        
    // Sent by client
    public string user { get; set; }
    public string vanityUrl { get; set; }
    public DateTime expires { get; set; }
    public string callbackUrl { get; set; }
}
    
public class UserAuthenticatingModel
{
    public string User { get; set; }
    public string VanityUrl { get; set; }
    public DateTime Expires { get; set; }
    public string CallbackUrl { get; set; }
}