namespace ZenithFin.EnableBanking
{
    class EnableBankingWorkspace : Workspace
    {
        public Authenticator authenticator;
        public EnableBankingWorkspace(string configPath) : base(configPath)
        {

            this.authenticator = new(this);
        }
    }
}
