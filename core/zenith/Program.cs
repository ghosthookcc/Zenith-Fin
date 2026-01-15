using System.Text.Json;
using ZenithFin.EnableBanking;

namespace ZenithFin
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Client client = new ();
            EnableBankingWorkspace workspace = new("EnableBanking/config.json");
            await workspace.authenticator.Authenticate(client);
        }
    }
}