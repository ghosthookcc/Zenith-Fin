using Microsoft.Extensions.Options;
using ZenithFin.Utility;

namespace ZenithFin.EnableBanking
{
    public class EnableBankingOptions
    {
        public string WorkspacePath { get; set; } = default!;
    }
    public class EnableBankingWorkspace : Workspace
    {
        internal Authenticator Authenticator;

        internal Client Client = new Client();

        public EnableBankingWorkspace(IOptions<EnableBankingOptions> options) : base(options.Value.WorkspacePath)
        {
            this.Authenticator = new Authenticator(this, Client);
        }
    }
}
