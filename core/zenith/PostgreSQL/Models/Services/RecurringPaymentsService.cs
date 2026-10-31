using System.Globalization;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Ledger;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class RecurringPaymentsService
    {
        private readonly TransactionService _transactionService;
        private readonly RecurringPlanService _recurringPlanService;

        private const int HistoryMonths = 12;
        private const int TypicalOccurrences = 6;
        private const int MergeDays = 3;
        private const int MinimumGraceDays = 7;

        private static readonly string[] SavingRoles = { AccountRoles.Savings, AccountRoles.Investment };
        private static readonly string[] KindOrder = { Kinds.Cost, Kinds.Income, Kinds.Saving, Kinds.Transfer };

        public static class Kinds
        {
            public const string Income = "INCOME";
            public const string Cost = "COST";
            public const string Saving = "SAVING";
            public const string Transfer = "TRANSFER";
        }

        public static class Statuses
        {
            public const string Paid = "PAID";
            public const string Due = "DUE";
            public const string Late = "LATE";
            public const string Later = "LATER";
            public const string Ended = "ENDED";
        }

        private sealed record Row(TransactionDto.Transaction Transaction,
                                  DateOnly Date);

        private sealed record Occurrence(DateOnly Date,
                                         decimal Amount,
                                         decimal Counted);

        private sealed record Cadence(int Months,
                                      int Days,
                                      decimal PerMonth)
        {
            public DateOnly After(DateOnly date) => Months > 0 ? date.AddMonths(Months) : date.AddDays(Days);

            public int GraceDays => Math.Max(MinimumGraceDays, (Months > 0 ? Months * 30 : Days) / 2);
        }

        private static readonly Dictionary<string, Cadence> Cadences = new()
        {
            ["WEEKLY"] = new(0, 7, 52m / 12m),
            ["BIWEEKLY"] = new(0, 14, 26m / 12m),
            ["MONTHLY"] = new(1, 0, 1m),
            ["BIMONTHLY"] = new(2, 0, 1m / 2m),
            ["QUARTERLY"] = new(3, 0, 1m / 3m),
            ["HALF_YEARLY"] = new(6, 0, 1m / 6m),
            ["YEARLY"] = new(12, 0, 1m / 12m)
        };

        public RecurringPaymentsService(TransactionService transactionService,
                                        RecurringPlanService recurringPlanService)
        {
            _transactionService = transactionService;
            _recurringPlanService = recurringPlanService;
        }

        public async Task<RecurringDto.Overview?> GetOverviewAsync(string sessionId)
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            DateOnly thisMonth = new(today.Year, today.Month, 1);

            TransactionDto.TransactionsResponse? response = await _transactionService.GetTransactionsAsync(sessionId,
                                                                                                          thisMonth.AddMonths(-HistoryMonths),
                                                                                                          thisMonth.AddMonths(1).AddDays(-1));

            if (response == null) return null;

            RecurringDto.Overview overview = Build(today, response);
            RecurringDto.Plan? plan = await _recurringPlanService.GetPlanAsync(sessionId, today, overview);

            if (plan != null) overview.Plan = plan;

            return overview;
        }

        public static RecurringDto.Overview Build(DateOnly today,
                                                  TransactionDto.TransactionsResponse response)
        {
            DateOnly thisMonth = new(today.Year, today.Month, 1);
            DateOnly nextMonth = thisMonth.AddMonths(1);
            DateOnly firstMonth = thisMonth.AddMonths(-HistoryMonths);

            Dictionary<long, TransactionDto.Account> accounts = response.Accounts.GroupBy(account => account.Id)
                                                                                 .ToDictionary(group => group.Key, group => group.First());
            Dictionary<long, TagDto.Tag> tags = response.Tags.GroupBy(tag => tag.Id)
                                                             .ToDictionary(group => group.Key, group => group.First());

            Row[] rows = response.Transactions.Where(transaction => transaction.Counted)
                                              .Select(transaction => ParseDate(transaction.Date) is DateOnly date ? new Row(transaction, date) : null)
                                              .Where(row => row != null && row.Date >= firstMonth && row.Date < nextMonth)
                                              .Select(row => row!)
                                              .ToArray();

            string currency = accounts.Values.GroupBy(account => account.Currency)
                                             .OrderByDescending(group => group.Count())
                                             .Select(group => group.Key)
                                             .FirstOrDefault() ?? "SEK";

            List<RecurringDto.Series> series = new();
            Dictionary<string, Row[]> rowsByKey = new();

            foreach (IGrouping<string, Row> group in rows.Where(row => row.Transaction.SeriesKey != null && row.Transaction.Recurring != null)
                                                        .GroupBy(row => row.Transaction.SeriesKey!))
            {
                Row[] ordered = group.OrderBy(row => row.Date).ThenBy(row => row.Transaction.Id).ToArray();
                rowsByKey[group.Key] = ordered;
                Occurrence[] occurrences = Merge(ordered);
                Occurrence last = occurrences[^1];
                TransactionDto.Transaction latest = ordered[^1].Transaction;
                TransactionDto.RecurringInfo info = latest.Recurring!;
                Cadence cadence = Cadences.GetValueOrDefault(info.Frequency) ?? Cadences["MONTHLY"];
                Occurrence[] recent = occurrences.TakeLast(TypicalOccurrences).ToArray();
                decimal typicalAmount = Median(recent.Select(occurrence => occurrence.Amount));
                decimal typicalCounted = Median(recent.Select(occurrence => occurrence.Counted));
                string kind = KindOf(last, latest, accounts);
                DateOnly next = ParseDate(info.Next) ?? cadence.After(last.Date);
                TagDto.Tag? tag = latest.TagId is long tagId ? tags.GetValueOrDefault(tagId) : null;
                Row[] thisMonthRows = ordered.Where(row => row.Date >= thisMonth && row.Date < nextMonth).ToArray();
                string[] accountNames = ordered.Reverse()
                                               .Select(row => accounts.GetValueOrDefault(row.Transaction.AccountId)?.Name)
                                               .Where(name => !string.IsNullOrWhiteSpace(name))
                                               .Select(name => name!)
                                               .Distinct()
                                               .ToArray();
                string description = string.IsNullOrWhiteSpace(latest.Description)
                    ? latest.BankTransactionCode ?? (latest.Amount < 0 ? "Payment" : "Deposit")
                    : latest.Description;
                long? actionable = ordered.Where(row => row.Transaction.SharedBy == null &&
                                                        accounts.GetValueOrDefault(row.Transaction.AccountId)?.SharedBy == null)
                                          .Select(row => (long?)row.Transaction.Id)
                                          .LastOrDefault();

                series.Add(new RecurringDto.Series
                {
                    Key = group.Key,
                    LatestTransactionId = actionable,
                    Name = description.Any(char.IsLetter)
                        ? description
                        : $"{(latest.Amount < 0 ? "Out of" : "Into")} {accountNames.FirstOrDefault() ?? "an account"}",
                    Kind = kind,
                    Direction = latest.Amount < 0 ? Effects.Out : Effects.In,
                    Type = latest.Type,
                    Subtype = latest.Subtype,
                    Category = tag?.Name,
                    CategoryIcon = tag?.Icon,
                    CategoryColor = tag?.Color,
                    Accounts = accountNames,
                    Frequency = info.Frequency,
                    FrequencyLabel = info.Label,
                    Manual = info.Manual,
                    Occurrences = info.Occurrences,
                    First = Format(occurrences[0].Date),
                    Last = Format(last.Date),
                    LastAmount = last.Amount,
                    Next = Format(next),
                    TypicalAmount = typicalAmount,
                    TypicalCounted = typicalCounted,
                    MinAmount = recent.Min(occurrence => Math.Abs(occurrence.Amount)),
                    MaxAmount = recent.Max(occurrence => Math.Abs(occurrence.Amount)),
                    Monthly = Math.Round(Math.Abs(kind is Kinds.Income or Kinds.Cost ? typicalCounted : typicalAmount) * cadence.PerMonth, 2),
                    Status = StatusOf(thisMonthRows.Length > 0, next, cadence, today, nextMonth),
                    ThisMonth = thisMonthRows.Sum(row => row.Transaction.Amount),
                    ThisMonthCounted = thisMonthRows.Sum(row => row.Transaction.CountedAmount),
                    Currency = latest.Currency
                });
            }

            HashSet<long> savingIds = series.Where(item => item.Kind == Kinds.Saving)
                                            .SelectMany(item => rowsByKey[item.Key])
                                            .Select(row => row.Transaction.Id)
                                            .ToHashSet();

            series.RemoveAll(item => item.Kind == Kinds.Transfer &&
                                     rowsByKey[item.Key].Any(row => row.Transaction.MatchedTransactionId is long matched && savingIds.Contains(matched)));

            Dictionary<string, string> kindByKey = series.ToDictionary(item => item.Key, item => item.Kind);
            RecurringDto.Series[] active = series.Where(item => item.Status != Statuses.Ended).ToArray();
            RecurringDto.Series[] due = active.Where(item => item.Status is Statuses.Due or Statuses.Late).ToArray();

            List<RecurringDto.MonthTotals> months = new();

            for (DateOnly month = firstMonth; month < nextMonth; month = month.AddMonths(1))
            {
                DateOnly end = month.AddMonths(1);
                Row[] inMonth = rows.Where(row => row.Date >= month && row.Date < end).ToArray();
                decimal[] manual = response.ManualTransactions.Where(transaction => transaction.MatchedTransactionId == null &&
                                                                                    ParseDate(transaction.Date) is DateOnly date &&
                                                                                    date >= month &&
                                                                                    date < end)
                                                              .Select(transaction => LedgerEngine.CountedByCounting(transaction.Amount, transaction.Counting))
                                                              .ToArray();
                Row[] recurring = inMonth.Where(row => row.Transaction.SeriesKey != null && kindByKey.ContainsKey(row.Transaction.SeriesKey)).ToArray();

                decimal moneyIn = inMonth.Where(row => row.Transaction.CountedAmount > 0).Sum(row => row.Transaction.CountedAmount) +
                                  manual.Where(amount => amount > 0).Sum();
                decimal moneyOut = -inMonth.Where(row => row.Transaction.CountedAmount < 0).Sum(row => row.Transaction.CountedAmount) -
                                   manual.Where(amount => amount < 0).Sum();
                decimal recurringIncome = recurring.Where(row => row.Transaction.CountedAmount > 0).Sum(row => row.Transaction.CountedAmount);
                decimal recurringCosts = -recurring.Where(row => row.Transaction.CountedAmount < 0).Sum(row => row.Transaction.CountedAmount);
                decimal saved = recurring.Where(row => kindByKey[row.Transaction.SeriesKey!] == Kinds.Saving)
                                         .Sum(row => Math.Abs(row.Transaction.Amount));
                bool current = month == thisMonth;

                months.Add(new RecurringDto.MonthTotals
                {
                    Month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Current = current,
                    MoneyIn = moneyIn,
                    MoneyOut = moneyOut,
                    RecurringIncome = recurringIncome,
                    RecurringCosts = recurringCosts,
                    Saved = saved,
                    OtherSpending = moneyOut - recurringCosts,
                    LeftAfterRecurring = moneyIn - recurringCosts,
                    Left = moneyIn - moneyOut,
                    DueIncome = current ? due.Where(item => item.Kind == Kinds.Income).Sum(item => Math.Abs(item.TypicalCounted)) : 0,
                    DueCosts = current ? due.Where(item => item.Kind == Kinds.Cost).Sum(item => Math.Abs(item.TypicalCounted)) : 0,
                    DueSavings = current ? due.Where(item => item.Kind == Kinds.Saving).Sum(item => Math.Abs(item.TypicalAmount)) : 0
                });
            }

            decimal recurringIncomePerMonth = active.Where(item => item.Kind == Kinds.Income).Sum(item => item.Monthly);
            decimal costs = active.Where(item => item.Kind == Kinds.Cost).Sum(item => item.Monthly);
            decimal savings = active.Where(item => item.Kind == Kinds.Saving).Sum(item => item.Monthly);
            decimal? expectedSalary = ExpectedSalaryFor(response.ExpectedSalaries, thisMonth);
            decimal income = expectedSalary is decimal salary
                ? salary + active.Where(item => item.Kind == Kinds.Income && item.Subtype != Subtypes.Salary).Sum(item => item.Monthly)
                : recurringIncomePerMonth;

            return new RecurringDto.Overview
            {
                Today = Format(today),
                Currency = currency,
                Typical = new RecurringDto.Summary
                {
                    Income = income,
                    IncomeSource = expectedSalary == null ? "RECURRING" : "EXPECTED_SALARY",
                    ExpectedSalary = expectedSalary,
                    RecurringIncome = recurringIncomePerMonth,
                    Costs = costs,
                    Savings = savings,
                    LeftAfterCosts = income - costs,
                    LeftAfterSavings = income - costs - savings
                },
                Months = months,
                Series = series.OrderBy(item => Array.IndexOf(KindOrder, item.Kind))
                               .ThenBy(item => item.Status == Statuses.Ended ? 1 : 0)
                               .ThenByDescending(item => item.Monthly)
                               .ThenBy(item => item.Name, StringComparer.Ordinal)
                               .ToArray()
            };
        }

        private static Occurrence[] Merge(IReadOnlyList<Row> ordered)
        {
            List<Occurrence> occurrences = new();

            foreach (Row row in ordered)
            {
                if (occurrences.Count > 0 && row.Date.DayNumber - occurrences[^1].Date.DayNumber <= MergeDays)
                {
                    Occurrence previous = occurrences[^1];

                    occurrences[^1] = previous with
                    {
                        Amount = previous.Amount + row.Transaction.Amount,
                        Counted = previous.Counted + row.Transaction.CountedAmount
                    };
                }
                else
                {
                    occurrences.Add(new Occurrence(row.Date, row.Transaction.Amount, row.Transaction.CountedAmount));
                }
            }

            return occurrences.ToArray();
        }

        private static string KindOf(Occurrence last,
                                     TransactionDto.Transaction latest,
                                     IReadOnlyDictionary<long, TransactionDto.Account> accounts)
        {
            if (last.Counted > 0) return Kinds.Income;
            if (last.Counted < 0) return Kinds.Cost;

            bool intoSavings = last.Amount > 0 && SavingRoles.Contains(accounts.GetValueOrDefault(latest.AccountId)?.Role);
            bool toVirtualAccount = last.Amount < 0 && latest.VirtualAccountId != null;

            return intoSavings || toVirtualAccount ? Kinds.Saving : Kinds.Transfer;
        }

        private static string StatusOf(bool paidThisMonth,
                                       DateOnly next,
                                       Cadence cadence,
                                       DateOnly today,
                                       DateOnly nextMonth)
        {
            if (paidThisMonth) return Statuses.Paid;

            if (next >= today) return next < nextMonth ? Statuses.Due : Statuses.Later;

            return today.DayNumber - next.DayNumber <= cadence.GraceDays ? Statuses.Late : Statuses.Ended;
        }

        private static decimal? ExpectedSalaryFor(IReadOnlyList<ExpectedSalaryDto.ExpectedSalary> salaries,
                                                  DateOnly month)
        {
            string key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

            return salaries.Where(salary => salary.Month.Length >= 7 && string.CompareOrdinal(salary.Month[..7], key) <= 0)
                           .OrderBy(salary => salary.Month[..7], StringComparer.Ordinal)
                           .Select(salary => (decimal?)salary.Amount)
                           .LastOrDefault();
        }

        private static decimal Median(IEnumerable<decimal> values)
        {
            decimal[] sorted = values.OrderBy(value => value).ToArray();

            if (sorted.Length == 0) return 0;

            int middle = sorted.Length / 2;

            return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        }

        private static DateOnly? ParseDate(string? date)
        {
            return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value) ? value : null;
        }

        private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
