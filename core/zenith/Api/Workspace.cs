using ZenithFin.Utility;

namespace ZenithFin.Api
{
    
    class ZenithFinApiWorkspace : Workspace
    {
        public readonly ApiManager Manager;
        public ZenithFinApiWorkspace(string configPath) : base(configPath)
        {
            Manager = new ();
        }
    }
}
