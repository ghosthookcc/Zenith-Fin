using GoCardless;
using System.Configuration;

string? accessToken = ConfigurationManager.AppSettings["GoCardlessAccessToken"];
GoCardlessClient gocardless = GoCardlessClient.Create(accessToken, 
                                         GoCardlessClient.Environment.LIVE);