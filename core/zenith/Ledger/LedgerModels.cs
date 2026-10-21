namespace ZenithFin.Ledger
{
    public static class TransactionTypes
    {
        public const string Income = "INCOME";
        public const string Expense = "EXPENSE";
        public const string Transfer = "TRANSFER";
        public const string Credit = "CREDIT";
        public const string Investment = "INVESTMENT";

        public static readonly string[] All = { Income, Expense, Transfer, Credit, Investment };
    }

    public static class Subtypes
    {
        public const string Salary = "SALARY";
        public const string OtherIncome = "OTHER_INCOME";

        public const string Purchase = "PURCHASE";
        public const string CardPurchase = "CARD_PURCHASE";
        public const string Refund = "REFUND";
        public const string Fee = "FEE";

        public const string OwnTransfer = "OWN_TRANSFER";
        public const string JointDeposit = "JOINT_DEPOSIT";
        public const string JointWithdrawal = "JOINT_WITHDRAWAL";
        public const string JointInternal = "JOINT_INTERNAL";
        public const string Settlement = "SETTLEMENT";

        public const string CardPayment = "CARD_PAYMENT";
        public const string DebtPayment = "DEBT_PAYMENT";
        public const string LoanPayout = "LOAN_PAYOUT";

        public const string InvestmentDeposit = "INVESTMENT_DEPOSIT";
        public const string InvestmentWithdrawal = "INVESTMENT_WITHDRAWAL";
    }

    public static class Effects
    {
        public const string In = "IN";
        public const string Out = "OUT";
        public const string Neutral = "NEUTRAL";
    }

    public static class TypeSources
    {
        public const string User = "USER";
        public const string CreditAccount = "CREDIT_ACCOUNT";
        public const string TransferMatch = "TRANSFER_MATCH";
        public const string AccountNumber = "ACCOUNT_NUMBER";
        public const string VirtualAccount = "VIRTUAL_ACCOUNT";
        public const string BankCode = "BANK_CODE";
        public const string Category = "CATEGORY";
        public const string Direction = "DIRECTION";
    }

    public static class EffectSources
    {
        public const string User = "USER";
        public const string Type = "TYPE";
        public const string VirtualAccount = "VIRTUAL_ACCOUNT";
        public const string Status = "STATUS";
    }

    public static class AccountRoles
    {
        public const string Everyday = "EVERYDAY";
        public const string Savings = "SAVINGS";
        public const string CreditCard = "CREDIT_CARD";
        public const string Loan = "LOAN";
        public const string Investment = "INVESTMENT";

        public static readonly string[] All = { Everyday, Savings, CreditCard, Loan, Investment };

        public static string FromBankType(string? cashAccountType)
        {
            return cashAccountType?.Trim().ToUpperInvariant() switch
            {
                "SVGS" => Savings,
                "CARD" => CreditCard,
                "LOAN" => Loan,
                _ => Everyday
            };
        }
    }

    public static class CreditModes
    {
        public const string Mirror = "MIRROR";
        public const string Card = "CARD";

        public static readonly string[] All = { Mirror, Card };
    }

    public sealed record LedgerAccount(long Id,
                                       long OwnerUserId,
                                       string PhysicalKey,
                                       string? Iban,
                                       string Name,
                                       string Currency,
                                       string Role,
                                       long? CreditAccountId,
                                       string? CreditName,
                                       string? CreditMode);

    public sealed record LedgerTransaction(long Id,
                                           long AccountId,
                                           DateOnly Date,
                                           decimal Amount,
                                           string Currency,
                                           string Status,
                                           string? CounterpartyName,
                                           string? CounterpartyAccount,
                                           string? BankTransactionCode,
                                           string ExternalId,
                                           string? TagKind,
                                           string? TagKey,
                                           string? TagName,
                                           string? TagSource,
                                           string? CountingOverride,
                                           string? TypeOverride);

    public sealed record LedgerVirtualAccount(long Id,
                                              string Name,
                                              IReadOnlyList<string> Keys,
                                              string Counting);

    public sealed class LedgerScope
    {
        public long ViewerUserId { get; init; }
        public long? PartnerUserId { get; init; }
        public Func<string, DateOnly, bool> IsJoint { get; init; } = (_, _) => false;
    }

    public sealed record LedgerInput(IReadOnlyList<LedgerAccount> Accounts,
                                     IReadOnlyList<LedgerTransaction> Transactions,
                                     IReadOnlyList<LedgerVirtualAccount> VirtualAccounts,
                                     LedgerScope Scope);

    public sealed record LedgerEntry(long TransactionId,
                                     long AccountId,
                                     string PhysicalKey,
                                     DateOnly Date,
                                     decimal Amount,
                                     string Currency,
                                     string Type,
                                     string Subtype,
                                     string TypeSource,
                                     string Reason,
                                     string Effect,
                                     string EffectSource,
                                     decimal CountedAmount,
                                     bool Counted,
                                     long? CounterTransactionId,
                                     string? CounterPhysicalKey,
                                     long? VirtualAccountId,
                                     string AutomaticType,
                                     string AutomaticSubtype,
                                     string AutomaticReason,
                                     string AutomaticEffect,
                                     string TypeEffect);

    public sealed record LedgerResult(IReadOnlyList<LedgerEntry> Entries,
                                      IReadOnlyDictionary<long, long> DuplicateOf);
}
