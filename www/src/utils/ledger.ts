import type { ManualTransaction } from "./manual";

export const typeChoices: { value: string; label: string }[] = [
  { value: "INCOME", label: "Income" },
  { value: "EXPENSE", label: "Expense" },
  { value: "TRANSFER", label: "Transfer" },
  { value: "CREDIT", label: "Debt / credit" },
  { value: "INVESTMENT", label: "Investment" },
];

const subtypeLabels: Record<string, string> = {
  SALARY: "Salary & benefits",
  OTHER_INCOME: "Other money in",
  PURCHASE: "Purchase",
  CARD_PURCHASE: "Card purchase",
  REFUND: "Refund",
  FEE: "Fee",
  OWN_TRANSFER: "Own transfer",
  JOINT_DEPOSIT: "Into shared account",
  JOINT_WITHDRAWAL: "From shared account",
  JOINT_INTERNAL: "Between shared accounts",
  SETTLEMENT: "Settlement",
  CARD_PAYMENT: "Card bill",
  DEBT_PAYMENT: "Debt payment",
  LOAN_PAYOUT: "Loan payout",
  INVESTMENT_DEPOSIT: "Into investments",
  INVESTMENT_WITHDRAWAL: "From investments",
};

export function typeLabel(type: string): string {
  return typeChoices.find((choice) => choice.value === type)?.label ?? type;
}

export function subtypeLabel(subtype: string): string {
  return subtypeLabels[subtype] ?? subtype;
}

export function effectLabel(effect: string): string {
  switch (effect) {
    case "IN":
      return "Money in";
    case "OUT":
      return "Money out";
    default:
      return "Neutral";
  }
}

export function manualLedger(transaction: ManualTransaction, amount: number) {
  const matched = transaction.matchedTransactionId != null;
  const counted =
    transaction.counting === "NEUTRAL"
      ? 0
      : transaction.counting === "OUT"
        ? -Math.abs(amount)
        : Math.abs(amount);
  const type =
    transaction.counting === "OUT"
      ? "EXPENSE"
      : transaction.counting === "IN"
        ? "INCOME"
        : "TRANSFER";
  const subtype =
    type === "EXPENSE"
      ? "PURCHASE"
      : type === "INCOME"
        ? "OTHER_INCOME"
        : "OWN_TRANSFER";
  const effect = matched
    ? "NEUTRAL"
    : counted < 0
      ? "OUT"
      : counted > 0
        ? "IN"
        : "NEUTRAL";
  const reason = matched
    ? "Your bank shows this payment now, so the bank's version counts instead"
    : "You added this transaction yourself";

  return {
    type,
    subtype,
    typeSource: "USER",
    typeReason: reason,
    effect,
    effectSource: "USER",
    countedAmount: matched ? 0 : counted,
    counted: !matched,
    automaticType: type,
    automaticSubtype: subtype,
    automaticReason: reason,
    automaticEffect: effect,
    typeEffect: effect,
  };
}

const roleLabels: Record<string, string> = {
  EVERYDAY: "Everyday",
  SAVINGS: "Savings",
  CREDIT_CARD: "Credit card",
  LOAN: "Loan",
  INVESTMENT: "Investment",
};

export const roleChoices = Object.entries(roleLabels).map(([value, label]) => ({
  value,
  label,
}));

export function roleLabel(role: string | null | undefined): string {
  return roleLabels[role ?? "EVERYDAY"] ?? "Everyday";
}

export function accountKind(
  account: { role?: string | null; creditName?: string | null } | undefined,
): string {
  if (!account) {
    return "Account";
  }

  return account.creditName ? "Credit" : roleLabel(account.role);
}

export function categoryOrigin(
  source: string | null | undefined,
): { short: string; long: string } | null {
  switch (source) {
    case "AI":
      return { short: "AI", long: "Automatic (AI)" };
    case "LEARNED":
      return {
        short: "Learned",
        long: "Automatic (learned from your changes)",
      };
    case "USER":
      return { short: "You", long: "Set by you" };
    case "RULE":
      return { short: "Rule", long: "Automatic (rule)" };
    default:
      return null;
  }
}

export function typeOrigin(source: string): string {
  switch (source) {
    case "USER":
      return "Set by you";
    case "CREDIT_ACCOUNT":
      return "Automatic (credit account setup)";
    case "TRANSFER_MATCH":
      return "Automatic (matched transfer)";
    case "ACCOUNT_NUMBER":
      return "Automatic (known account number)";
    case "VIRTUAL_ACCOUNT":
      return "Automatic (virtual account)";
    case "BANK_CODE":
      return "Automatic (the bank's transfer code)";
    case "CATEGORY":
      return "Automatic (category)";
    default:
      return "Automatic (direction of the money)";
  }
}

export function effectOrigin(source: string): string {
  switch (source) {
    case "USER":
      return "Set by you";
    case "STATUS":
      return "Not counted because of its status";
    case "VIRTUAL_ACCOUNT":
      return "Automatic (virtual account setup)";
    default:
      return "Automatic (from the type)";
  }
}
