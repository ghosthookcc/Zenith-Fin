using ZenithFin.Categorization;
using ZenithFin.Utility;

namespace ZenithFin.Ledger
{
    public static class LedgerEngine
    {
        private const int MaxTransferDays = 4;
        private const int MinimumCounterpartyDigits = 5;

        private static readonly string[] UncountedStatuses = { "CNCL", "RJCT", "SCHD" };
        private static readonly string[] DeterministicTagSources = { "RULE", "USER", "LEARNED" };

        private static readonly KeywordPattern[] ExcludedPairCodes = new[] { "SWISH", "KORTKOP", "RANTA", "SKATT", "AVGIFT" }
                                                                     .Select(code => new KeywordPattern(code))
                                                                     .ToArray();

        private sealed record PhysicalAccount(string Key,
                                              LedgerAccount Primary,
                                              IReadOnlyList<long> Owners,
                                              string Role,
                                              string? CreditMode,
                                              string? CreditName);

        private sealed record Decision(string Type,
                                       string Subtype,
                                       string Source,
                                       string Reason,
                                       decimal? Counted);

        public static LedgerResult Classify(LedgerInput input)
        {
            Dictionary<long, LedgerAccount> accounts = input.Accounts.ToDictionary(account => account.Id);
            long viewer = input.Scope.ViewerUserId;

            Dictionary<string, PhysicalAccount> physical = accounts.Values
                                                                   .GroupBy(account => account.PhysicalKey)
                                                                   .ToDictionary(group => group.Key, group => ToPhysical(group.Key, group.ToArray(), viewer));

            Dictionary<long, long> duplicateOf = new();
            List<LedgerTransaction> canonical = new();

            foreach (var group in input.Transactions.Where(transaction => accounts.ContainsKey(transaction.AccountId))
                                                    .GroupBy(transaction => (accounts[transaction.AccountId].PhysicalKey, transaction.ExternalId)))
            {
                LedgerTransaction chosen = group.OrderByDescending(transaction => accounts[transaction.AccountId].OwnerUserId == viewer)
                                                .ThenBy(transaction => transaction.Id)
                                                .First();

                canonical.Add(chosen);

                foreach (LedgerTransaction other in group.Where(other => other.Id != chosen.Id))
                    duplicateOf[other.Id] = chosen.Id;
            }

            AccountNumberIndex index = new(physical.Values.Select(account => (account.Key, account.Primary.Iban)));

            string KeyOf(LedgerTransaction transaction) => accounts[transaction.AccountId].PhysicalKey;

            Dictionary<long, LedgerTransaction> pairs = Pair(canonical, KeyOf, index);

            Dictionary<string, HashSet<string>> mirrorFunders = canonical.Where(transaction => transaction.Amount > 0 &&
                                                                                              physical[KeyOf(transaction)].CreditMode == CreditModes.Mirror)
                                                                         .GroupBy(KeyOf)
                                                                         .ToDictionary(group => group.Key,
                                                                                       group => group.Select(transaction => CounterpartyKey(transaction.CounterpartyAccount))
                                                                                                     .Where(key => key.Length >= MinimumCounterpartyDigits)
                                                                                                     .ToHashSet());

            List<LedgerEntry> entries = new(canonical.Count);

            foreach (LedgerTransaction transaction in canonical.OrderBy(transaction => transaction.Date).ThenBy(transaction => transaction.Id))
            {
                PhysicalAccount own = physical[KeyOf(transaction)];
                LedgerTransaction? counter = pairs.GetValueOrDefault(transaction.Id);
                string? counterKey = counter == null ? null : KeyOf(counter);
                string? numberKey = counter == null ? index.Find(transaction.CounterpartyAccount) : null;

                if (numberKey == own.Key) numberKey = null;

                LedgerVirtualAccount? virtualAccount = counterKey == null && numberKey == null
                    ? input.VirtualAccounts.FirstOrDefault(candidate => VirtualAccountMatcher.Matches(transaction.CounterpartyAccount, candidate.Keys))
                    : null;

                string? otherKey = counterKey ?? numberKey;
                PhysicalAccount? other = otherKey == null ? null : physical[otherKey];

                bool returnsToFunder = transaction.Amount < 0 &&
                                       mirrorFunders.TryGetValue(own.Key, out HashSet<string>? funders) &&
                                       funders.Contains(CounterpartyKey(transaction.CounterpartyAccount));

                Decision decision = Decide(transaction, own, counter, other, virtualAccount, returnsToFunder, input.Scope);
                Decision automatic = transaction.TypeOverride == null
                    ? decision
                    : Decide(transaction with { TypeOverride = null }, own, counter, other, virtualAccount, returnsToFunder, input.Scope);

                bool counted = !UncountedStatuses.Contains(transaction.Status);
                decimal amount;
                string effectSource;

                if (!counted)
                {
                    amount = 0;
                    effectSource = EffectSources.Status;
                }
                else if (transaction.CountingOverride != null)
                {
                    amount = CountedByCounting(transaction.Amount, transaction.CountingOverride);
                    effectSource = EffectSources.User;
                }
                else if (decision.Counted is decimal forced)
                {
                    amount = forced;
                    effectSource = decision.Source == TypeSources.VirtualAccount ? EffectSources.VirtualAccount : EffectSources.Type;
                }
                else
                {
                    amount = DefaultCounted(transaction, decision);
                    effectSource = EffectSources.Type;
                }

                decimal automaticAmount = counted ? DefaultCounted(transaction, automatic) : 0;
                decimal typeAmount = counted ? DefaultCounted(transaction, decision) : 0;

                entries.Add(new LedgerEntry(transaction.Id,
                                            transaction.AccountId,
                                            own.Key,
                                            transaction.Date,
                                            transaction.Amount,
                                            transaction.Currency,
                                            decision.Type,
                                            decision.Subtype,
                                            decision.Source,
                                            decision.Reason,
                                            EffectOf(amount),
                                            effectSource,
                                            amount,
                                            counted,
                                            counter?.Id,
                                            otherKey,
                                            virtualAccount?.Id,
                                            automatic.Type,
                                            automatic.Subtype,
                                            automatic.Reason,
                                            EffectOf(automaticAmount),
                                            EffectOf(typeAmount)));
            }

            return new LedgerResult(entries, duplicateOf);
        }

        private static decimal DefaultCounted(LedgerTransaction transaction,
                                              Decision decision)
        {
            if (decision.Counted is decimal forced) return forced;

            return decision.Type is TransactionTypes.Income or TransactionTypes.Expense ? transaction.Amount : 0;
        }

        private static string EffectOf(decimal amount)
        {
            return amount < 0 ? Effects.Out : amount > 0 ? Effects.In : Effects.Neutral;
        }

        public static decimal CountedByCounting(decimal amount,
                                                string counting)
        {
            return counting switch
            {
                "NEUTRAL" => 0,
                "OUT" => -Math.Abs(amount),
                "IN" => Math.Abs(amount),
                _ => amount
            };
        }

        private static Decision Decide(LedgerTransaction transaction,
                                       PhysicalAccount own,
                                       LedgerTransaction? counter,
                                       PhysicalAccount? other,
                                       LedgerVirtualAccount? virtualAccount,
                                       bool returnsToFunder,
                                       LedgerScope scope)
        {
            bool outgoing = transaction.Amount < 0;
            decimal magnitude = Math.Abs(transaction.Amount);

            if (transaction.TypeOverride != null)
                return new Decision(transaction.TypeOverride,
                                    UserSubtype(transaction, own, other, scope),
                                    TypeSources.User,
                                    "You set the type yourself",
                                    null);

            if (own.CreditMode == CreditModes.Mirror)
            {
                if (!outgoing)
                    return new Decision(TransactionTypes.Expense,
                                        Subtypes.CardPurchase,
                                        TypeSources.CreditAccount,
                                        $"A {own.CreditName} card purchase copied into {Label(own)}, so it counts as spending here",
                                        -magnitude);

                if (other != null || virtualAccount != null || returnsToFunder || HasOwnTransferCode(transaction))
                    return new Decision(TransactionTypes.Expense,
                                        Subtypes.Refund,
                                        TypeSources.CreditAccount,
                                        returnsToFunder && other == null
                                            ? $"A {own.CreditName} refund sent back to the account that paid for the purchase, so it lowers the spending"
                                            : $"A {own.CreditName} refund moved back out of {Label(own)}, so it lowers the spending",
                                        magnitude);

                return new Decision(TransactionTypes.Credit,
                                    Subtypes.CardPayment,
                                    TypeSources.CreditAccount,
                                    $"Pays the {own.CreditName} bill. The purchases already counted when they were copied into {Label(own)}",
                                    0);
            }

            if (own.CreditMode == CreditModes.Card)
            {
                if (outgoing && other == null)
                    return new Decision(TransactionTypes.Expense,
                                        Subtypes.CardPurchase,
                                        TypeSources.CreditAccount,
                                        $"A purchase on {own.CreditName}",
                                        null);

                if (!outgoing)
                {
                    if (other != null || HasOwnTransferCode(transaction))
                        return new Decision(TransactionTypes.Credit,
                                            Subtypes.CardPayment,
                                            TypeSources.CreditAccount,
                                            $"Pays off {own.CreditName}{(other == null ? "" : $" from {Label(other)}")}",
                                            0);

                    return new Decision(TransactionTypes.Expense,
                                        Subtypes.Refund,
                                        TypeSources.CreditAccount,
                                        $"A refund on {own.CreditName}",
                                        null);
                }
            }

            if (other != null)
            {
                string source = counter != null ? TypeSources.TransferMatch : TypeSources.AccountNumber;
                string evidence = counter != null
                    ? $"matched with the transaction on {Label(other)} on {counter.Date:yyyy-MM-dd}"
                    : $"the other account is {Label(other)}";

                return TransferDecision(transaction, own, other, outgoing, source, evidence, scope);
            }

            if (virtualAccount != null)
            {
                (string type, string subtype) = virtualAccount.Counting switch
                {
                    "NEUTRAL" => (TransactionTypes.Transfer, Subtypes.OwnTransfer),
                    "OUT" => (TransactionTypes.Expense, Subtypes.Purchase),
                    "IN" => (TransactionTypes.Income, Subtypes.OtherIncome),
                    _ => outgoing ? (TransactionTypes.Expense, Subtypes.Purchase) : (TransactionTypes.Income, Subtypes.OtherIncome)
                };

                return new Decision(type,
                                    subtype,
                                    TypeSources.VirtualAccount,
                                    $"The other account is your virtual account {virtualAccount.Name}",
                                    CountedByCounting(transaction.Amount, virtualAccount.Counting));
            }

            if (HasOwnTransferCode(transaction))
                return new Decision(TransactionTypes.Transfer,
                                    Subtypes.OwnTransfer,
                                    TypeSources.BankCode,
                                    "Your bank marks it as a transfer between your own accounts",
                                    0);

            if (transaction.TagSource != null && DeterministicTagSources.Contains(transaction.TagSource) && transaction.TagKind != null)
            {
                string by = transaction.TagSource switch
                {
                    "USER" => "You sorted it",
                    "LEARNED" => "Your earlier corrections sorted it",
                    _ => "A rule sorted it"
                };

                if (transaction.TagKind == "TRANSFER")
                    return new Decision(TransactionTypes.Transfer, Subtypes.OwnTransfer, TypeSources.Category, $"{by} as {transaction.TagName}", 0);

                if (transaction.TagKind == "INCOME" && !outgoing)
                    return new Decision(TransactionTypes.Income, Subtypes.Salary, TypeSources.Category, $"{by} as {transaction.TagName}", null);

                if (transaction.TagKey == CategoryRules.Savings)
                    return new Decision(TransactionTypes.Investment,
                                        outgoing ? Subtypes.InvestmentDeposit : Subtypes.InvestmentWithdrawal,
                                        TypeSources.Category,
                                        $"{by} as {transaction.TagName}",
                                        0);

                if (transaction.TagKey == CategoryRules.Fees && outgoing)
                    return new Decision(TransactionTypes.Expense, Subtypes.Fee, TypeSources.Category, $"{by} as {transaction.TagName}", null);

                if (transaction.TagKind == "SPENDING" && !outgoing)
                    return new Decision(TransactionTypes.Expense, Subtypes.Refund, TypeSources.Category, $"{by} as {transaction.TagName}, so money back lowers that spending", null);
            }

            return outgoing
                ? new Decision(TransactionTypes.Expense, Subtypes.Purchase, TypeSources.Direction, "Money that left the account, with nothing showing it went to one of your own accounts", null)
                : new Decision(TransactionTypes.Income, Subtypes.OtherIncome, TypeSources.Direction, "Money that came into the account, with nothing showing it came from one of your own accounts", null);
        }

        private static Decision TransferDecision(LedgerTransaction transaction,
                                                 PhysicalAccount own,
                                                 PhysicalAccount other,
                                                 bool outgoing,
                                                 string source,
                                                 string evidence,
                                                 LedgerScope scope)
        {
            if (other.CreditMode == CreditModes.Mirror)
                return new Decision(TransactionTypes.Transfer,
                                    TransferSubtype(own, other, transaction.Date, outgoing, scope),
                                    source,
                                    outgoing
                                        ? $"Covers a {other.CreditName} card purchase, which counts in {Label(other)}; {evidence}"
                                        : $"A {other.CreditName} refund passed back, which counts in {Label(other)}; {evidence}",
                                    0);

            if (other.CreditMode == CreditModes.Card || other.Role == AccountRoles.CreditCard)
            {
                if (outgoing)
                    return new Decision(TransactionTypes.Credit, Subtypes.CardPayment, source, $"Pays the {other.CreditName ?? Label(other)} bill; {evidence}", 0);
            }

            bool ownInvestment = own.Role == AccountRoles.Investment;
            bool otherInvestment = other.Role == AccountRoles.Investment;

            if (ownInvestment || otherInvestment)
            {
                bool intoInvestment = otherInvestment ? outgoing : !outgoing;

                return new Decision(TransactionTypes.Investment,
                                    intoInvestment ? Subtypes.InvestmentDeposit : Subtypes.InvestmentWithdrawal,
                                    source,
                                    $"{(intoInvestment ? "Money put into" : "Money taken out of")} an investment account; {evidence}",
                                    0);
            }

            bool ownLoan = own.Role == AccountRoles.Loan;
            bool otherLoan = other.Role == AccountRoles.Loan;

            if (ownLoan || otherLoan)
            {
                bool intoLoan = otherLoan ? outgoing : !outgoing;

                return new Decision(TransactionTypes.Credit,
                                    intoLoan ? Subtypes.DebtPayment : Subtypes.LoanPayout,
                                    source,
                                    $"{(intoLoan ? "A loan payment" : "Money paid out from a loan")}; {evidence}",
                                    0);
            }

            string subtype = TransferSubtype(own, other, transaction.Date, outgoing, scope);

            string what = subtype switch
            {
                Subtypes.JointDeposit => "Money put into a shared account",
                Subtypes.JointWithdrawal => "Money taken out of a shared account",
                Subtypes.JointInternal => "Money moved between shared accounts",
                Subtypes.Settlement => "A payment between you and your partner",
                _ => "Money moved between your own accounts"
            };

            return new Decision(TransactionTypes.Transfer, subtype, source, $"{what}; {evidence}", 0);
        }

        private static string TransferSubtype(PhysicalAccount own,
                                              PhysicalAccount? other,
                                              DateOnly date,
                                              bool outgoing,
                                              LedgerScope scope)
        {
            if (other == null) return Subtypes.OwnTransfer;

            bool ownJoint = scope.IsJoint(own.Key, date);
            bool otherJoint = scope.IsJoint(other.Key, date);

            if (ownJoint && otherJoint) return Subtypes.JointInternal;

            if (ownJoint || otherJoint)
            {
                bool intoJoint = otherJoint ? outgoing : !outgoing;

                return intoJoint ? Subtypes.JointDeposit : Subtypes.JointWithdrawal;
            }

            long? ownOwner = own.Owners.Count == 1 ? own.Owners[0] : null;
            long? otherOwner = other.Owners.Count == 1 ? other.Owners[0] : null;

            return ownOwner != null && otherOwner != null && ownOwner != otherOwner
                ? Subtypes.Settlement
                : Subtypes.OwnTransfer;
        }

        private static string UserSubtype(LedgerTransaction transaction,
                                          PhysicalAccount own,
                                          PhysicalAccount? other,
                                          LedgerScope scope)
        {
            bool outgoing = transaction.Amount < 0;

            return transaction.TypeOverride switch
            {
                TransactionTypes.Income => transaction.TagKind == "INCOME" ? Subtypes.Salary : Subtypes.OtherIncome,
                TransactionTypes.Expense => outgoing ? Subtypes.Purchase : Subtypes.Refund,
                TransactionTypes.Transfer => TransferSubtype(own, other, transaction.Date, outgoing, scope),
                TransactionTypes.Credit => outgoing ? Subtypes.DebtPayment : Subtypes.LoanPayout,
                _ => outgoing ? Subtypes.InvestmentDeposit : Subtypes.InvestmentWithdrawal
            };
        }

        private static Dictionary<long, LedgerTransaction> Pair(IReadOnlyList<LedgerTransaction> transactions,
                                                                Func<LedgerTransaction, string> keyOf,
                                                                AccountNumberIndex index)
        {
            Dictionary<long, LedgerTransaction> paired = new();

            foreach (var group in transactions.Where(transaction => transaction.Amount != 0 && !UncountedStatuses.Contains(transaction.Status))
                                              .GroupBy(transaction => (transaction.Currency, Amount: Math.Abs(transaction.Amount))))
            {
                LedgerTransaction[] outgoing = group.Where(transaction => transaction.Amount < 0).ToArray();
                LedgerTransaction[] incoming = group.Where(transaction => transaction.Amount > 0).ToArray();

                if (outgoing.Length == 0 || incoming.Length == 0) continue;

                var candidates = outgoing.SelectMany(leg => incoming.Where(candidate => keyOf(candidate) != keyOf(leg))
                                                                    .Select(candidate => (Outgoing: leg,
                                                                                          Incoming: candidate,
                                                                                          Days: Math.Abs(leg.Date.DayNumber - candidate.Date.DayNumber))))
                                         .Where(pair => pair.Days <= MaxTransferDays)
                                         .Select(pair => (pair.Outgoing, pair.Incoming, pair.Days, Score: Score(pair.Outgoing, pair.Incoming, keyOf, index)))
                                         .Where(pair => pair.Score > 0)
                                         .ToArray();

                foreach (var pair in candidates.Where(pair => pair.Score >= 2)
                                               .OrderByDescending(pair => pair.Score)
                                               .ThenBy(pair => pair.Days)
                                               .ThenBy(pair => pair.Outgoing.Id)
                                               .ThenBy(pair => pair.Incoming.Id))
                {
                    if (paired.ContainsKey(pair.Outgoing.Id) || paired.ContainsKey(pair.Incoming.Id)) continue;

                    paired[pair.Outgoing.Id] = pair.Incoming;
                    paired[pair.Incoming.Id] = pair.Outgoing;
                }

                var weak = candidates.Where(pair => pair.Score == 1 &&
                                                    !paired.ContainsKey(pair.Outgoing.Id) &&
                                                    !paired.ContainsKey(pair.Incoming.Id))
                                     .ToArray();

                foreach (var pair in weak)
                {
                    if (weak.Count(otherPair => otherPair.Outgoing.Id == pair.Outgoing.Id) != 1 ||
                        weak.Count(otherPair => otherPair.Incoming.Id == pair.Incoming.Id) != 1) continue;

                    paired[pair.Outgoing.Id] = pair.Incoming;
                    paired[pair.Incoming.Id] = pair.Outgoing;
                }
            }

            return paired;
        }

        private static int Score(LedgerTransaction outgoing,
                                 LedgerTransaction incoming,
                                 Func<LedgerTransaction, string> keyOf,
                                 AccountNumberIndex index)
        {
            string? outgoingTarget = index.Find(outgoing.CounterpartyAccount);
            string? incomingSource = index.Find(incoming.CounterpartyAccount);

            if (outgoingTarget == keyOf(incoming) || incomingSource == keyOf(outgoing)) return 3;

            if ((outgoingTarget != null && outgoingTarget != keyOf(incoming)) ||
                (incomingSource != null && incomingSource != keyOf(outgoing))) return 0;

            if (HasExcludedCode(outgoing) || HasExcludedCode(incoming)) return 0;

            if (HasOwnTransferCode(outgoing) || HasOwnTransferCode(incoming)) return 2;

            if (HasKnownCounterparty(outgoing) || HasKnownCounterparty(incoming)) return 0;

            return 1;
        }

        private static string CounterpartyKey(string? counterpartyAccount)
        {
            return AccountNumberIndex.Digits(counterpartyAccount).TrimStart('0');
        }

        private static bool HasOwnTransferCode(LedgerTransaction transaction)
        {
            string[] code = TextTokens.Tokenize(transaction.BankTransactionCode);

            return CategoryRules.OwnTransferCodes.Any(pattern => pattern.Matches(code));
        }

        private static bool HasExcludedCode(LedgerTransaction transaction)
        {
            string[] code = TextTokens.Tokenize(transaction.BankTransactionCode);

            return ExcludedPairCodes.Any(pattern => pattern.Matches(code));
        }

        private static bool HasKnownCounterparty(LedgerTransaction transaction)
        {
            return !string.IsNullOrWhiteSpace(transaction.CounterpartyName) ||
                   AccountNumberIndex.Digits(transaction.CounterpartyAccount).Length >= MinimumCounterpartyDigits;
        }

        private static PhysicalAccount ToPhysical(string key,
                                                  IReadOnlyList<LedgerAccount> rows,
                                                  long viewer)
        {
            LedgerAccount primary = rows.OrderByDescending(row => row.OwnerUserId == viewer)
                                        .ThenBy(row => row.Id)
                                        .First();

            LedgerAccount? credit = rows.Where(row => row.CreditMode != null)
                                        .OrderByDescending(row => row.OwnerUserId == viewer)
                                        .ThenBy(row => row.Id)
                                        .FirstOrDefault();

            return new PhysicalAccount(key,
                                       primary,
                                       rows.Select(row => row.OwnerUserId).Distinct().OrderBy(id => id).ToArray(),
                                       primary.Role,
                                       credit?.CreditMode,
                                       credit?.CreditName);
        }

        private static string Label(PhysicalAccount account)
        {
            string digits = AccountNumberIndex.Digits(account.Primary.Iban);

            return digits.Length >= 4 ? $"{account.Primary.Name} ··{digits[^4..]}" : account.Primary.Name;
        }
    }
}
