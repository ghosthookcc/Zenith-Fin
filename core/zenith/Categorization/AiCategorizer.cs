using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using ZenithFin.Utility;

namespace ZenithFin.Categorization
{
    public sealed record AiCategory(long TagId,
                                    string Name,
                                    string Kind,
                                    string? Key,
                                    IReadOnlyList<string> Keywords);

    public sealed record AiTransaction(long Id,
                                       string? CounterpartyName,
                                       IReadOnlyList<string> RemittanceInformation,
                                       string? BankTransactionCode,
                                       string CreditDebitIndicator,
                                       decimal Amount,
                                       string Currency);

    public sealed record AiItem(string Text,
                                IReadOnlyList<long> TransactionIds);

    public sealed record AiPrompt(string Instructions,
                                  string Input,
                                  JsonObject Schema);

    public static class AiCategorizer
    {
        public const string SchemaName = "transaction_categories";

        private static readonly Regex LongNumber = new(@"\d{5,}", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> BuiltInDescriptions = new()
        {
            [CategoryRules.Groceries] = "supermarkets, convenience stores, kiosks and Systembolaget",
            [CategoryRules.Dining] = "restaurants, cafés, bars, fast food, takeaway and food delivery",
            [CategoryRules.Transport] = "public transport, taxi, fuel, parking, tolls and car costs",
            [CategoryRules.Housing] = "rent, housing association fees and mortgage costs",
            [CategoryRules.Bills] = "electricity, phone, internet, insurance, union fees, loans and credit card bills",
            [CategoryRules.Shopping] = "clothes, electronics, home, garden, gifts and online shops",
            [CategoryRules.Entertainment] = "games, streaming, cinema, events, sports and hobbies",
            [CategoryRules.Health] = "pharmacy, healthcare, dentist, opticians and gym",
            [CategoryRules.Travel] = "flights, hotels and spending abroad",
            [CategoryRules.Savings] = "money sent to savings, funds or investment accounts",
            [CategoryRules.Fees] = "bank fees, taxes and fines",
            [CategoryRules.Income] = "salary, benefits, interest and refunds from authorities",
            [CategoryRules.Transfers] = "money moved between the person's own accounts",
            [CategoryRules.Other] = "anything that does not clearly fit another category"
        };

        public static IReadOnlyList<AiItem> Group(IEnumerable<AiTransaction> transactions)
        {
            return transactions.GroupBy(Describe)
                               .Select(group =>
                               {
                                   AiTransaction first = group.First();
                                   string amount = Math.Abs(first.Amount).ToString("0.##", CultureInfo.InvariantCulture);

                                   return new AiItem($"{group.Key} | {amount} {first.Currency}",
                                                     group.Select(transaction => transaction.Id).ToArray());
                               })
                               .ToArray();
        }

        public static AiPrompt BuildPrompt(IReadOnlyList<AiItem> items,
                                           IReadOnlyList<AiCategory> categories)
        {
            StringBuilder instructions = new();

            instructions.AppendLine("You sort a Swedish person's bank transactions into budget categories.");
            instructions.AppendLine("For every numbered transaction, pick exactly one category from the list, using its exact name.");
            instructions.AppendLine("Card purchases show the merchant. Swish payments and transfers between people often carry a message that says what the money was for.");
            instructions.AppendLine("Choose Other when no category clearly fits, rather than guessing.");
            instructions.AppendLine();
            instructions.AppendLine("Categories:");

            foreach (AiCategory category in categories)
                instructions.AppendLine($"- {category.Name}: {DescribeCategory(category)}");

            StringBuilder input = new();

            for (int index = 0; index < items.Count; index++)
                input.AppendLine($"{index + 1}. {items[index].Text}");

            JsonArray names = new(categories.Select(category => (JsonNode?)JsonValue.Create(category.Name)).ToArray());

            JsonObject schema = new()
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["results"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["transaction"] = new JsonObject { ["type"] = "integer" },
                                ["category"] = new JsonObject { ["type"] = "string", ["enum"] = names }
                            },
                            ["required"] = new JsonArray("transaction", "category"),
                            ["additionalProperties"] = false
                        }
                    }
                },
                ["required"] = new JsonArray("results"),
                ["additionalProperties"] = false
            };

            return new AiPrompt(instructions.ToString(), input.ToString(), schema);
        }

        public static Dictionary<int, string> ParseChoices(string json,
                                                           int itemCount)
        {
            Dictionary<int, string> choices = new();

            using JsonDocument document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("results", out JsonElement results) ||
                results.ValueKind != JsonValueKind.Array)
                return choices;

            foreach (JsonElement result in results.EnumerateArray())
            {
                if (!result.TryGetProperty("transaction", out JsonElement number) ||
                    !number.TryGetInt32(out int position) ||
                    !result.TryGetProperty("category", out JsonElement category) ||
                    category.ValueKind != JsonValueKind.String)
                    continue;

                if (position < 1 || position > itemCount) continue;

                choices.TryAdd(position - 1, category.GetString()!);
            }

            return choices;
        }

        private static string Describe(AiTransaction transaction)
        {
            List<string> parts = new() { transaction.CreditDebitIndicator == "CRDT" ? "money in" : "money out" };

            IEnumerable<string?> fields = new[] { transaction.BankTransactionCode, transaction.CounterpartyName }
                                          .Concat(transaction.RemittanceInformation);

            foreach (string? field in fields)
            {
                string? text = TransactionText.Clean(field);

                if (text == null) continue;

                text = LongNumber.Replace(text, "#");

                if (text.Any(char.IsLetter) && !parts.Contains(text, StringComparer.OrdinalIgnoreCase)) parts.Add(text);
            }

            return string.Join(" | ", parts);
        }

        private static string DescribeCategory(AiCategory category)
        {
            if (category.Key != null && BuiltInDescriptions.TryGetValue(category.Key, out string? description))
                return description;

            string kind = category.Kind switch
            {
                "INCOME" => "income",
                "TRANSFER" => "money moved between the person's own accounts",
                _ => "spending"
            };

            return category.Keywords.Count == 0
                ? $"{kind}, a category the person made themselves"
                : $"{kind}, a category the person made themselves, for things like {string.Join(", ", category.Keywords)}";
        }
    }
}
