namespace SomtodayProxy;

public class SomtodayAuthenticatieModel
{
    public string grant_type { get; set; }
    public string code { get; set; }
    public string redirect_uri { get; set; }
    public string code_verifier { get; set; }
    public string client_id { get; set; }
    public string claims { get; set; }
        
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