using System.Globalization;
using System.Text.RegularExpressions;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record ManualTransactionResult(int StatusCode,
                                                 string? Message,
                                                 ManualTransactionDto.ManualTransaction? ManualTransaction = null);

    public sealed class ManualTransactionService
    {
        private readonly ManualTransactionRepository _manualTransactionRepository;
        private readonly TransactionRepository _transactionRepository;
        private readonly VirtualAccountRepository _virtualAccountRepository;
        private readonly UserRepository _userRepository;

        private const decimal MaxAmount = 1_000_000_000_000m;
        private const int MaxDescriptionLength = 140;
        private const int MaxNameLength = 60;
        private const string DefaultCurrency = "SEK";

        private static readonly DateOnly EarliestDate = new(2000, 1, 1);

        private static readonly string[] Countings = { "OUT", "IN", "NEUTRAL" };

        private static readonly Regex AccountNumberFormat = new(@"^[A-Z0-9][A-Z0-9 .-]*$", RegexOptions.Compiled);

        private static readonly ManualTransactionResult Unauthorized = new(StatusCodes.Status401Unauthorized, "No user found for session");

        private sealed record Side(long? AccountId,
                                   long? VirtualAccountId,
                                   string? AccountNumber,
                                   string? Name,
                                   string? Currency);

        private sealed record Linkables(TransactionAccountRow[] Accounts,
                                        VirtualAccountDto.VirtualAccount[] VirtualAccounts);

        public ManualTransactionService(ManualTransactionRepository manualTransactionRepository,
                                        TransactionRepository transactionRepository,
                                        VirtualAccountRepository virtualAccountRepository,
                                        UserRepository userRepository)
        {
            _manualTransactionRepository = manualTransactionRepository;
            _transactionRepository = transactionRepository;
            _virtualAccountRepository = virtualAccountRepository;
            _userRepository = userRepository;
        }

        public async Task<ManualTransactionDto.LinkableAccountsResponse?> GetLinkableAccountsAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            Linkables linkables = await GetLinkablesAsync(userId.Value);

            return new ManualTransactionDto.LinkableAccountsResponse
            {
                Accounts = linkables.Accounts.Select(account => new ManualTransactionDto.LinkableAccount
                {
                    Id = account.Id,
                    Kind = ManualTransactionDto.AccountKind.Connected,
                    Name = ConnectedName(account),
                    BankName = account.BankName,
                    AccountNumber = CanonicalNumber(account),
                    Currency = account.Currency,
                    IsActive = account.IsActive
                }).ToArray(),
                VirtualAccounts = linkables.VirtualAccounts.Select(account => new ManualTransactionDto.LinkableAccount
                {
                    Id = account.Id,
                    Kind = ManualTransactionDto.AccountKind.Virtual,
                    Name = account.Name,
                    AccountNumber = CanonicalNumber(account),
                    Currency = DefaultCurrency,
                    IsActive = true
                }).ToArray()
            };
        }

        public async Task<ManualTransactionDto.ManualTransaction[]> GetManualTransactionsAsync(long userId)
        {
            ManualTransactionDto.ManualTransaction[] transactions = await _manualTransactionRepository.GetManualTransactionsForUserAsync(userId);

            if (transactions.Length == 0) return transactions;

            ManualMatchCandidate[] candidates = await _manualTransactionRepository.GetMatchCandidatesAsync(userId, ManualTransactionMatcher.MaxDayDistance);
            Dictionary<long, long> matches = ManualTransactionMatcher.Match(transactions, candidates);

            foreach (ManualTransactionDto.ManualTransaction transaction in transactions)
            {
                transaction.MatchedTransactionId = matches.TryGetValue(transaction.Id, out long matched) ? matched : null;
            }

            return transactions;
        }

        public async Task<ManualTransactionResult> CreateAsync(string sessionId,
                                                               ManualTransactionDto.CreateManualTransactionRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            if (request.Amount is not decimal requested || requested <= 0) return BadRequest("Enter how much was sent");

            decimal amount = decimal.Round(requested, 2, MidpointRounding.AwayFromZero);

            if (amount <= 0) return BadRequest("Enter how much was sent");
            if (amount > MaxAmount) return BadRequest("That amount is too large");

            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
                return BadRequest("Choose the date the money was sent");

            if (date < EarliestDate || date > today.AddYears(1))
                return BadRequest($"Choose a date between {EarliestDate:yyyy-MM-dd} and {today.AddYears(1):yyyy-MM-dd}");

            string counting = request.Counting?.Trim().ToUpperInvariant() ?? "";

            if (!Countings.Contains(counting)) return BadRequest("Choose money out, money in or neutral");

            string? description = TransactionText.Clean(request.Description);

            if (description?.Length > MaxDescriptionLength)
                return BadRequest($"Descriptions can be at most {MaxDescriptionLength} characters");

            Linkables linkables = await GetLinkablesAsync(userId.Value);

            (Side? from, ManualTransactionResult? fromError) = ResolveSide(request.FromAccountId,
                                                                           request.FromVirtualAccountId,
                                                                           request.FromAccountNumber,
                                                                           false,
                                                                           linkables);

            if (from == null) return fromError!;

            (Side? to, ManualTransactionResult? toError) = ResolveSide(request.ToAccountId,
                                                                       request.ToVirtualAccountId,
                                                                       request.ToAccountNumber,
                                                                       true,
                                                                       linkables);

            if (to == null) return toError!;

            if ((from.AccountId != null && from.AccountId == to.AccountId) ||
                (from.VirtualAccountId != null && from.VirtualAccountId == to.VirtualAccountId))
                return BadRequest("The money has to go to a different account than it came from");

            string? toName = to.Name;

            if (to.AccountId == null && to.VirtualAccountId == null)
            {
                toName = TransactionText.Clean(request.ToName);

                if (toName?.Length > MaxNameLength) return BadRequest($"Names can be at most {MaxNameLength} characters");
            }

            ManualTransactionDto.ManualTransaction transaction = new()
            {
                Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Amount = amount,
                Currency = from.Currency ?? to.Currency ?? DefaultCurrency,
                Description = description,
                Counting = counting,
                FromAccountId = from.AccountId,
                FromVirtualAccountId = from.VirtualAccountId,
                FromAccountNumber = from.AccountNumber,
                FromName = from.Name,
                ToAccountId = to.AccountId,
                ToVirtualAccountId = to.VirtualAccountId,
                ToAccountNumber = to.AccountNumber,
                ToName = toName,
                CreatedAt = DateTime.UtcNow
            };

            transaction.Id = await _manualTransactionRepository.InsertManualTransactionAsync(userId.Value, transaction);

            return new ManualTransactionResult(StatusCodes.Status200OK, null, transaction);
        }

        public async Task<ManualTransactionResult> DeleteAsync(string sessionId,
                                                               long manualTransactionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            return await _manualTransactionRepository.DeleteManualTransactionAsync(userId.Value, manualTransactionId)
                ? new ManualTransactionResult(StatusCodes.Status200OK, null)
                : new ManualTransactionResult(StatusCodes.Status404NotFound, "Transaction not found");
        }

        private static (Side? Side, ManualTransactionResult? Invalid) ResolveSide(long? accountId,
                                                                                long? virtualAccountId,
                                                                                string? accountNumber,
                                                                                bool required,
                                                                                Linkables linkables)
        {
            if (accountId != null && virtualAccountId != null)
                return (null, BadRequest("Choose either one of your accounts or a virtual account, not both"));

            string number = Compact(accountNumber);

            if (accountId != null)
            {
                TransactionAccountRow? account = linkables.Accounts.FirstOrDefault(candidate => candidate.Id == accountId);

                if (account == null) return (null, NotFound("That account isn't one of yours"));

                string? canonical = CanonicalNumber(account);

                if (number.Length > 0 && number != Compact(canonical))
                    return (null, BadRequest($"The account number doesn't belong to {ConnectedName(account)}"));

                return (ConnectedSide(account), null);
            }

            if (virtualAccountId != null)
            {
                VirtualAccountDto.VirtualAccount? account = linkables.VirtualAccounts.FirstOrDefault(candidate => candidate.Id == virtualAccountId);

                if (account == null) return (null, NotFound("That virtual account isn't one of yours"));

                if (number.Length > 0 && !BelongsTo(number, account))
                    return (null, BadRequest($"The account number doesn't belong to {account.Name}"));

                return (VirtualSide(account), null);
            }

            if (number.Length == 0)
            {
                return required
                    ? (null, BadRequest("Choose where the money ended up, or enter its account number"))
                    : (new Side(null, null, null, null, null), null);
            }

            string text = TransactionText.Clean(accountNumber)!.ToUpperInvariant();

            if (number.Length is < 5 or > 34 || !AccountNumberFormat.IsMatch(text))
                return (null, BadRequest("Account numbers have 5 to 34 letters and digits"));

            TransactionAccountRow? ownAccount = linkables.Accounts.FirstOrDefault(candidate => number == Compact(CanonicalNumber(candidate)));

            if (ownAccount != null) return (ConnectedSide(ownAccount), null);

            VirtualAccountDto.VirtualAccount? ownVirtualAccount = linkables.VirtualAccounts.FirstOrDefault(candidate => BelongsTo(number, candidate));

            if (ownVirtualAccount != null) return (VirtualSide(ownVirtualAccount), null);

            return (new Side(null, null, text, null, null), null);
        }

        private static Side ConnectedSide(TransactionAccountRow account)
        {
            return new Side(account.Id, null, CanonicalNumber(account), ConnectedName(account), account.Currency);
        }

        private static Side VirtualSide(VirtualAccountDto.VirtualAccount account)
        {
            return new Side(null, account.Id, CanonicalNumber(account), account.Name, null);
        }

        private static bool BelongsTo(string compactNumber,
                                      VirtualAccountDto.VirtualAccount account)
        {
            return compactNumber == Compact(CanonicalNumber(account)) ||
                   compactNumber == Compact(account.AccountNumber) ||
                   (account.Iban != null && compactNumber == Compact(account.Iban));
        }

        private static string ConnectedName(TransactionAccountRow account)
        {
            return TransactionText.Clean(account.Name) ?? account.BankName;
        }

        private static string? CanonicalNumber(TransactionAccountRow account)
        {
            string iban = Compact(account.Iban);

            return iban.Length == 0 ? null : iban;
        }

        private static string CanonicalNumber(VirtualAccountDto.VirtualAccount account)
        {
            return string.IsNullOrEmpty(account.ClearingNumber)
                ? account.AccountNumber
                : $"{account.ClearingNumber}-{account.AccountNumber}";
        }

        private static string Compact(string? value)
        {
            return new string((value ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private async Task<Linkables> GetLinkablesAsync(long userId)
        {
            return new Linkables(await _transactionRepository.GetAccountsForUserAsync(userId),
                                 await _virtualAccountRepository.GetVirtualAccountsForUserAsync(userId));
        }

        private static ManualTransactionResult BadRequest(string message)
        {
            return new ManualTransactionResult(StatusCodes.Status400BadRequest, message);
        }

        private static ManualTransactionResult NotFound(string message)
        {
            return new ManualTransactionResult(StatusCodes.Status404NotFound, message);
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
