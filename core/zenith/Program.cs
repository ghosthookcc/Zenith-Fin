using ZenithFin.EnableBanking;
using ZenithFin.Api;

namespace ZenithFin
{
    class Program
    {
        static readonly EnableBankingWorkspace EnableBanking = new("EnableBanking/workspace.json");
        static readonly ZenithFinApiWorkspace ZenithFin = new("Api/workspace.json");
        static async Task Main(string[] args)
        {
            await ZenithFin.Manager.StartAsync();
        }
    }
}