namespace ZenithFin.EnableBanking
{
    class EnableBankingWorkspace : Workspace
    {
        public Authenticator authenticator;

        private Client _client = new Client();

        public EnableBankingWorkspace(string configPath) : base(configPath)
        {
            this.authenticator = new(this, _client);
        }
    }
}
