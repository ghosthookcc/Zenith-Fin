using System.Globalization;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Ledger;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record RecurringPlanResult(int StatusCode,
                                             string? Message,
                                             long? ItemId = null);

    public sealed class RecurringPlanService
    {
        private readonly RecurringPlanRepository _recurringPlanRepository;
        private readonly UserRepository _userRepository;

        private const int PlanMonths = 12;
        private const int MaxItems = 200;
        private const int MaxNameLength = 60;
        private const int MaxSeriesKeyLength = 1000;
        private const int MaxEvery = 120;
        private const int MaxYearsAhead = 10;
        private const int MaxEndYearsAhead = 50;
        private const int MaxParts = 30;
        private const int SalaryDay = 25;
        private const decimal MaxAmount = 1_000_000_000m;

        private static readonly DateOnly EarliestDate = new(2000, 1, 1);

        private static readonly string[] KindOrder =
        {
            RecurringPaymentsService.Kinds.Income,
            RecurringPaymentsService.Kinds.Cost,
            RecurringPaymentsService.Kinds.Saving,
            RecurringPaymentsService.Kinds.Transfer
        };

        private static readonly RecurringPlanResult Unauthorized = new(StatusCodes.Status401Unauthorized, "No user found for session");
        private static readonly RecurringPlanResult NotFound = new(StatusCodes.Status404NotFound, "That row is not in your plan");

        public static class Sources
        {
            public const string Fixed = "FIXED";
            public const string Bank = "BANK";
        }

        public static class Units
        {
            public const string Week = "WEEK";
            public const string Month = "MONTH";
            public const string Year = "YEAR";
        }

        public static class States
        {
            public const string Active = "ACTIVE";
            public const string Quiet = "QUIET";
            public const string Ended = "ENDED";
            public const string Missing = "MISSING";
        }

        public static class OccurrenceStatuses
        {
            public const string Paid = "PAID";
            public const string Late = "LATE";
            public const string Passed = "PASSED";
            public const string Upcoming = "UPCOMING";
        }

        private sealed record Cadence(int Every,
                                      string Unit)
        {
            public DateOnly At(DateOnly anchor, int step) => Unit switch
            {
                Units.Week => anchor.AddDays(step * Every * 7),
                Units.Year => anchor.AddMonths(step * Every * 12),
                _ => anchor.AddMonths(step * Every)
            };

            public decimal PerMonth => Unit switch
            {
                Units.Week => 52m / 12m / Every,
                Units.Year => 1m / (12m * Every),
                _ => 1m / Every
            };

            public string Label => (Unit, Every) switch
            {
                (Units.Week, 1) => "Every week",
                (Units.Week, _) => $"Every {Every} weeks",
                (Units.Year, 1) => "Every year",
                (Units.Year, _) => $"Every {Every} years",
                (_, 1) => "Every month",
                _ => $"Every {Every} months"
            };
        }

        private sealed record Planned(DateOnly Date,
                                      RecurringDto.PlanOccurrence Occurrence);

        private static readonly Cadence MonthlyCadence = new(1, Units.Month);

        private static readonly Dictionary<string, Cadence> BankCadences = new()
        {
            ["WEEKLY"] = new(1, Units.Week),
            ["BIWEEKLY"] = new(2, Units.Week),
            ["MONTHLY"] = MonthlyCadence,
            ["BIMONTHLY"] = new(2, Units.Month),
            ["QUARTERLY"] = new(3, Units.Month),
            ["HALF_YEARLY"] = new(6, Units.Month),
            ["YEARLY"] = new(1, Units.Year)
        };

        public RecurringPlanService(RecurringPlanRepository recurringPlanRepository,
                                    UserRepository userRepository)
        {
            _recurringPlanRepository = recurringPlanRepository;
            _userRepository = userRepository;
        }

        public async Task<RecurringDto.Plan?> GetPlanAsync(string sessionId,
                                                           DateOnly today,
                                                           RecurringDto.Overview overview)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            if (!await _recurringPlanRepository.PlanExistsAsync(userId.Value))
                await _recurringPlanRepository.CreatePlanAsync(userId.Value, SeedFor(today, overview));

            RecurringPlanItemRow[] rows = await _recurringPlanRepository.GetItemsAsync(userId.Value);

            return Build(today, rows, overview.Series);
        }

        public async Task<RecurringPlanResult> CreateItemAsync(string sessionId,
                                                               RecurringDto.SavePlanItemRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            string? seriesKey = string.IsNullOrWhiteSpace(request.SeriesKey) ? null : request.SeriesKey;

            if (seriesKey != null && seriesKey.Length > MaxSeriesKeyLength) return Invalid("That payment could not be found");

            (RecurringPlanItemRow? row, string? error) = Parse(request, seriesKey != null, Today());

            if (row == null) return Invalid(error!);

            (List<RecurringPlanPartRow>? parts, string? partsError) = ParseParts(request.Parts, row.Direction);

            if (partsError != null) return Invalid(partsError);

            if (await _recurringPlanRepository.CountItemsAsync(userId.Value) >= MaxItems)
                return Invalid($"A plan can have at most {MaxItems} rows");

            row.SeriesKey = seriesKey;
            row.Parts = parts ?? new List<RecurringPlanPartRow>();

            long? itemId = await _recurringPlanRepository.InsertItemAsync(userId.Value, row);

            return itemId == null
                ? new RecurringPlanResult(StatusCodes.Status409Conflict, "That payment is already in your plan")
                : new RecurringPlanResult(StatusCodes.Status200OK, null, itemId);
        }

        public async Task<RecurringPlanResult> UpdateItemAsync(string sessionId,
                                                               long itemId,
                                                               RecurringDto.SavePlanItemRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            RecurringPlanItemRow? existing = await _recurringPlanRepository.GetItemAsync(userId.Value, itemId);

            if (existing == null) return NotFound;

            (RecurringPlanItemRow? row, string? error) = Parse(request, existing.SeriesKey != null, Today());

            if (row == null) return Invalid(error!);

            row.Id = itemId;
            row.Direction ??= existing.Direction;

            (List<RecurringPlanPartRow>? parts, string? partsError) = ParseParts(request.Parts, row.Direction);

            if (partsError != null) return Invalid(partsError);

            row.Parts = parts ?? new List<RecurringPlanPartRow>();

            return await _recurringPlanRepository.UpdateItemAsync(userId.Value, row, parts != null)
                ? new RecurringPlanResult(StatusCodes.Status200OK, null, itemId)
                : NotFound;
        }

        public async Task<RecurringPlanResult> DeleteItemAsync(string sessionId,
                                                               long itemId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            return await _recurringPlanRepository.DeleteItemAsync(userId.Value, itemId)
                ? new RecurringPlanResult(StatusCodes.Status200OK, null, itemId)
                : NotFound;
        }

        public static RecurringPlanItemRow? SeedFor(DateOnly today,
                                                    RecurringDto.Overview overview)
        {
            decimal? expected = overview.Typical.ExpectedSalary is decimal salary && Math.Round(salary, 2) > 0
                ? Math.Round(salary, 2)
                : null;

            RecurringDto.Series? salarySeries = overview.Series.Where(series => series.Kind == RecurringPaymentsService.Kinds.Income &&
                                                                                series.Subtype == Subtypes.Salary &&
                                                                                series.Status != RecurringPaymentsService.Statuses.Ended)
                                                               .OrderByDescending(series => series.Monthly)
                                                               .FirstOrDefault();

            if (salarySeries != null)
            {
                return new RecurringPlanItemRow
                {
                    Direction = Effects.In,
                    Name = "Salary",
                    Amount = expected,
                    SeriesKey = salarySeries.Key
                };
            }

            if (expected == null) return null;

            DateOnly payday = new(today.Year, today.Month, Math.Min(SalaryDay, DateTime.DaysInMonth(today.Year, today.Month)));

            if (payday > today)
            {
                DateOnly previous = payday.AddMonths(-1);
                payday = new DateOnly(previous.Year, previous.Month, Math.Min(SalaryDay, DateTime.DaysInMonth(previous.Year, previous.Month)));
            }

            return new RecurringPlanItemRow
            {
                Direction = Effects.In,
                Name = "Salary",
                Amount = expected,
                Every = 1,
                Unit = Units.Month,
                LastDate = Format(payday)
            };
        }

        public static RecurringDto.Plan Build(DateOnly today,
                                              IReadOnlyList<RecurringPlanItemRow> rows,
                                              IReadOnlyList<RecurringDto.Series> series)
        {
            DateOnly thisMonth = new(today.Year, today.Month, 1);
            DateOnly until = thisMonth.AddMonths(PlanMonths);

            Dictionary<string, RecurringDto.Series> seriesByKey = series.GroupBy(item => item.Key)
                                                                        .ToDictionary(group => group.Key, group => group.First());

            List<RecurringDto.PlanItem> items = new();
            List<Planned> planned = new();

            foreach (RecurringPlanItemRow row in rows)
            {
                bool bank = row.SeriesKey != null;
                RecurringDto.Series? linked = bank ? seriesByKey.GetValueOrDefault(row.SeriesKey!) : null;
                Cadence? custom = row.Every is int every && every > 0 && UnitOf(row.Unit) is string unit ? new Cadence(every, unit) : null;
                Cadence? bankCadence = linked == null ? null : BankCadences.GetValueOrDefault(linked.Frequency) ?? MonthlyCadence;
                Cadence? cadence = custom ?? bankCadence;
                decimal? bankAmount = linked == null ? null : TypicalOf(linked);
                decimal? amount = row.Amount ?? bankAmount;
                DateOnly? anchor = bank ? ParseDate(linked?.Last) : ParseDate(row.LastDate);
                DateOnly? end = ParseDate(row.EndDate);
                string direction = linked != null ? DirectionOf(linked) : row.Direction == Effects.In ? Effects.In : Effects.Out;
                string name = row.Name ?? linked?.Name ?? (direction == Effects.In ? "Money in" : "Money out");

                string state = bank && (linked == null || anchor == null) ? States.Missing
                    : end is DateOnly stop && stop < today ? States.Ended
                    : linked?.Status == RecurringPaymentsService.Statuses.Ended ? States.Quiet
                    : States.Active;

                DateOnly? next = null;

                if (linked != null && anchor is DateOnly paidOn && paidOn >= thisMonth)
                {
                    decimal paid = Math.Abs(CountsAsCounted(linked.Kind) ? linked.ThisMonthCounted : linked.ThisMonth);

                    planned.Add(new Planned(paidOn, Occurrence(row.Id, name, direction, paidOn, paid, OccurrenceStatuses.Paid)));
                }

                if (state != States.Missing && anchor is DateOnly start && cadence != null && amount is decimal value)
                {
                    int firstStep = bank ? 1 : 0;

                    foreach (DateOnly date in DatesBetween(start, cadence, firstStep, thisMonth, until, end))
                    {
                        string status = date >= today ? OccurrenceStatuses.Upcoming
                            : bank ? OccurrenceStatuses.Late
                            : OccurrenceStatuses.Passed;

                        planned.Add(new Planned(date, Occurrence(row.Id, name, direction, date, value, status)));
                    }

                    next = NextFrom(start, cadence, firstStep, today, end);
                }

                bool counted = (state is States.Active or States.Quiet) && cadence != null && amount != null;
                RecurringDto.PlanPart[] parts = direction == Effects.Out
                    ? row.Parts.Select(part => new RecurringDto.PlanPart { Id = part.Id, Name = part.Name, Amount = part.Amount }).ToArray()
                    : Array.Empty<RecurringDto.PlanPart>();
                decimal partsTotal = parts.Sum(part => part.Amount);

                items.Add(new RecurringDto.PlanItem
                {
                    Id = row.Id,
                    Source = bank ? Sources.Bank : Sources.Fixed,
                    SeriesKey = row.SeriesKey,
                    Direction = direction,
                    Name = name,
                    CustomName = row.Name,
                    Amount = amount,
                    CustomAmount = row.Amount,
                    BankAmount = bankAmount,
                    Every = cadence?.Every ?? 0,
                    Unit = cadence?.Unit ?? string.Empty,
                    CadenceLabel = cadence?.Label ?? string.Empty,
                    CustomCadence = custom != null,
                    BankCadenceLabel = bankCadence?.Label,
                    LastDate = anchor is DateOnly last ? Format(last) : null,
                    EndDate = end is DateOnly ends ? Format(ends) : null,
                    Next = next is DateOnly upcoming ? Format(upcoming) : null,
                    Monthly = counted ? Math.Round(amount!.Value * cadence!.PerMonth, 2) : 0,
                    State = state,
                    Kind = linked?.Kind,
                    CategoryIcon = linked?.CategoryIcon,
                    CategoryColor = linked?.CategoryColor,
                    Accounts = linked?.Accounts ?? Array.Empty<string>(),
                    Parts = parts,
                    PartsTotal = partsTotal,
                    Unexplained = parts.Length > 0 && amount is decimal total ? total - partsTotal : null
                });
            }

            List<RecurringDto.PlanMonth> months = new();

            for (DateOnly month = thisMonth; month < until; month = month.AddMonths(1))
            {
                DateOnly end = month.AddMonths(1);
                RecurringDto.PlanOccurrence[] occurrences = planned.Where(entry => entry.Date >= month && entry.Date < end)
                                                                   .OrderBy(entry => entry.Date)
                                                                   .ThenBy(entry => entry.Occurrence.Direction == Effects.In ? 0 : 1)
                                                                   .ThenBy(entry => entry.Occurrence.Name, StringComparer.Ordinal)
                                                                   .Select(entry => entry.Occurrence)
                                                                   .ToArray();
                decimal moneyIn = occurrences.Where(occurrence => occurrence.Direction == Effects.In).Sum(occurrence => occurrence.Amount);
                decimal moneyOut = occurrences.Where(occurrence => occurrence.Direction == Effects.Out).Sum(occurrence => occurrence.Amount);

                months.Add(new RecurringDto.PlanMonth
                {
                    Month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Current = month == thisMonth,
                    In = moneyIn,
                    Out = moneyOut,
                    Left = moneyIn - moneyOut,
                    Occurrences = occurrences
                });
            }

            decimal averageIn = items.Where(item => item.Direction == Effects.In).Sum(item => item.Monthly);
            decimal averageOut = items.Where(item => item.Direction == Effects.Out).Sum(item => item.Monthly);
            HashSet<string> plannedKeys = rows.Where(row => row.SeriesKey != null)
                                              .Select(row => row.SeriesKey!)
                                              .ToHashSet(StringComparer.Ordinal);

            return new RecurringDto.Plan
            {
                Items = items.OrderBy(item => item.Direction == Effects.In ? 0 : 1)
                             .ThenBy(item => item.State is States.Ended or States.Missing ? 1 : 0)
                             .ThenByDescending(item => item.Monthly)
                             .ThenBy(item => item.Name, StringComparer.Ordinal)
                             .ThenBy(item => item.Id)
                             .ToArray(),
                Months = months,
                Average = new RecurringDto.PlanTotals
                {
                    In = averageIn,
                    Out = averageOut,
                    Left = averageIn - averageOut
                },
                Suggestions = series.Where(item => item.Status != RecurringPaymentsService.Statuses.Ended && !plannedKeys.Contains(item.Key))
                                    .OrderBy(item => Array.IndexOf(KindOrder, item.Kind))
                                    .ThenByDescending(item => item.Monthly)
                                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                                    .Select(item => new RecurringDto.PlanSuggestion
                                    {
                                        Key = item.Key,
                                        Name = item.Name,
                                        Kind = item.Kind,
                                        Direction = DirectionOf(item),
                                        Amount = TypicalOf(item),
                                        CadenceLabel = (BankCadences.GetValueOrDefault(item.Frequency) ?? MonthlyCadence).Label,
                                        Last = item.Last,
                                        Monthly = item.Monthly,
                                        CategoryIcon = item.CategoryIcon,
                                        CategoryColor = item.CategoryColor,
                                        Accounts = item.Accounts
                                    })
                                    .ToArray()
            };
        }

        private static (RecurringPlanItemRow? Row, string? Error) Parse(RecurringDto.SavePlanItemRequest request,
                                                                        bool bank,
                                                                        DateOnly today)
        {
            string? name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();

            if (name != null && (name.Length > MaxNameLength || name.Any(char.IsControl)))
                return (null, $"Names can be at most {MaxNameLength} characters on one line");

            string? direction = string.IsNullOrWhiteSpace(request.Direction) ? null : request.Direction.Trim().ToUpperInvariant();

            if (direction is not (null or Effects.In or Effects.Out) || (!bank && direction == null))
                return (null, "Choose money in or money out");

            decimal? amount = request.Amount is decimal entered ? Math.Round(entered, 2, MidpointRounding.AwayFromZero) : null;

            if (amount is decimal value && (value <= 0 || value > MaxAmount))
                return (null, "The amount must be more than 0 and at most 1 000 000 000 kr");

            if (!bank && amount == null) return (null, "Enter an amount");

            string? unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim().ToUpperInvariant();

            if ((request.Every == null) != (unit == null) || (!bank && unit == null))
                return (null, "Choose how often it happens");

            if (unit is not (null or Units.Month or Units.Year) || request.Every is < 1 or > MaxEvery)
                return (null, $"It can repeat every 1 to {MaxEvery} months or years");

            DateOnly? lastDate = null;

            if (!bank)
            {
                lastDate = ParseDate(request.LastDate);

                if (lastDate is not DateOnly last || last < EarliestDate || last > today.AddYears(MaxYearsAhead))
                    return (null, "Enter when it last happened, as a date from 2000 on");
            }

            DateOnly? endDate = null;

            if (!string.IsNullOrWhiteSpace(request.EndDate))
            {
                endDate = ParseDate(request.EndDate);

                if (endDate is not DateOnly end || end < EarliestDate || end > today.AddYears(MaxEndYearsAhead))
                    return (null, "Enter the end as a date");

                if (lastDate is DateOnly start && end < start)
                    return (null, "It cannot end before it last happened");
            }

            return (new RecurringPlanItemRow
            {
                Direction = direction,
                Name = name,
                Amount = amount,
                Every = request.Every,
                Unit = unit,
                LastDate = lastDate is DateOnly happened ? Format(happened) : null,
                EndDate = endDate is DateOnly ends ? Format(ends) : null
            }, null);
        }

        private static (List<RecurringPlanPartRow>? Parts, string? Error) ParseParts(IReadOnlyList<RecurringDto.SavePlanPart>? requested,
                                                                                    string? direction)
        {
            if (requested == null) return (null, null);

            if (requested.Count > MaxParts) return (null, $"A row can have at most {MaxParts} parts");

            if (requested.Count > 0 && direction == Effects.In) return (null, "Only money out can be split into parts");

            List<RecurringPlanPartRow> parts = new();

            foreach (RecurringDto.SavePlanPart part in requested)
            {
                string name = part.Name?.Trim() ?? string.Empty;

                if (name.Length == 0 || name.Length > MaxNameLength || name.Any(char.IsControl))
                    return (null, $"Give each part a name of at most {MaxNameLength} characters on one line");

                decimal? amount = part.Amount is decimal entered ? Math.Round(entered, 2, MidpointRounding.AwayFromZero) : null;

                if (amount is not decimal value || value <= 0 || value > MaxAmount)
                    return (null, "Each part needs an amount above 0 and at most 1 000 000 000 kr");

                parts.Add(new RecurringPlanPartRow { Name = name, Amount = value });
            }

            return (parts, null);
        }

        private static IEnumerable<DateOnly> DatesBetween(DateOnly anchor,
                                                          Cadence cadence,
                                                          int firstStep,
                                                          DateOnly from,
                                                          DateOnly until,
                                                          DateOnly? end)
        {
            for (int step = firstStep; ; step++)
            {
                DateOnly date = cadence.At(anchor, step);

                if (date >= until || (end is DateOnly last && date > last)) yield break;

                if (date >= from) yield return date;
            }
        }

        private static DateOnly? NextFrom(DateOnly anchor,
                                          Cadence cadence,
                                          int firstStep,
                                          DateOnly today,
                                          DateOnly? end)
        {
            for (int step = firstStep; ; step++)
            {
                DateOnly date = cadence.At(anchor, step);

                if (end is DateOnly last && date > last) return null;

                if (date >= today) return date;
            }
        }

        private static RecurringDto.PlanOccurrence Occurrence(long itemId,
                                                              string name,
                                                              string direction,
                                                              DateOnly date,
                                                              decimal amount,
                                                              string status)
        {
            return new RecurringDto.PlanOccurrence
            {
                ItemId = itemId,
                Name = name,
                Direction = direction,
                Date = Format(date),
                Amount = amount,
                Status = status
            };
        }

        private static bool CountsAsCounted(string kind) => kind is RecurringPaymentsService.Kinds.Income or RecurringPaymentsService.Kinds.Cost;

        private static decimal TypicalOf(RecurringDto.Series series)
        {
            return Math.Round(Math.Abs(CountsAsCounted(series.Kind) ? series.TypicalCounted : series.TypicalAmount), 2);
        }

        private static string DirectionOf(RecurringDto.Series series)
        {
            return series.Kind switch
            {
                RecurringPaymentsService.Kinds.Income => Effects.In,
                RecurringPaymentsService.Kinds.Transfer => series.Direction == Effects.In ? Effects.In : Effects.Out,
                _ => Effects.Out
            };
        }

        private static string? UnitOf(string? unit) => unit is Units.Month or Units.Year ? unit : null;

        private static RecurringPlanResult Invalid(string message) => new(StatusCodes.Status400BadRequest, message);

        private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

        private static DateOnly? ParseDate(string? date)
        {
            return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value) ? value : null;
        }

        private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
