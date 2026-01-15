using System.Text.Json;

namespace ZenithFin
{
    class Workspace
    {
        public Dictionary<string, string>? config = new();
        public Workspace(string configPath)
        {
            config = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configPath));
        }
    }
}
