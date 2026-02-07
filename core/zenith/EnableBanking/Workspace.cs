using ZenithFin.Utility;

namespace ZenithFin.EnableBanking
{
    public class EnableBankingWorkspace : Workspace
    {
        internal Authenticator Authenticator;

        internal Client Client = new Client();

        public EnableBankingWorkspace(string configPath) : base(configPath)
        {
            this.Authenticator = new(this, Client);
        }
    }
}
