using System.Text.Json;

namespace ZenithFin
{
    class Workspace
    {
        public JsonDocument? config;
        public Workspace(string configPath)
        {
            var jsonText = File.ReadAllText(configPath);
            config = JsonDocument.Parse(jsonText);
        }
    }
}
