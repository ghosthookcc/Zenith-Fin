using System.Text;
using System.Text.Json;

namespace ZenithFin.Utility
{
    public sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            StringBuilder output = new (name.Length + 10);

            for (int idx = 0; idx < name.Length; idx++)
            {
                char character = name[idx];
                if (char.IsUpper(character))
                {
                    if (idx > 0 && !char.IsUpper(name[idx - 1]))
                        output.Append('_');
                    output.Append(char.ToLowerInvariant(character));
                }
                else
                {
                    output.Append(character);
                }
            }
            return output.ToString();
        }
    }
}
