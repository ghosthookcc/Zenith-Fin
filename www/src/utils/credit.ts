export interface CreditAccountRule {
  id: number;
  name: string;
  accountId: number;
  counting: string;
  creditLimit?: number | null;
}

export const uncountedStatuses = ["CNCL", "RJCT", "SCHD", "MTCH"];

export const countingChoices = [
  {
    value: "OUT",
    label: "Money out",
    hint: "Every transaction on the linked account counts as money out, even money coming in.",
  },
  {
    value: "IN",
    label: "Money in",
    hint: "Every transaction on the linked account counts as money in, even money going out.",
  },
  {
    value: "FLOW",
    label: "Money in & out",
    hint: "Money coming in counts as money in and money going out counts as money out.",
  },
  {
    value: "NEUTRAL",
    label: "Neutral",
    hint: "Its transactions are left out of the month's money in and out.",
  },
];

export const manualCountingChoices: { value: string | null; label: string }[] =
  [
    { value: null, label: "Automatic" },
    { value: "IN", label: "Money in" },
    { value: "OUT", label: "Money out" },
    { value: "NEUTRAL", label: "Neutral" },
  ];

export function countingLabel(counting: string): string {
  return (
    countingChoices.find((choice) => choice.value === counting)?.label ??
    counting
  );
}

export function countedAmount(amount: number, counting: string): number | null {
  switch (counting) {
    case "NEUTRAL":
      return null;
    case "OUT":
      return -Math.abs(amount);
    case "IN":
      return Math.abs(amount);
    default:
      return amount;
  }
}

export function creditUsed(
  transactions: { amount: number; status: string; counting?: string | null }[],
  counting: string,
): number {
  let out = 0;
  let incoming = 0;

  for (const transaction of transactions) {
    if (
      uncountedStatuses.includes(transaction.status) ||
      transaction.counting === "NEUTRAL"
    ) {
      continue;
    }

    const minor = Math.round(Math.abs(transaction.amount) * 100);
    const outgoing = transaction.counting
      ? transaction.counting === "OUT"
      : transaction.amount < 0;

    if (outgoing) {
      out += minor;
    } else {
      incoming += minor;
    }
  }

  return counting === "OUT" || counting === "IN"
    ? out + incoming
    : Math.max(0, out - incoming);
}

export interface VirtualAccountRule {
  id: number;
  name: string;
  accountNumber: string;
  clearingNumber?: string | null;
  iban?: string | null;
  counting: string;
  balance?: number | null;
  balanceSetAt?: string | null;
  currentBalance?: number | null;
}

const minimumMatchDigits = 6;

function significantDigits(value: string | null | undefined): string {
  return (value ?? "").replace(/\D/g, "").replace(/^0+/, "");
}

export function virtualAccountKeys(account: VirtualAccountRule): string[] {
  const keys = [significantDigits(account.accountNumber)];

  if (account.clearingNumber) {
    keys.push(
      significantDigits(`${account.clearingNumber}${account.accountNumber}`),
    );
  }

  const iban = (account.iban ?? "").replace(/[^A-Za-z0-9]/g, "");

  if (iban.length > 4) {
    keys.push(significantDigits(iban.slice(4)));
  }

  return keys.filter((key) => key.length >= minimumMatchDigits);
}

export function matchesVirtualAccount(
  counterpartyAccount: string | null | undefined,
  account: VirtualAccountRule,
): boolean {
  const counterparty = significantDigits(counterpartyAccount);

  if (counterparty.length < minimumMatchDigits) {
    return false;
  }

  return virtualAccountKeys(account).some(
    (key) => counterparty.endsWith(key) || key.endsWith(counterparty),
  );
}

export interface MonthTransaction {
  accountId: number;
  amount: number;
  currency: string;
  status: string;
  counterpartyAccount?: string | null;
  counting?: string | null;
  virtualAccountId?: number;
}

export interface MonthRange {
  from: string;
  to: string;
  name: string;
}

export function currentMonthRange(): MonthRange {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const lastDay = new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate();

  return {
    from: `${now.getFullYear()}-${month}-01`,
    to: `${now.getFullYear()}-${month}-${String(lastDay).padStart(2, "0")}`,
    name: new Intl.DateTimeFormat("en-GB", { month: "long" }).format(now),
  };
}

export function formatKr(minor: number): string {
  return new Intl.NumberFormat("sv-SE", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(minor / 100);
}
