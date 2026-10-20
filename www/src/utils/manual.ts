import type { MonthTransaction } from "./credit";

export const openManualTransactionEvent = "zenithnewtransaction";
export const manualTransactionsChangedEvent = "zenithtransactionschanged";

export interface ManualTransaction {
  id: number;
  date: string;
  amount: number;
  currency: string;
  description?: string | null;
  counting: string;
  fromAccountId?: number | null;
  fromVirtualAccountId?: number | null;
  fromAccountNumber?: string | null;
  fromName?: string | null;
  toAccountId?: number | null;
  toVirtualAccountId?: number | null;
  toAccountNumber?: string | null;
  toName?: string | null;
  createdAt: string;
  matchedTransactionId?: number | null;
}

export interface LinkableAccount {
  id: number;
  kind: "connected" | "virtual";
  name: string;
  bankName?: string | null;
  accountNumber?: string | null;
  currency: string;
  isActive: boolean;
}

export const manualCountings = [
  { value: "OUT", label: "Money out" },
  { value: "IN", label: "Money in" },
  { value: "NEUTRAL", label: "Neutral" },
];

export function openManualTransaction(): void {
  window.dispatchEvent(new CustomEvent(openManualTransactionEvent));
}

export function manualSignedAmount(
  transaction: ManualTransaction,
  perspective: { accountId?: number | null; virtualAccountId?: number | null },
): number {
  if (transaction.counting === "OUT") {
    return -transaction.amount;
  }

  if (transaction.counting === "IN") {
    return transaction.amount;
  }

  const receiving =
    (perspective.accountId != null &&
      transaction.toAccountId === perspective.accountId) ||
    (perspective.virtualAccountId != null &&
      transaction.toVirtualAccountId === perspective.virtualAccountId);
  const sending =
    transaction.fromAccountId != null ||
    transaction.fromVirtualAccountId != null;

  return receiving || !sending ? transaction.amount : -transaction.amount;
}

export function manualMonthRows(
  transactions: ManualTransaction[],
): MonthTransaction[] {
  const rows: MonthTransaction[] = [];

  for (const transaction of transactions) {
    if (transaction.matchedTransactionId != null) {
      continue;
    }

    const base = {
      currency: transaction.currency,
      status: "BOOK",
      counting: transaction.counting,
    };

    if (transaction.fromAccountId != null) {
      rows.push({
        ...base,
        accountId: transaction.fromAccountId,
        amount: -transaction.amount,
      });
    }

    if (transaction.toAccountId != null) {
      rows.push({
        ...base,
        accountId: transaction.toAccountId,
        amount: transaction.amount,
      });
    }

    if (transaction.fromVirtualAccountId != null) {
      rows.push({
        ...base,
        accountId: 0,
        amount: transaction.amount,
        virtualAccountId: transaction.fromVirtualAccountId,
      });
    }

    if (transaction.toVirtualAccountId != null) {
      rows.push({
        ...base,
        accountId: 0,
        amount: -transaction.amount,
        virtualAccountId: transaction.toVirtualAccountId,
      });
    }
  }

  return rows;
}
