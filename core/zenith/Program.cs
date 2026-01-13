using GoCardless;
using System.Configuration;

var accessToken = ;
var gocardless = GoCardlessClient.Create(accessToken, 
                                         GoCardlessClient.Environment.LIVE);