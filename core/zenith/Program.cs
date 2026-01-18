using ZenithFin.EnableBanking;
using ZenithFin.Api;

namespace ZenithFin
{
    class Program
    {
        static readonly EnableBankingWorkspace Enablebanking = new("EnableBanking/workspace.json");
        static readonly ZenithFinApiWorkspace ZenithFin = new("Api/workspace.json");
        static void Main(string[] args)
        {
            //await workspace.authenticator.Authenticate();
            ZenithFin.Manager.Start();
        }
    }
}