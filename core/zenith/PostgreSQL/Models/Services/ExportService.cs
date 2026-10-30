using System.Globalization;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.EnableBanking;
using ZenithFin.Ledger;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record ExportFile(string FileName,
                                    byte[] Content);

    public sealed record AccountsExportInput(DateTime ExportedAt,
                                             IReadOnlyList<AccountDto.Balance> Accounts,
                                             IReadOnlyList<ExportAccountRow> Details,
                                             IReadOnlyList<CreditAccountDto.CreditAccount> CreditAccounts,
                                             IReadOnlyList<AccountShareRow> Shares,
                                             IReadOnlyList<VirtualAccountDto.VirtualAccount> VirtualAccounts,
                                             IReadOnlyList<ManualTransactionDto.ManualTransaction> ManualTransactions,
                                             IReadOnlyList<LedgerEntry> Entries);

    public sealed record TransactionsExportInput(DateOnly From,
                                                 DateOnly To,
                                                 IReadOnlyList<TransactionDto.TransactionsResponse> Months);

    public sealed class ExportService
    {
        private readonly UserRepository _userRepository;
        private readonly ExportRepository _exportRepository;
        private readonly CreditAccountRepository _creditAccountRepository;
        private readonly AccountShareRepository _accountShareRepository;
        private readonly BankingService _bankingService;
        private readonly VirtualAccountService _virtualAccountService;
        private readonly ManualTransactionService _manualTransactionService;
        private readonly LedgerService _ledgerService;
        private readonly TransactionService _transactionService;

        private const int AverageMonths = 12;

        private static readonly Dictionary<string, string> BalanceCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["closingBooked"] = "CLBD",
            ["interimBooked"] = "ITBD",
            ["openingBooked"] = "OPBD",
            ["previouslyClosedBooked"] = "PRCD",
            ["closingAvailable"] = "CLAV",
            ["interimAvailable"] = "ITAV",
            ["forwardAvailable"] = "FWAV",
            ["openingAvailable"] = "OPAV",
            ["expected"] = "XPCD"
        };

        private static readonly string[] BookedCodes = { "CLBD", "ITBD", "OPBD", "PRCD" };
        private static readonly string[] AvailableCodes = { "ITAV", "CLAV", "FWAV", "OPAV" };

        private static readonly CsvColumn<TransactionLine>[] TransactionColumns =
        {
            new("Date", line => CsvCell.Date(line.Date)),
            new("Year", line => CsvCell.Count(line.Date.Year)),
            new("Month", line => CsvCell.Text(line.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture))),
            new("Weekday", line => CsvCell.Text(line.Date.DayOfWeek.ToString())),
            new("Booking date", line => CsvCell.Date(line.BookingDate)),
            new("Value date", line => CsvCell.Date(line.ValueDate)),
            new("Status", line => CsvCell.Text(line.Status)),
            new("Source", line => CsvCell.Text(line.Source)),
            new("Transaction ID", line => CsvCell.Text(line.TransactionId)),
            new("Account ID", line => CsvCell.Count(line.AccountId)),
            new("Account", line => CsvCell.Text(line.Account)),
            new("Bank", line => CsvCell.Text(line.Bank)),
            new("Account type", line => CsvCell.Text(line.AccountType)),
            new("Shared by", line => CsvCell.Text(line.SharedBy)),
            new("Description", line => CsvCell.Text(line.Description)),
            new("Counterparty", line => CsvCell.Text(line.Counterparty)),
            new("Counterparty account", line => CsvCell.Text(line.CounterpartyAccount)),
            new("Message", line => CsvCell.Text(line.Message)),
            new("Bank code", line => CsvCell.Text(line.BankCode)),
            new("Amount", line => CsvCell.Number(line.Amount)),
            new("Currency", line => CsvCell.Text(line.Currency)),
            new("Original amount", line => CsvCell.Number(line.OriginalAmount)),
            new("Original currency", line => CsvCell.Text(line.OriginalCurrency)),
            new("Category", line => CsvCell.Text(line.Category)),
            new("Category kind", line => CsvCell.Text(line.CategoryKind)),
            new("Category set by", line => CsvCell.Text(line.CategorySetBy)),
            new("Type", line => CsvCell.Text(line.Type)),
            new("Subtype", line => CsvCell.Text(line.Subtype)),
            new("Type set by", line => CsvCell.Text(line.TypeSetBy)),
            new("Counts as", line => CsvCell.Text(line.CountsAs)),
            new("Counted amount", line => CsvCell.Number(line.CountedAmount)),
            new("Counting set by", line => CsvCell.Text(line.CountingSetBy)),
            new("Recurring", line => CsvCell.Text(line.Recurring)),
            new("Credit account", line => CsvCell.Text(line.CreditAccount)),
            new("Virtual account", line => CsvCell.Text(line.VirtualAccount)),
            new("Shared cost", line => CsvCell.Text(line.SharedCost)),
            new("Your share", line => CsvCell.Number(line.YourShare)),
            new("Paid by", line => CsvCell.Text(line.PaidBy)),
            new("Linked transaction ID", line => CsvCell.Count(line.LinkedTransactionId))
        };

        private static readonly CsvColumn<AccountLine>[] AccountColumns =
        {
            new("Exported at", line => CsvCell.Time(line.ExportedAt)),
            new("Kind", line => CsvCell.Text(line.Kind)),
            new("Account ID", line => CsvCell.Count(line.Id)),
            new("Account name", line => CsvCell.Text(line.Name)),
            new("Name at bank", line => CsvCell.Text(line.NameAtBank)),
            new("Bank", line => CsvCell.Text(line.Bank)),
            new("Account type", line => CsvCell.Text(line.AccountType)),
            new("Account type set by", line => CsvCell.Text(line.AccountTypeSetBy)),
            new("IBAN", line => CsvCell.Text(line.Iban)),
            new("Account number", line => CsvCell.Text(line.AccountNumber)),
            new("Clearing number", line => CsvCell.Text(line.ClearingNumber)),
            new("Currency", line => CsvCell.Text(line.Currency)),
            new("Balance", line => CsvCell.Number(line.Balance)),
            new("Booked balance", line => CsvCell.Number(line.BookedBalance)),
            new("Available balance", line => CsvCell.Number(line.AvailableBalance)),
            new("Balance updated at", line => CsvCell.Time(line.BalanceUpdatedAt)),
            new("Share of total (%)", line => CsvCell.Number(line.ShareOfTotal)),
            new("Shared by bank", line => CsvCell.YesNo(line.SharedByBank)),
            new("Bank access", line => CsvCell.Text(line.BankAccess)),
            new("Bank access until", line => CsvCell.Date(line.BankAccessUntil)),
            new("Transactions synced at", line => CsvCell.Time(line.TransactionsSyncedAt)),
            new("Shared with", line => CsvCell.Text(line.SharedWith)),
            new("Credit account", line => CsvCell.Text(line.CreditAccount)),
            new("Credit setup", line => CsvCell.Text(line.CreditSetup)),
            new("Credit limit", line => CsvCell.Number(line.CreditLimit)),
            new("Credit used this month", line => CsvCell.Number(line.CreditUsed)),
            new("Credit left this month", line => CsvCell.Number(line.CreditLeft)),
            new("Counting", line => CsvCell.Text(line.Counting)),
            new("Starting balance", line => CsvCell.Number(line.StartingBalance)),
            new("Starting balance set at", line => CsvCell.Time(line.StartingBalanceSetAt)),
            new("This month", line => CsvCell.Text(line.ThisMonth)),
            new("Money in this month", line => CsvCell.Number(line.Current.MoneyIn)),
            new("Money out this month", line => CsvCell.Number(line.Current.MoneyOut)),
            new("Net this month", line => CsvCell.Number(line.Current.Net)),
            new("Deposits this month", line => CsvCell.Number(line.Current.Deposits)),
            new("Withdrawals this month", line => CsvCell.Number(line.Current.Withdrawals)),
            new("Transactions this month", line => CsvCell.Count(line.Current.Transactions)),
            new("Last month", line => CsvCell.Text(line.LastMonth)),
            new("Money in last month", line => CsvCell.Number(line.Previous.MoneyIn)),
            new("Money out last month", line => CsvCell.Number(line.Previous.MoneyOut)),
            new("Net last month", line => CsvCell.Number(line.Previous.Net)),
            new("Deposits last month", line => CsvCell.Number(line.Previous.Deposits)),
            new("Withdrawals last month", line => CsvCell.Number(line.Previous.Withdrawals)),
            new("Transactions last month", line => CsvCell.Count(line.Previous.Transactions)),
            new("Months in average", line => CsvCell.Count(line.AverageMonths)),
            new("Average money in per month", line => CsvCell.Number(line.AveragePerMonth(flow => flow.MoneyIn))),
            new("Average money out per month", line => CsvCell.Number(line.AveragePerMonth(flow => flow.MoneyOut))),
            new("Average net per month", line => CsvCell.Number(line.AveragePerMonth(flow => flow.Net))),
            new("Average deposits per month", line => CsvCell.Number(line.AveragePerMonth(flow => flow.Deposits))),
            new("Average withdrawals per month", line => CsvCell.Number(line.AveragePerMonth(flow => flow.Withdrawals)))
        };

        public ExportService(UserRepository userRepository,
                             ExportRepository exportRepository,
                             CreditAccountRepository creditAccountRepository,
                             AccountShareRepository accountShareRepository,
                             BankingService bankingService,
                             VirtualAccountService virtualAccountService,
                             ManualTransactionService manualTransactionService,
                             LedgerService ledgerService,
                             TransactionService transactionService)
        {
            _userRepository = userRepository;
            _exportRepository = exportRepository;
            _creditAccountRepository = creditAccountRepository;
            _accountShareRepository = accountShareRepository;
            _bankingService = bankingService;
            _virtualAccountService = virtualAccountService;
            _manualTransactionService = manualTransactionService;
            _ledgerService = ledgerService;
            _transactionService = transactionService;
        }

        public async Task<ExportFile?> ExportAccountsAsync(string sessionId,
                                                           CsvDialect dialect)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            DateTime exportedAt = DateTime.UtcNow;
            ExportMonths months = ExportMonths.At(exportedAt);

            AccountDto.Balance[] balances = await _bankingService.GetAccountsBalancesAsync(sessionId);
            ExportAccountRow[] details = await _exportRepository.GetAccountsAsync(userId.Value);
            CreditAccountDto.CreditAccount[] creditAccounts = await _creditAccountRepository.GetCreditAccountsForUserAsync(userId.Value);
            AccountShareRow[] shares = await _accountShareRepository.GetSharesForUserAsync(userId.Value);
            IReadOnlyList<VirtualAccountDto.VirtualAccount> virtualAccounts = (await _virtualAccountService.GetVirtualAccountsAsync(sessionId))?.VirtualAccounts
                                                                              ?? Array.Empty<VirtualAccountDto.VirtualAccount>();
            ManualTransactionDto.ManualTransaction[] manualTransactions = await _manualTransactionService.GetManualTransactionsAsync(userId.Value);
            LedgerEntry[] entries = await _ledgerService.ClassifyByMonthAsync(userId.Value, months.AverageStart, months.NextMonth.AddDays(-1));

            return BuildAccounts(new AccountsExportInput(exportedAt,
                                                         balances,
                                                         details,
                                                         creditAccounts,
                                                         shares,
                                                         virtualAccounts,
                                                         manualTransactions,
                                                         entries),
                                 dialect);
        }

        public static ExportFile BuildAccounts(AccountsExportInput input,
                                               CsvDialect dialect)
        {
            DateTime exportedAt = input.ExportedAt;
            ExportMonths months = ExportMonths.At(exportedAt);
            DateOnly thisMonth = months.ThisMonth;
            DateOnly nextMonth = months.NextMonth;
            DateOnly lastMonth = months.LastMonth;

            Dictionary<long, ExportAccountRow> details = input.Details.ToDictionary(row => row.Id);
            Dictionary<long, CreditAccountDto.CreditAccount> creditByAccount = input.CreditAccounts.ToDictionary(credit => credit.AccountId);
            IReadOnlyList<AccountShareRow> shares = input.Shares;

            string thisMonthLabel = thisMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            string lastMonthLabel = lastMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);

            List<AccountLine> lines = new();
            Dictionary<long, AccountLine> bankLines = new();
            Dictionary<string, AccountLine> bankLinesByKey = new();

            foreach (AccountDto.Balance account in input.Accounts)
            {
                ExportAccountRow? detail = details.GetValueOrDefault(account.Id);
                CreditAccountDto.CreditAccount? credit = creditByAccount.GetValueOrDefault(account.Id);

                AccountLine line = new()
                {
                    ExportedAt = exportedAt,
                    Kind = "Bank account",
                    Id = account.Id,
                    Name = account.Name ?? "Bank account",
                    NameAtBank = account.OriginalName,
                    Bank = account.BankName,
                    AccountType = RoleLabel(account.Role),
                    AccountTypeSetBy = RoleSourceLabel(account.RoleSource),
                    Iban = account.Iban,
                    Currency = account.Currency,
                    Balance = MainBalance(account.Balances),
                    BookedBalance = BalanceOf(account.Balances, BookedCodes),
                    AvailableBalance = BalanceOf(account.Balances, AvailableCodes),
                    BalanceUpdatedAt = detail?.BalancesFetchedAt,
                    SharedByBank = account.InConsent,
                    BankAccess = detail == null
                        ? null
                        : detail.ConnectionStatus == "ACTIVE" && detail.ConsentExpiresAt > Csv.Local(exportedAt)
                            ? "Connected"
                            : "Needs reconnecting",
                    BankAccessUntil = detail == null ? null : DateOnly.FromDateTime(detail.ConsentExpiresAt),
                    TransactionsSyncedAt = detail?.TransactionsSyncedAt,
                    SharedWith = SharedWith(shares.Where(share => share.AccountId == account.Id)),
                    CreditAccount = credit?.Name,
                    CreditSetup = credit == null ? null : CreditModeLabel(credit.Mode),
                    CreditLimit = credit?.CreditLimit,
                    ThisMonth = thisMonthLabel,
                    LastMonth = lastMonthLabel,
                    HistoryStart = ParseDate(detail?.FirstTransactionOn),
                    PhysicalKey = LedgerService.PhysicalKey(account.Id, detail?.IdentificationHash, account.Iban)
                };

                lines.Add(line);
                bankLines[account.Id] = line;
                bankLinesByKey.TryAdd(line.PhysicalKey, line);
            }

            DateOnly? historyStart = bankLines.Values.Select(line => line.HistoryStart).Where(start => start != null).Min();

            Dictionary<long, AccountLine> virtualLines = new();

            foreach (VirtualAccountDto.VirtualAccount account in input.VirtualAccounts)
            {
                AccountLine line = new()
                {
                    ExportedAt = exportedAt,
                    Kind = "Virtual account",
                    Id = account.Id,
                    Name = account.Name,
                    Bank = "Virtual account",
                    Iban = account.Iban,
                    AccountNumber = account.AccountNumber,
                    ClearingNumber = account.ClearingNumber,
                    Currency = "SEK",
                    Balance = account.CurrentBalance,
                    BalanceUpdatedAt = account.CurrentBalance == null ? null : exportedAt,
                    SharedWith = SharedWith(shares.Where(share => share.VirtualAccountId == account.Id)),
                    Counting = CountingLabel(account.Counting),
                    StartingBalance = account.Balance,
                    StartingBalanceSetAt = account.BalanceSetAt,
                    ThisMonth = thisMonthLabel,
                    LastMonth = lastMonthLabel,
                    HistoryStart = historyStart
                };

                lines.Add(line);
                virtualLines[account.Id] = line;
            }

            foreach (AccountLine line in lines)
                line.SetAverageWindow(AverageFrom(line.HistoryStart, months.AverageStart, thisMonth), thisMonth);

            foreach (LedgerEntry entry in input.Entries)
            {
                if (!entry.Counted) continue;

                if (bankLinesByKey.TryGetValue(entry.PhysicalKey, out AccountLine? bankLine))
                    bankLine.Add(entry.Date, thisMonth, lastMonth, entry.CountedAmount, entry.Amount);

                if (entry.VirtualAccountId is long virtualId && virtualLines.TryGetValue(virtualId, out AccountLine? virtualLine))
                    virtualLine.Add(entry.Date, thisMonth, lastMonth, 0, -entry.Amount);
            }

            foreach (ManualTransactionDto.ManualTransaction manual in input.ManualTransactions)
            {
                if (manual.MatchedTransactionId != null || ParseDate(manual.Date) is not DateOnly date) continue;

                (AccountLine? Line, decimal Moved)[] legs =
                {
                    (manual.FromAccountId is long fromId ? bankLines.GetValueOrDefault(fromId) : null, -manual.Amount),
                    (manual.ToAccountId is long toId ? bankLines.GetValueOrDefault(toId) : null, manual.Amount),
                    (manual.FromVirtualAccountId is long fromVirtualId ? virtualLines.GetValueOrDefault(fromVirtualId) : null, -manual.Amount),
                    (manual.ToVirtualAccountId is long toVirtualId ? virtualLines.GetValueOrDefault(toVirtualId) : null, manual.Amount)
                };

                AccountLine? counter = legs.Select(leg => leg.Line).FirstOrDefault(line => line != null);
                decimal counted = LedgerEngine.CountedByCounting(manual.Amount, manual.Counting);

                foreach ((AccountLine? line, decimal moved) in legs)
                    line?.Add(date, thisMonth, lastMonth, line == counter ? counted : 0, moved);
            }

            foreach ((long accountId, CreditAccountDto.CreditAccount credit) in creditByAccount)
            {
                if (!bankLines.TryGetValue(accountId, out AccountLine? line)) continue;

                decimal used = -input.Entries.Where(entry => entry.PhysicalKey == line.PhysicalKey &&
                                                              entry.Counted &&
                                                              entry.Date >= thisMonth &&
                                                              entry.Date < nextMonth &&
                                                              entry.Currency == line.Currency &&
                                                              entry.Subtype is Subtypes.CardPurchase or Subtypes.Refund)
                                              .Sum(entry => entry.CountedAmount);

                line.CreditUsed = Math.Max(0, used);
                line.CreditLeft = credit.CreditLimit - line.CreditUsed;
            }

            decimal total = lines.Sum(line => line.Balance ?? 0);

            foreach (AccountLine line in lines.Where(line => line.Balance != null))
                line.ShareOfTotal = total > 0 ? line.Balance!.Value / total * 100 : 0;

            string fileName = $"zenith-accounts-{months.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";

            return new ExportFile(fileName, Csv.Write(dialect, AccountColumns, lines));
        }

        public sealed record ExportMonths(DateOnly Today,
                                          DateOnly ThisMonth,
                                          DateOnly NextMonth,
                                          DateOnly LastMonth,
                                          DateOnly AverageStart)
        {
            public static ExportMonths At(DateTime exportedAt)
            {
                DateOnly today = DateOnly.FromDateTime(Csv.Local(exportedAt));
                DateOnly thisMonth = new(today.Year, today.Month, 1);

                return new ExportMonths(today,
                                        thisMonth,
                                        thisMonth.AddMonths(1),
                                        thisMonth.AddMonths(-1),
                                        thisMonth.AddMonths(-AverageMonths));
            }
        }

        public async Task<ExportFile?> ExportTransactionsAsync(string sessionId,
                                                               DateOnly from,
                                                               DateOnly to,
                                                               CsvDialect dialect)
        {
            TransactionDto.TransactionsResponse[]? months = await _transactionService.GetTransactionsByMonthAsync(sessionId, from, to);

            return months == null ? null : BuildTransactions(new TransactionsExportInput(from, to, months), dialect);
        }

        public static ExportFile BuildTransactions(TransactionsExportInput input,
                                                   CsvDialect dialect)
        {
            List<TransactionLine> lines = new();

            foreach (TransactionDto.TransactionsResponse month in input.Months)
            {
                Dictionary<long, TransactionDto.Account> accounts = month.Accounts.GroupBy(account => account.Id)
                                                                                  .ToDictionary(group => group.Key, group => group.First());
                Dictionary<long, TagDto.Tag> tags = month.Tags.GroupBy(tag => tag.Id)
                                                              .ToDictionary(group => group.Key, group => group.First());
                Dictionary<long, string> virtualNames = month.VirtualAccounts.ToDictionary(account => account.Id, account => account.Name);

                foreach (TransactionDto.Transaction transaction in month.Transactions)
                {
                    if (ParseDate(transaction.Date) is not DateOnly date || date < input.From || date > input.To) continue;

                    TransactionDto.Account? account = accounts.GetValueOrDefault(transaction.AccountId);
                    TagDto.Tag? tag = transaction.TagId is long tagId ? tags.GetValueOrDefault(tagId) : null;

                    lines.Add(new TransactionLine
                    {
                        Date = date,
                        BookingDate = ParseDate(transaction.BookingDate),
                        ValueDate = ParseDate(transaction.ValueDate),
                        Status = StatusLabel(transaction.Status),
                        Source = "Bank",
                        TransactionId = transaction.Id.ToString(CultureInfo.InvariantCulture),
                        SortId = transaction.Id,
                        AccountId = transaction.AccountId,
                        Account = account?.Name ?? "Bank account",
                        Bank = account?.BankName,
                        AccountType = account == null ? null : RoleLabel(account.Role),
                        SharedBy = transaction.SharedBy ?? account?.SharedBy,
                        Description = string.IsNullOrWhiteSpace(transaction.Description)
                            ? transaction.BankTransactionCode ?? (transaction.Amount < 0 ? "Payment" : "Deposit")
                            : transaction.Description,
                        Counterparty = transaction.CounterpartyName,
                        CounterpartyAccount = transaction.CounterpartyAccount,
                        Message = string.Join(" | ", transaction.RemittanceInformation.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim())),
                        BankCode = transaction.BankTransactionCode,
                        Amount = transaction.Amount,
                        Currency = transaction.Currency,
                        OriginalAmount = transaction.OriginalAmount,
                        OriginalCurrency = transaction.OriginalAmount == null ? null : transaction.OriginalCurrency,
                        Category = tag?.Name ?? "Uncategorized",
                        CategoryKind = tag == null ? null : CategoryKindLabel(tag.Kind),
                        CategorySetBy = tag == null ? null : CategorySourceLabel(transaction.TagSource),
                        Type = TypeLabel(transaction.Type),
                        Subtype = SubtypeLabel(transaction.Subtype),
                        TypeSetBy = TypeSourceLabel(transaction.TypeSource),
                        CountsAs = EffectLabel(transaction.Effect),
                        CountedAmount = transaction.CountedAmount,
                        CountingSetBy = EffectSourceLabel(transaction.EffectSource),
                        Recurring = transaction.Recurring?.Label,
                        CreditAccount = account?.CreditName,
                        VirtualAccount = transaction.VirtualAccountId is long virtualId ? virtualNames.GetValueOrDefault(virtualId) : null,
                        SharedCost = transaction.Shared?.Status switch
                        {
                            "SHARED" => "Shared",
                            "NOT_VERIFIED" => "Not verified",
                            _ => null
                        },
                        YourShare = transaction.Shared?.MyShare,
                        PaidBy = transaction.Shared?.PaidBy,
                        LinkedTransactionId = transaction.MatchedTransactionId
                    });
                }

                foreach (ManualTransactionDto.ManualTransaction manual in month.ManualTransactions)
                {
                    if (manual.MatchedTransactionId != null || ParseDate(manual.Date) is not DateOnly date || date < input.From || date > input.To) continue;

                    long? accountId = manual.FromAccountId ?? manual.ToAccountId;
                    TransactionDto.Account? account = accountId is long id ? accounts.GetValueOrDefault(id) : null;
                    bool fromAccount = manual.FromAccountId != null;
                    bool sending = manual.FromAccountId != null || manual.FromVirtualAccountId != null;
                    decimal amount = manual.Counting switch
                    {
                        "OUT" => -manual.Amount,
                        "IN" => manual.Amount,
                        _ => sending ? -manual.Amount : manual.Amount
                    };
                    decimal counted = LedgerEngine.CountedByCounting(manual.Amount, manual.Counting);
                    string toLabel = manual.ToName ?? manual.ToAccountNumber ?? "another account";
                    long? virtualAccountId = account == null ? manual.FromVirtualAccountId ?? manual.ToVirtualAccountId : null;

                    lines.Add(new TransactionLine
                    {
                        Date = date,
                        Status = "Added by you",
                        Source = "Manual",
                        TransactionId = "M" + manual.Id.ToString(CultureInfo.InvariantCulture),
                        SortId = manual.Id,
                        AccountId = account == null ? null : accountId,
                        Account = account?.Name,
                        Bank = account?.BankName,
                        AccountType = account == null ? null : RoleLabel(account.Role),
                        SharedBy = account?.SharedBy,
                        Description = string.IsNullOrWhiteSpace(manual.Description) ? $"To {toLabel}" : manual.Description,
                        Counterparty = fromAccount || manual.ToAccountId == null ? manual.ToName : manual.FromName,
                        CounterpartyAccount = fromAccount || manual.ToAccountId == null ? manual.ToAccountNumber : manual.FromAccountNumber,
                        Amount = amount,
                        Currency = manual.Currency,
                        Category = "Added by you",
                        CategorySetBy = "You",
                        Type = manual.Counting switch
                        {
                            "OUT" => TypeLabel(TransactionTypes.Expense),
                            "IN" => TypeLabel(TransactionTypes.Income),
                            _ => TypeLabel(TransactionTypes.Transfer)
                        },
                        Subtype = manual.Counting switch
                        {
                            "OUT" => SubtypeLabel(Subtypes.Purchase),
                            "IN" => SubtypeLabel(Subtypes.OtherIncome),
                            _ => SubtypeLabel(Subtypes.OwnTransfer)
                        },
                        TypeSetBy = "You",
                        CountsAs = EffectLabel(counted < 0 ? Effects.Out : counted > 0 ? Effects.In : Effects.Neutral),
                        CountedAmount = counted,
                        CountingSetBy = "You",
                        CreditAccount = account?.CreditName,
                        VirtualAccount = virtualAccountId is long virtualId ? virtualNames.GetValueOrDefault(virtualId) : null
                    });
                }
            }

            TransactionLine[] ordered = lines.OrderBy(line => line.Date)
                                             .ThenBy(line => line.Source == "Bank" ? 0 : 1)
                                             .ThenBy(line => line.SortId)
                                             .ToArray();

            return new ExportFile($"zenith-transactions-{PeriodName(input.From, input.To)}.csv",
                                  Csv.Write(dialect, TransactionColumns, ordered));
        }

        public static string PeriodName(DateOnly from,
                                        DateOnly to)
        {
            bool monthStart = from.Day == 1;
            bool monthEnd = to.AddDays(1).Day == 1;
            bool yearStart = monthStart && from.Month == 1;
            bool yearEnd = monthEnd && to.Month == 12;

            string Format(DateOnly date, string pattern) => date.ToString(pattern, CultureInfo.InvariantCulture);

            if (yearStart && yearEnd)
                return from.Year == to.Year ? Format(from, "yyyy") : $"{Format(from, "yyyy")}-to-{Format(to, "yyyy")}";

            if (monthStart && monthEnd)
                return from.Year == to.Year && from.Month == to.Month
                    ? Format(from, "yyyy-MM")
                    : $"{Format(from, "yyyy-MM")}-to-{Format(to, "yyyy-MM")}";

            return from == to ? Format(from, "yyyy-MM-dd") : $"{Format(from, "yyyy-MM-dd")}-to-{Format(to, "yyyy-MM-dd")}";
        }

        private static DateOnly? AverageFrom(DateOnly? historyStart,
                                             DateOnly averageStart,
                                             DateOnly thisMonth)
        {
            if (historyStart is not DateOnly start) return null;

            DateOnly firstFullMonth = start.Day == 1
                ? start
                : new DateOnly(start.Year, start.Month, 1).AddMonths(1);

            DateOnly from = firstFullMonth > averageStart ? firstFullMonth : averageStart;

            return from < thisMonth ? from : null;
        }

        private static decimal? MainBalance(IReadOnlyList<EnableBankingEntities.Balance> balances)
        {
            EnableBankingEntities.Balance[] ordered = balances.OrderBy(balance => balance.balanceType, StringComparer.Ordinal)
                                                              .ThenBy(balance => balance.balanceAmount.currency, StringComparer.Ordinal)
                                                              .ToArray();

            EnableBankingEntities.Balance? main = ordered.FirstOrDefault(balance => string.Equals(balance.balanceType, "closingBooked", StringComparison.OrdinalIgnoreCase))
                                                  ?? ordered.FirstOrDefault(balance => string.Equals(balance.balanceType, "expected", StringComparison.OrdinalIgnoreCase))
                                                  ?? ordered.FirstOrDefault();

            return main == null ? null : ParseAmount(main.balanceAmount.amount) ?? 0;
        }

        private static decimal? BalanceOf(IReadOnlyList<EnableBankingEntities.Balance> balances,
                                          string[] codes)
        {
            foreach (string code in codes)
            {
                EnableBankingEntities.Balance? match = balances.FirstOrDefault(balance => BalanceCode(balance.balanceType) == code);

                if (match != null) return ParseAmount(match.balanceAmount.amount);
            }

            return null;
        }

        private static string BalanceCode(string? balanceType)
        {
            string type = balanceType?.Trim() ?? "";

            return BalanceCodes.TryGetValue(type, out string? code) ? code : type.ToUpperInvariant();
        }

        private static decimal? ParseAmount(string? amount)
        {
            return decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : null;
        }

        private static DateOnly? ParseDate(string? date)
        {
            return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value) ? value : null;
        }

        private static string? SharedWith(IEnumerable<AccountShareRow> shares)
        {
            string[] names = shares.Select(share => $"{share.FirstName} {share.LastName}".Trim())
                                   .Where(name => name.Length > 0)
                                   .ToArray();

            return names.Length == 0 ? null : string.Join(", ", names);
        }

        private static string RoleLabel(string role)
        {
            return role switch
            {
                AccountRoles.Savings => "Savings",
                AccountRoles.CreditCard => "Credit card",
                AccountRoles.Loan => "Loan",
                AccountRoles.Investment => "Investment",
                _ => "Everyday"
            };
        }

        private static string RoleSourceLabel(string roleSource)
        {
            return roleSource switch
            {
                "USER" => "You",
                "BANK" => "Bank",
                _ => "Not set"
            };
        }

        private static string CreditModeLabel(string mode)
        {
            return mode switch
            {
                CreditModes.Mirror => "Purchases copied in",
                CreditModes.Card => "It is the card account",
                _ => mode
            };
        }

        private static string CountingLabel(string counting)
        {
            return counting switch
            {
                "IN" => "Money in",
                "OUT" => "Money out",
                "FLOW" => "Money in & out",
                _ => "Neutral"
            };
        }

        private static string StatusLabel(string status)
        {
            return status switch
            {
                "BOOK" => "Booked",
                "PDNG" or "HOLD" => "Pending",
                "SCHD" => "Scheduled",
                "CNCL" => "Cancelled",
                "RJCT" => "Rejected",
                _ => status
            };
        }

        private static string TypeLabel(string type)
        {
            return type switch
            {
                TransactionTypes.Income => "Income",
                TransactionTypes.Expense => "Expense",
                TransactionTypes.Transfer => "Transfer",
                TransactionTypes.Credit => "Debt / credit",
                TransactionTypes.Investment => "Investment",
                _ => type
            };
        }

        private static string SubtypeLabel(string subtype)
        {
            return subtype switch
            {
                Subtypes.Salary => "Salary & benefits",
                Subtypes.OtherIncome => "Other money in",
                Subtypes.Purchase => "Purchase",
                Subtypes.CardPurchase => "Card purchase",
                Subtypes.Refund => "Refund",
                Subtypes.Fee => "Fee",
                Subtypes.OwnTransfer => "Own transfer",
                Subtypes.JointDeposit => "Into shared account",
                Subtypes.JointWithdrawal => "From shared account",
                Subtypes.JointInternal => "Between shared accounts",
                Subtypes.Settlement => "Settlement",
                Subtypes.CardPayment => "Card bill",
                Subtypes.DebtPayment => "Debt payment",
                Subtypes.LoanPayout => "Loan payout",
                Subtypes.InvestmentDeposit => "Into investments",
                Subtypes.InvestmentWithdrawal => "From investments",
                _ => subtype
            };
        }

        private static string TypeSourceLabel(string source)
        {
            return source switch
            {
                TypeSources.User => "You",
                TypeSources.CreditAccount => "Credit account setup",
                TypeSources.TransferMatch => "Matched transfer",
                TypeSources.AccountNumber => "Known account number",
                TypeSources.VirtualAccount => "Virtual account",
                TypeSources.BankCode => "Bank's transfer code",
                TypeSources.Category => "Category",
                _ => "Direction of the money"
            };
        }

        private static string EffectLabel(string effect)
        {
            return effect switch
            {
                Effects.In => "Money in",
                Effects.Out => "Money out",
                _ => "Neutral"
            };
        }

        private static string EffectSourceLabel(string source)
        {
            return source switch
            {
                EffectSources.User => "You",
                EffectSources.Status => "Not counted (status)",
                EffectSources.VirtualAccount => "Virtual account setup",
                _ => "Type"
            };
        }

        private static string? CategorySourceLabel(string? source)
        {
            return source switch
            {
                "AI" => "AI",
                "LEARNED" => "Learned",
                "USER" => "You",
                "RULE" => "Rule",
                _ => null
            };
        }

        private static string CategoryKindLabel(string kind)
        {
            return kind switch
            {
                "INCOME" => "Income",
                "TRANSFER" => "Transfer",
                _ => "Spending"
            };
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }

        private sealed class Flow
        {
            public decimal MoneyIn { get; private set; }
            public decimal MoneyOut { get; private set; }
            public decimal Deposits { get; private set; }
            public decimal Withdrawals { get; private set; }
            public int Transactions { get; private set; }
            public decimal Net => MoneyIn - MoneyOut;

            public void Add(decimal counted,
                            decimal moved)
            {
                MoneyIn += Math.Max(counted, 0);
                MoneyOut += Math.Max(-counted, 0);
                Deposits += Math.Max(moved, 0);
                Withdrawals += Math.Max(-moved, 0);
                Transactions += 1;
            }
        }

        private sealed class TransactionLine
        {
            public DateOnly Date { get; init; }
            public DateOnly? BookingDate { get; init; }
            public DateOnly? ValueDate { get; init; }
            public string Status { get; init; } = "";
            public string Source { get; init; } = "";
            public string TransactionId { get; init; } = "";
            public long SortId { get; init; }
            public long? AccountId { get; init; }
            public string? Account { get; init; }
            public string? Bank { get; init; }
            public string? AccountType { get; init; }
            public string? SharedBy { get; init; }
            public string Description { get; init; } = "";
            public string? Counterparty { get; init; }
            public string? CounterpartyAccount { get; init; }
            public string? Message { get; init; }
            public string? BankCode { get; init; }
            public decimal Amount { get; init; }
            public string Currency { get; init; } = "";
            public decimal? OriginalAmount { get; init; }
            public string? OriginalCurrency { get; init; }
            public string Category { get; init; } = "";
            public string? CategoryKind { get; init; }
            public string? CategorySetBy { get; init; }
            public string Type { get; init; } = "";
            public string Subtype { get; init; } = "";
            public string TypeSetBy { get; init; } = "";
            public string CountsAs { get; init; } = "";
            public decimal CountedAmount { get; init; }
            public string CountingSetBy { get; init; } = "";
            public string? Recurring { get; init; }
            public string? CreditAccount { get; init; }
            public string? VirtualAccount { get; init; }
            public string? SharedCost { get; init; }
            public decimal? YourShare { get; init; }
            public string? PaidBy { get; init; }
            public long? LinkedTransactionId { get; init; }
        }

        private sealed class AccountLine
        {
            public DateTime ExportedAt { get; init; }
            public string Kind { get; init; } = "";
            public long Id { get; init; }
            public string Name { get; init; } = "";
            public string? NameAtBank { get; init; }
            public string Bank { get; init; } = "";
            public string? AccountType { get; init; }
            public string? AccountTypeSetBy { get; init; }
            public string? Iban { get; init; }
            public string? AccountNumber { get; init; }
            public string? ClearingNumber { get; init; }
            public string Currency { get; init; } = "";
            public decimal? Balance { get; init; }
            public decimal? BookedBalance { get; init; }
            public decimal? AvailableBalance { get; init; }
            public DateTime? BalanceUpdatedAt { get; init; }
            public decimal? ShareOfTotal { get; set; }
            public bool? SharedByBank { get; init; }
            public string? BankAccess { get; init; }
            public DateOnly? BankAccessUntil { get; init; }
            public DateTime? TransactionsSyncedAt { get; init; }
            public string? SharedWith { get; init; }
            public string? CreditAccount { get; init; }
            public string? CreditSetup { get; init; }
            public decimal? CreditLimit { get; init; }
            public decimal? CreditUsed { get; set; }
            public decimal? CreditLeft { get; set; }
            public string? Counting { get; init; }
            public decimal? StartingBalance { get; init; }
            public DateTime? StartingBalanceSetAt { get; init; }
            public string ThisMonth { get; init; } = "";
            public string LastMonth { get; init; } = "";
            public DateOnly? HistoryStart { get; init; }
            public string PhysicalKey { get; init; } = "";
            public DateOnly? AverageFrom { get; private set; }
            public Flow Current { get; } = new();
            public Flow Previous { get; } = new();
            public Flow Average { get; } = new();

            public int AverageMonths { get; private set; }

            public void Add(DateOnly date,
                            DateOnly thisMonth,
                            DateOnly lastMonth,
                            decimal counted,
                            decimal moved)
            {
                if (date >= thisMonth && date < thisMonth.AddMonths(1)) Current.Add(counted, moved);
                if (date >= lastMonth && date < thisMonth) Previous.Add(counted, moved);
                if (AverageFrom is DateOnly from && date >= from && date < thisMonth) Average.Add(counted, moved);
            }

            public decimal? AveragePerMonth(Func<Flow, decimal> value)
            {
                return AverageMonths == 0 ? null : value(Average) / AverageMonths;
            }

            public void SetAverageWindow(DateOnly? from,
                                         DateOnly thisMonth)
            {
                AverageFrom = from;
                AverageMonths = from is DateOnly start
                    ? (thisMonth.Year - start.Year) * 12 + thisMonth.Month - start.Month
                    : 0;
            }
        }
    }
}
