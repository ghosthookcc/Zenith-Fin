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
  };
}
