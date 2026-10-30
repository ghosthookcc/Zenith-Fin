namespace ZenithFin.Api.Models.Dtos
{
    public static class RecurringDto
    {
        public sealed class Overview
        {
            public string Today { get; set; } = string.Empty;
            public string Currency { get; set; } = string.Empty;
            public Summary Typical { get; set; } = new();
            public IReadOnlyList<MonthTotals> Months { get; set; } = Array.Empty<MonthTotals>();
            public IReadOnlyList<Series> Series { get; set; } = Array.Empty<Series>();
        }

        public sealed class Summary
        {
            public decimal Income { get; set; }
            public string IncomeSource { get; set; } = string.Empty;
            public decimal? ExpectedSalary { get; set; }
            public decimal RecurringIncome { get; set; }
            public decimal Costs { get; set; }
            public decimal Savings { get; set; }
            public decimal LeftAfterCosts { get; set; }
            public decimal LeftAfterSavings { get; set; }
        }

        public sealed class MonthTotals
        {
            public string Month { get; set; } = string.Empty;
            public bool Current { get; set; }
            public decimal MoneyIn { get; set; }
            public decimal MoneyOut { get; set; }
            public decimal RecurringIncome { get; set; }
            public decimal RecurringCosts { get; set; }
            public decimal Saved { get; set; }
            public decimal OtherSpending { get; set; }
            public decimal LeftAfterRecurring { get; set; }
            public decimal Left { get; set; }
            public decimal DueIncome { get; set; }
            public decimal DueCosts { get; set; }
            public decimal DueSavings { get; set; }
        }

        public sealed class Series
        {
            public string Key { get; set; } = string.Empty;
            public long? LatestTransactionId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string Direction { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Subtype { get; set; } = string.Empty;
            public string? Category { get; set; }
            public string? CategoryIcon { get; set; }
            public string? CategoryColor { get; set; }
            public IReadOnlyList<string> Accounts { get; set; } = Array.Empty<string>();
            public string Frequency { get; set; } = string.Empty;
            public string FrequencyLabel { get; set; } = string.Empty;
            public bool Manual { get; set; }
            public int Occurrences { get; set; }
            public string First { get; set; } = string.Empty;
            public string Last { get; set; } = string.Empty;
            public decimal LastAmount { get; set; }
            public string Next { get; set; } = string.Empty;
            public decimal TypicalAmount { get; set; }
            public decimal TypicalCounted { get; set; }
            public decimal MinAmount { get; set; }
            public decimal MaxAmount { get; set; }
            public decimal Monthly { get; set; }
            public string Status { get; set; } = string.Empty;
            public decimal ThisMonth { get; set; }
            public string Currency { get; set; } = string.Empty;
        }
    }
}
