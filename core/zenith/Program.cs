using ZenithFin.Api;

namespace ZenithFin
{
    class Program
    {
        static readonly ZenithFinApiWorkspace ZenithFin = new("Api/workspace.json");
        static async Task Main(string[] args)
        {
            await ZenithFin.Manager.StartAsync();
        }
    }
}