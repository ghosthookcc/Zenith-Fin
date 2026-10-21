import {
  $,
  component$,
  sync$,
  useOnWindow,
  useSignal,
  useStore,
  useVisibleTask$,
} from "@builder.io/qwik";

import {
  CategoryBreakdown,
  type CategoryBreakdownRow,
} from "./CategoryBreakdown";
import { Icon } from "./Icon";
import { accountColors } from "../utils/accounts";
import {
  countingLabel,
  creditModeLabel,
  type CreditAccountRule,
  manualCountingChoices,
  matchesVirtualAccount,
  uncountedStatuses,
  type VirtualAccountRule,
} from "../utils/credit";
import {
  accountKind,
  categoryOrigin,
  effectLabel,
  effectOrigin,
  manualLedger,
  subtypeLabel,
  typeChoices,
  typeLabel,
  typeOrigin,
} from "../utils/ledger";
import {
  type ManualTransaction,
  manualSignedAmount,
  manualTransactionsChangedEvent,
  openManualTransaction,
} from "../utils/manual";

interface CalendarAccount {
  id: number;
  name?: string | null;
  bankName: string;
  iban?: string | null;
  currency: string;
  isActive: boolean;
  transactionsSyncedAt?: string | null;
  isStale: boolean;
  role: string;
  roleSource: string;
  creditName?: string | null;
  creditMode?: string | null;
  sharedBy?: string | null;
}

interface CalendarTransaction {
  id: number;
  accountId: number;
  date: string;
  amount: number;
  currency: string;
  originalAmount?: number | null;
  originalCurrency?: string | null;
  status: string;
  description?: string | null;
  remittanceInformation: string[];
  bankTransactionCode?: string | null;
  counterpartyAccount?: string | null;
  tagId?: number | null;
  tagSource?: string | null;
  counting?: string | null;
  typeOverride?: string | null;
  type: string;
  subtype: string;
  typeSource: string;
  typeReason: string;
  effect: string;
  effectSource: string;
  countedAmount: number;
  counted: boolean;
  matchedTransactionId?: number | null;
  automaticType: string;
  automaticSubtype: string;
  automaticReason: string;
  automaticEffect: string;
  typeEffect: string;
  editable?: boolean;
  sharedBy?: string | null;
  shared?: {
    status: string;
    reason: string;
    myShare?: number | null;
    partnerShare?: number | null;
    paidBy?: string | null;
  } | null;
  recurring?: Recurring | null;
  manual?: ManualTransaction | null;
}

interface Recurring {
  frequency: string;
  label: string;
  occurrences: number;
  next: string;
  manual: boolean;
}

interface CalendarTag {
  id: number;
  key?: string | null;
  name: string;
  icon: string;
  color: string;
  kind: string;
  isSystem: boolean;
}

interface ExpectedSalary {
  month: string;
  amount: number;
}

interface TransactionsResponse {
  from: string;
  to: string;
  accounts: CalendarAccount[];
  tags: CalendarTag[];
  creditAccounts?: CreditAccountRule[];
  virtualAccounts?: VirtualAccountRule[];
  expectedSalaries?: ExpectedSalary[];
  manualTransactions?: ManualTransaction[];
  transactions: CalendarTransaction[];
}

interface SyncResult {
  accountId: number;
  status: string;
  error?: string | null;
}

interface TagUpdate {
  id: number;
  tagId?: number | null;
  tagSource?: string | null;
}

interface CorrectionResponse {
  transactions: TagUpdate[];
  remembered: boolean;
  moved: number;
}

interface Picker {
  id: number | null;
  remember: boolean;
  saving: boolean;
  error: string | null;
}

interface Notice {
  id: number;
  text: string;
}

interface SalaryEditor {
  open: boolean;
  amount: string;
  saving: boolean;
  error: string | null;
}

interface SyncProblem {
  accountId: number | null;
  message: string;
}

interface DayCell {
  date: string;
  day: number;
  inMonth: boolean;
}

interface DaySummary {
  income: number;
  expenses: number;
  otherCurrencies: number;
  transactions: CalendarTransaction[];
}

const weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

const inactiveAccountColor = "#5d6678";
const maskedAmount = "•• •••,••";

const uncategorized: CalendarTag = {
  id: 0,
  key: null,
  name: "Uncategorized",
  icon: "tag",
  color: "#7b8496",
  kind: "SPENDING",
  isSystem: true,
};

const addedByYou: CalendarTag = {
  id: -2_000_000,
  key: null,
  name: "Added by you",
  icon: "pencil",
  color: "#8792a5",
  kind: "SPENDING",
  isSystem: false,
};

function isoDate(year: number, monthIndex: number, day: number): string {
  return new Date(Date.UTC(year, monthIndex, day)).toISOString().slice(0, 10);
}

function addDays(date: string, days: number): string {
  const [year, month, day] = date.split("-").map(Number);

  return isoDate(year, month - 1, day + days);
}

function localToday(): string {
  const now = new Date();

  return isoDate(now.getFullYear(), now.getMonth(), now.getDate());
}

function buildMonthGrid(year: number, monthIndex: number): DayCell[] {
  const leadingDays =
    (new Date(Date.UTC(year, monthIndex, 1)).getUTCDay() + 6) % 7;
  const daysInMonth = new Date(Date.UTC(year, monthIndex + 1, 0)).getUTCDate();
  const cellCount = Math.ceil((leadingDays + daysInMonth) / 7) * 7;

  return Array.from({ length: cellCount }, (_, index) => {
    const date = new Date(Date.UTC(year, monthIndex, index - leadingDays + 1));

    return {
      date: date.toISOString().slice(0, 10),
      day: date.getUTCDate(),
      inMonth: date.getUTCMonth() === monthIndex,
    };
  });
}

function formatDate(date: string, options: Intl.DateTimeFormatOptions): string {
  return new Intl.DateTimeFormat("en-GB", {
    ...options,
    timeZone: "UTC",
  }).format(new Date(`${date}T00:00:00Z`));
}

function formatMoney(amount: number, currency: string, signed = false): string {
  try {
    return new Intl.NumberFormat("sv-SE", {
      style: "currency",
      currency,
      signDisplay: signed ? "exceptZero" : "auto",
    }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currency}`;
  }
}

function formatCompact(minor: number): string {
  return new Intl.NumberFormat("sv-SE", {
    maximumFractionDigits: 0,
    signDisplay: "exceptZero",
  }).format(minor / 100);
}

function accountName(account: CalendarAccount | undefined): string {
  if (!account) {
    return "Unknown account";
  }

  const name = account.name?.trim() || "Bank account";
  const iban = account.iban?.replace(/\s+/g, "");

  return iban ? `${name} ··${iban.slice(-4)}` : name;
}

function accountColorMap(accounts: CalendarAccount[]): Record<number, string> {
  const colors: Record<number, string> = {};
  let index = 0;

  for (const account of [...accounts].sort((a, b) => a.id - b.id)) {
    colors[account.id] = account.isActive
      ? accountColors[index++ % accountColors.length]
      : inactiveAccountColor;
  }

  return colors;
}

function transactionTitle(transaction: CalendarTransaction): string {
  return (
    transaction.description ||
    transaction.bankTransactionCode ||
    (transaction.amount < 0 ? "Payment" : "Deposit")
  );
}

function transactionDetail(
  transaction: CalendarTransaction,
  title: string,
): string | null {
  const lines = transaction.remittanceInformation.filter(
    (line) =>
      /\p{L}/u.test(line) && !line.includes(title) && !title.includes(line),
  );

  return lines.length > 0 ? lines.join(" · ") : null;
}

function statusLabel(status: string): string | null {
  switch (status) {
    case "PDNG":
    case "HOLD":
      return "Pending";
    case "SCHD":
      return "Scheduled";
    case "CNCL":
      return "Cancelled";
    case "RJCT":
      return "Rejected";
    case "MTCH":
      return "Matched";
    default:
      return null;
  }
}

function mostCommonCurrency(
  transactions: CalendarTransaction[],
  fallback: string,
): string {
  const counts: Record<string, number> = {};

  for (const transaction of transactions) {
    counts[transaction.currency] = (counts[transaction.currency] ?? 0) + 1;
  }

  return Object.entries(counts).sort((a, b) => b[1] - a[1])[0]?.[0] ?? fallback;
}

function flowAmount(transaction: CalendarTransaction): number | null {
  return transaction.counted && transaction.countedAmount !== 0
    ? transaction.countedAmount
    : null;
}

function isCounted(transaction: CalendarTransaction): boolean {
  return flowAmount(transaction) !== null;
}

function visibleNeutral(
  transactions: CalendarTransaction[],
  monthPrefix: string,
  filteredTag: number | null,
): number {
  return transactions.filter(
    (transaction) =>
      transaction.date.startsWith(monthPrefix) &&
      !uncountedStatuses.includes(transaction.status) &&
      !isCounted(transaction) &&
      (filteredTag === null ||
        (!transaction.manual &&
          (transaction.tagId ?? uncategorized.id) === filteredTag)),
  ).length;
}

function isSpending(transaction: CalendarTransaction): boolean {
  return (
    transaction.type === "EXPENSE" ||
    (transaction.effectSource === "USER" && transaction.countedAmount < 0)
  );
}

function countingNotice(counting: string | null): string {
  switch (counting) {
    case "NEUTRAL":
      return "Now neutral, so it is left out of your totals.";
    case "OUT":
      return "Now counted as money out.";
    case "IN":
      return "Now counted as money in.";
    default:
      return "Back to automatic counting.";
  }
}

function recurringTitle(recurring: Recurring): string {
  const next = formatDate(recurring.next, { day: "numeric", month: "short" });

  return `Recurring, ${recurring.label.toLowerCase()} · ${recurring.occurrences} ${
    recurring.occurrences === 1 ? "payment" : "payments"
  } so far · next around ${next}${recurring.manual ? " · marked by you" : ""}`;
}

function summarizeDays(
  transactions: CalendarTransaction[],
  currency: string,
  neutralView: boolean,
): Map<string, DaySummary> {
  const days = new Map<string, DaySummary>();

  for (const transaction of transactions) {
    const day = days.get(transaction.date) ?? {
      income: 0,
      expenses: 0,
      otherCurrencies: 0,
      transactions: [],
    };

    day.transactions.push(transaction);

    const amount = neutralView
      ? uncountedStatuses.includes(transaction.status)
        ? null
        : transaction.amount
      : flowAmount(transaction);

    if (amount !== null) {
      const minor = Math.round(amount * 100);

      if (transaction.currency !== currency) {
        day.otherCurrencies += 1;
      } else if (minor < 0) {
        day.expenses -= minor;
      } else {
        day.income += minor;
      }
    }

    days.set(transaction.date, day);
  }

  return days;
}

function spendingByCategory(
  transactions: CalendarTransaction[],
  currency: string,
  tagsById: Record<number, CalendarTag>,
): { tag: CalendarTag; amount: number }[] {
  const totals = new Map<number, { tag: CalendarTag; amount: number }>();

  for (const transaction of transactions) {
    const amount = flowAmount(transaction);

    if (
      transaction.currency !== currency ||
      amount === null ||
      !isSpending(transaction)
    ) {
      continue;
    }

    const bucket = transaction.manual
      ? addedByYou
      : (tagsById[transaction.tagId ?? uncategorized.id] ?? uncategorized);
    const total = totals.get(bucket.id) ?? { tag: bucket, amount: 0 };

    total.amount -= Math.round(amount * 100);
    totals.set(bucket.id, total);
  }

  return Array.from(totals.values())
    .filter((total) => total.amount > 0)
    .sort((a, b) => b.amount - a.amount);
}

function formatShare(part: number, total: number): string {
  const share = total > 0 ? (part / total) * 100 : 0;

  return share > 0 && share < 1 ? "<1%" : `${Math.round(share)}%`;
}

function heatCap(dailyExpenses: number[]): number {
  if (dailyExpenses.length === 0) {
    return 0;
  }

  const sorted = [...dailyExpenses].sort((a, b) => a - b);
  const position = (sorted.length - 1) * 0.9;
  const lower = Math.floor(position);
  const upper = Math.min(lower + 1, sorted.length - 1);

  return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
}

function heatLevel(expenses: number, cap: number): number {
  if (expenses <= 0 || cap <= 0) {
    return 0;
  }

  return Math.min(
    4,
    Math.max(1, Math.ceil(Math.sqrt(Math.min(1, expenses / cap)) * 4)),
  );
}

function dayLabel(
  cell: DayCell,
  summary: DaySummary | undefined,
  currency: string,
  hidden: boolean,
): string {
  const date = formatDate(cell.date, {
    weekday: "long",
    day: "numeric",
    month: "long",
  });
  const count = summary?.transactions.length ?? 0;

  if (count === 0) {
    return `${date}, no transactions`;
  }

  const parts = [`${count} ${count === 1 ? "transaction" : "transactions"}`];

  if (!hidden && summary) {
    if (summary.expenses > 0) {
      parts.push(`spent ${formatMoney(summary.expenses / 100, currency)}`);
    }

    if (summary.income > 0) {
      parts.push(`received ${formatMoney(summary.income / 100, currency)}`);
    }
  }

  return `${date}, ${parts.join(", ")}`;
}

function syncLabel(accounts: CalendarAccount[], now: number): string {
  const active = accounts.filter((account) => account.isActive);

  if (active.length === 0) {
    return "No active bank connections";
  }

  const syncTimes = active.map((account) =>
    account.transactionsSyncedAt
      ? Date.parse(account.transactionsSyncedAt)
      : NaN,
  );

  if (syncTimes.some((time) => Number.isNaN(time))) {
    return "Not synced yet";
  }

  const minutes = Math.floor((now - Math.min(...syncTimes)) / 60_000);

  if (minutes < 1) {
    return "Synced just now";
  }

  if (minutes < 60) {
    return `Synced ${minutes} min ago`;
  }

  if (minutes < 24 * 60) {
    return `Synced ${Math.floor(minutes / 60)} h ago`;
  }

  return `Synced ${formatDate(
    new Date(Math.min(...syncTimes)).toISOString().slice(0, 10),
    {
      day: "numeric",
      month: "short",
    },
  )}`;
}

async function readJson(response: Response): Promise<unknown> {
  const text = await response.text();

  if (!text) {
    return null;
  }

  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`The server returned invalid JSON (${response.status})`);
  }
}

function messageFrom(payload: unknown): string | null {
  if (typeof payload === "string") {
    return payload;
  }

  if (payload && typeof payload === "object" && "message" in payload) {
    const message = (payload as { message?: unknown }).message;

    if (typeof message === "string") {
      return message;
    }
  }

  return null;
}

function isCorrectionResponse(value: unknown): value is CorrectionResponse {
  return (
    !!value &&
    typeof value === "object" &&
    Array.isArray((value as CorrectionResponse).transactions) &&
    typeof (value as CorrectionResponse).moved === "number"
  );
}

function correctionNotice(
  tag: CalendarTag,
  changed: boolean,
  response: CorrectionResponse,
): string {
  const lead = changed ? `Moved to ${tag.name}` : `Kept in ${tag.name}`;

  if (!response.remembered) {
    return `${lead}.`;
  }

  if (response.moved > 0) {
    return `${lead}, along with ${response.moved} similar ${
      response.moved === 1 ? "transaction" : "transactions"
    }. New ones will follow.`;
  }

  return `${lead}. Similar transactions will follow from now on.`;
}

function isTransactionsResponse(value: unknown): value is TransactionsResponse {
  return (
    !!value &&
    typeof value === "object" &&
    typeof (value as TransactionsResponse).from === "string" &&
    typeof (value as TransactionsResponse).to === "string" &&
    Array.isArray((value as TransactionsResponse).accounts) &&
    Array.isArray((value as TransactionsResponse).tags) &&
    Array.isArray((value as TransactionsResponse).transactions)
  );
}

export const TransactionCalendar = component$(() => {
  const today = localToday();
  const [todayYear, todayMonth] = today.split("-").map(Number);

  const viewYear = useSignal(todayYear);
  const viewMonth = useSignal(todayMonth - 1);
  const selectedDate = useSignal<string | null>(null);
  const accountFilter = useSignal("all");
  const categoryFilter = useSignal("all");
  const hidden = useSignal(false);

  const data = useSignal<TransactionsResponse | null>(null);
  const loading = useSignal(true);
  const error = useSignal<string | null>(null);
  const syncing = useSignal(false);
  const syncProblems = useSignal<SyncProblem[]>([]);
  const loadedAt = useSignal(Date.now());
  const latestRequest = useSignal(0);

  const picker = useStore<Picker>({
    id: null,
    remember: true,
    saving: false,
    error: null,
  });
  const notice = useSignal<Notice | null>(null);
  const salaryEditor = useStore<SalaryEditor>({
    open: false,
    amount: "",
    saving: false,
    error: null,
  });

  const load = $(async () => {
    const request = latestRequest.value + 1;

    latestRequest.value = request;
    loading.value = true;
    error.value = null;

    const cells = buildMonthGrid(viewYear.value, viewMonth.value);
    const query = new URLSearchParams({
      from: cells[0].date,
      to: cells[cells.length - 1].date,
    });

    try {
      const response = await fetch(`/api/transactions?${query}`, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ??
            `Failed to fetch transactions (${response.status})`,
        );
      }

      if (!isTransactionsResponse(payload)) {
        throw new Error("Unexpected response from the transaction API.");
      }

      if (request !== latestRequest.value) {
        return null;
      }

      data.value = payload;
      loadedAt.value = Date.now();

      return payload;
    } catch (err) {
      if (request === latestRequest.value) {
        error.value =
          err instanceof Error ? err.message : "Could not load transactions.";
      }

      return null;
    } finally {
      if (request === latestRequest.value) {
        loading.value = false;
      }
    }
  });

  const sync = $(async (force: boolean) => {
    if (syncing.value) {
      return;
    }

    syncing.value = true;

    try {
      const response = await fetch(`/api/transactions/sync?force=${force}`, {
        method: "POST",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ?? `Sync failed (${response.status})`,
        );
      }

      const results =
        (payload as { accounts?: SyncResult[] } | null)?.accounts ?? [];

      syncProblems.value = results
        .filter((result) => result.status === "failed")
        .map((result) => ({
          accountId: result.accountId,
          message: result.error || "Sync failed",
        }));
    } catch (err) {
      syncProblems.value = [
        {
          accountId: null,
          message: err instanceof Error ? err.message : "Sync failed",
        },
      ];
    } finally {
      syncing.value = false;
    }

    await load();
  });

  const showMonth = $(
    async (year: number, monthIndex: number, select: string | null) => {
      viewYear.value = year;
      viewMonth.value = monthIndex;
      selectedDate.value = select;
      picker.id = null;
      notice.value = null;
      salaryEditor.open = false;

      await load();
    },
  );

  useOnWindow(
    manualTransactionsChangedEvent,
    $(async (event: Event) => {
      const date = (event as CustomEvent<{ date?: string } | null>).detail
        ?.date;

      if (date && /^\d{4}-\d{2}-\d{2}$/.test(date)) {
        const [year, month] = date.split("-").map(Number);

        await showMonth(year, month - 1, date);
        return;
      }

      await load();
    }),
  );

  const changeMonth = $(async (offset: number) => {
    const target = new Date(
      Date.UTC(viewYear.value, viewMonth.value + offset, 1),
    );

    await showMonth(target.getUTCFullYear(), target.getUTCMonth(), null);
  });

  const selectDay = $(async (cell: DayCell) => {
    if (!cell.inMonth) {
      const [year, month] = cell.date.split("-").map(Number);

      await showMonth(year, month - 1, cell.date);
      return;
    }

    selectedDate.value = selectedDate.value === cell.date ? null : cell.date;
    picker.id = null;
    notice.value = null;
  });

  const selectCategory = $((id: number) => {
    categoryFilter.value =
      categoryFilter.value === String(id) ? "all" : String(id);
  });

  const openSalaryEditor = $((current: number | null) => {
    salaryEditor.open = true;
    salaryEditor.amount = current === null ? "" : String(current);
    salaryEditor.error = null;
  });

  const updateSalaries = $((month: string, saved: ExpectedSalary | null) => {
    if (!data.value) {
      return;
    }

    const kept = (data.value.expectedSalaries ?? []).filter(
      (entry) => entry.month !== month,
    );

    data.value = {
      ...data.value,
      expectedSalaries: (saved ? [...kept, saved] : kept).sort((a, b) =>
        a.month.localeCompare(b.month),
      ),
    };
  });

  const saveSalary = $(async (month: string) => {
    if (salaryEditor.saving) {
      return;
    }

    const text = salaryEditor.amount
      .trim()
      .replace(/\s+/g, "")
      .replace(",", ".");
    const amount = Number(text);

    if (text === "" || !Number.isFinite(amount) || amount < 0) {
      salaryEditor.error = "Enter the expected salary in kronor";
      return;
    }

    salaryEditor.saving = true;
    salaryEditor.error = null;

    try {
      const response = await fetch(`/api/expected-salaries/${month}`, {
        method: "PUT",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        credentials: "same-origin",
        body: JSON.stringify({ amount }),
      });

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ??
            `Could not save the expected salary (${response.status})`,
        );
      }

      await updateSalaries(month, payload as ExpectedSalary);
      salaryEditor.open = false;
    } catch (err) {
      salaryEditor.error =
        err instanceof Error
          ? err.message
          : "Could not save the expected salary.";
    } finally {
      salaryEditor.saving = false;
    }
  });

  const removeSalary = $(async (month: string) => {
    if (salaryEditor.saving) {
      return;
    }

    salaryEditor.saving = true;
    salaryEditor.error = null;

    try {
      const response = await fetch(`/api/expected-salaries/${month}`, {
        method: "DELETE",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
      });

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ??
            `Could not remove the expected salary (${response.status})`,
        );
      }

      await updateSalaries(month, null);
      salaryEditor.open = false;
    } catch (err) {
      salaryEditor.error =
        err instanceof Error
          ? err.message
          : "Could not remove the expected salary.";
    } finally {
      salaryEditor.saving = false;
    }
  });

  const openPicker = $((id: number) => {
    if (picker.saving) {
      return;
    }

    picker.id = picker.id === id ? null : id;
    picker.remember = true;
    picker.error = null;
    notice.value = null;
  });

  const removeManual = $(async (id: number) => {
    if (picker.saving) {
      return;
    }

    picker.saving = true;
    picker.error = null;

    try {
      const response = await fetch(`/api/manual-transactions/${id}`, {
        method: "DELETE",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      const payload = await readJson(response);

      if (!response.ok && response.status !== 404) {
        throw new Error(
          messageFrom(payload) ??
            `Could not remove the transaction (${response.status})`,
        );
      }

      picker.id = null;

      if (data.value) {
        data.value = {
          ...data.value,
          manualTransactions: (data.value.manualTransactions ?? []).filter(
            (transaction) => transaction.id !== id,
          ),
        };
      }
    } catch (err) {
      picker.error =
        err instanceof Error
          ? err.message
          : "Could not remove the transaction.";
    } finally {
      picker.saving = false;
    }
  });

  const closePicker = $((id: number) => {
    if (picker.saving) {
      return;
    }

    picker.id = null;
    picker.error = null;

    document
      .querySelector<HTMLButtonElement>(`[data-tag-button="${id}"]`)
      ?.focus();
  });

  const changeTag = $(
    async (transaction: CalendarTransaction, tag: CalendarTag) => {
      if (picker.saving) {
        return;
      }

      picker.saving = true;
      picker.error = null;

      try {
        const response = await fetch(
          `/api/transactions/${transaction.id}/tag`,
          {
            method: "PUT",
            headers: {
              Accept: "application/json",
              "Content-Type": "application/json",
            },
            credentials: "same-origin",
            body: JSON.stringify({
              tagId: tag.id,
              remember: picker.remember && !!transaction.description,
            }),
          },
        );

        const payload = await readJson(response);

        if (!response.ok) {
          throw new Error(
            messageFrom(payload) ??
              `Could not change the category (${response.status})`,
          );
        }

        if (!isCorrectionResponse(payload)) {
          throw new Error("Unexpected response from the transaction API.");
        }

        notice.value = {
          id: transaction.id,
          text: correctionNotice(tag, transaction.tagId !== tag.id, payload),
        };
        picker.id = null;

        await load();

        document
          .querySelector<HTMLButtonElement>(
            `[data-tag-button="${transaction.id}"]`,
          )
          ?.focus();
      } catch (err) {
        picker.error =
          err instanceof Error ? err.message : "Could not change the category.";
      } finally {
        picker.saving = false;
      }
    },
  );

  const resetCategory = $(async (transaction: CalendarTransaction) => {
    if (picker.saving) {
      return;
    }

    picker.saving = true;
    picker.error = null;

    try {
      const response = await fetch(`/api/transactions/${transaction.id}/tag`, {
        method: "PUT",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        credentials: "same-origin",
        body: JSON.stringify({ tagId: null, remember: false }),
      });

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ??
            `Could not go back to the automatic category (${response.status})`,
        );
      }

      notice.value = {
        id: transaction.id,
        text: "Back to the automatic category.",
      };

      await load();
    } catch (err) {
      picker.error =
        err instanceof Error
          ? err.message
          : "Could not go back to the automatic category.";
    } finally {
      picker.saving = false;
    }
  });

  const setType = $(
    async (transaction: CalendarTransaction, type: string | null) => {
      if (picker.saving || (transaction.typeOverride ?? null) === type) {
        return;
      }

      picker.saving = true;
      picker.error = null;

      try {
        const response = await fetch(
          `/api/transactions/${transaction.id}/type`,
          {
            method: "PUT",
            headers: {
              Accept: "application/json",
              "Content-Type": "application/json",
            },
            credentials: "same-origin",
            body: JSON.stringify({ type }),
          },
        );

        const payload = await readJson(response);

        if (!response.ok) {
          throw new Error(
            messageFrom(payload) ??
              `Could not change the type (${response.status})`,
          );
        }

        notice.value = {
          id: transaction.id,
          text:
            type === null
              ? "Back to the automatic type."
              : `Now a${type === "INCOME" || type === "EXPENSE" || type === "INVESTMENT" ? "n" : ""} ${typeLabel(type).toLowerCase()}.`,
        };

        await load();
      } catch (err) {
        picker.error =
          err instanceof Error ? err.message : "Could not change the type.";
      } finally {
        picker.saving = false;
      }
    },
  );

  const proposeShare = $(async (transaction: CalendarTransaction) => {
    if (picker.saving) {
      return;
    }

    picker.saving = true;
    picker.error = null;

    try {
      const response = await fetch("/api/sharing/arrangements", {
        method: "POST",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        credentials: "same-origin",
        body: JSON.stringify({ transactionId: transaction.id }),
      });

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ??
            `Could not suggest sharing it (${response.status})`,
        );
      }

      notice.value = {
        id: transaction.id,
        text: "Suggested to your partner. It counts as shared once they accept.",
      };
      picker.id = null;
    } catch (err) {
      picker.error =
        err instanceof Error ? err.message : "Could not suggest sharing it.";
    } finally {
      picker.saving = false;
    }
  });

  const setRecurring = $(
    async (transaction: CalendarTransaction, recurring: boolean) => {
      if (picker.saving) {
        return;
      }

      picker.saving = true;
      picker.error = null;

      try {
        const response = await fetch(
          `/api/transactions/${transaction.id}/recurring`,
          {
            method: "PUT",
            headers: {
              Accept: "application/json",
              "Content-Type": "application/json",
            },
            credentials: "same-origin",
            body: JSON.stringify({ recurring }),
          },
        );

        const payload = await readJson(response);

        if (!response.ok) {
          throw new Error(
            messageFrom(payload) ?? `Could not change it (${response.status})`,
          );
        }

        notice.value = {
          id: transaction.id,
          text: recurring
            ? "Marked as a recurring payment, along with the others like it."
            : "No longer treated as a recurring payment.",
        };

        await load();
      } catch (err) {
        picker.error =
          err instanceof Error ? err.message : "Could not change it.";
      } finally {
        picker.saving = false;
      }
    },
  );

  const setCounting = $(
    async (transaction: CalendarTransaction, counting: string | null) => {
      if (picker.saving || (transaction.counting ?? null) === counting) {
        return;
      }

      picker.saving = true;
      picker.error = null;

      try {
        const response = await fetch(
          `/api/transactions/${transaction.id}/counting`,
          {
            method: "PUT",
            headers: {
              Accept: "application/json",
              "Content-Type": "application/json",
            },
            credentials: "same-origin",
            body: JSON.stringify({ counting }),
          },
        );

        const payload = await readJson(response);

        if (!response.ok) {
          throw new Error(
            messageFrom(payload) ??
              `Could not change how it counts (${response.status})`,
          );
        }

        notice.value = {
          id: transaction.id,
          text: countingNotice(counting),
        };

        await load();
      } catch (err) {
        picker.error =
          err instanceof Error
            ? err.message
            : "Could not change how it counts.";
      } finally {
        picker.saving = false;
      }
    },
  );

  useVisibleTask$(
    async () => {
      const initial = await load();

      if (
        initial?.accounts.some((account) => account.isActive && account.isStale)
      ) {
        await sync(false);
      }
    },
    {
      strategy: "document-ready",
    },
  );

  useVisibleTask$(({ track }) => {
    const id = track(() => picker.id);

    if (id !== null) {
      document
        .querySelector(`[data-tag-button="${id}"]`)
        ?.closest("li")
        ?.scrollIntoView({ block: "nearest" });
    }
  });

  const cells = buildMonthGrid(viewYear.value, viewMonth.value);
  const monthPrefix = `${viewYear.value}-${String(viewMonth.value + 1).padStart(2, "0")}`;
  const monthName = formatDate(`${monthPrefix}-01`, { month: "long" });
  const monthTitle = formatDate(`${monthPrefix}-01`, {
    month: "long",
    year: "numeric",
  });
  const isCurrentMonth = today.startsWith(monthPrefix);

  const accounts = data.value?.accounts ?? [];
  const accountsById: Record<number, CalendarAccount> = Object.fromEntries(
    accounts.map((account) => [account.id, account]),
  );
  const colors = accountColorMap(accounts);

  const covered =
    !!data.value &&
    data.value.from <= cells[0].date &&
    data.value.to >= cells[cells.length - 1].date;

  const tags = data.value?.tags ?? [];
  const tagsById: Record<number, CalendarTag> = Object.fromEntries(
    tags.map((tag) => [tag.id, tag]),
  );
  const creditAccounts = data.value?.creditAccounts ?? [];
  const virtualAccounts = data.value?.virtualAccounts ?? [];

  const filteredCredit = accountFilter.value.startsWith("credit:")
    ? (creditAccounts.find(
        (creditAccount) => `credit:${creditAccount.id}` === accountFilter.value,
      ) ?? null)
    : null;
  const filteredVirtual = accountFilter.value.startsWith("virtual:")
    ? (virtualAccounts.find(
        (virtualAccount) =>
          `virtual:${virtualAccount.id}` === accountFilter.value,
      ) ?? null)
    : null;
  const filteredAccount = filteredCredit
    ? filteredCredit.accountId
    : accountFilter.value === "all" || accountFilter.value.includes(":")
      ? null
      : Number(accountFilter.value);
  const direction =
    categoryFilter.value === "in" ||
    categoryFilter.value === "out" ||
    categoryFilter.value === "neutral"
      ? categoryFilter.value
      : null;
  const showingNeutral = direction === "neutral";
  const filteredTag =
    direction !== null || categoryFilter.value === "all"
      ? null
      : Number(categoryFilter.value);
  const inFlowDirection = (transaction: CalendarTransaction) => {
    const amount = flowAmount(transaction);

    if (showingNeutral) {
      return amount === null && !uncountedStatuses.includes(transaction.status);
    }

    return amount !== null && (direction === "in" ? amount > 0 : amount < 0);
  };

  const manualLabel = (
    accountId: number | null | undefined,
    virtualAccountId: number | null | undefined,
    accountNumber: string | null | undefined,
    name: string | null | undefined,
  ): string | null => {
    if (accountId != null && accountsById[accountId]) {
      return accountName(accountsById[accountId]);
    }

    const virtualAccount =
      virtualAccountId != null
        ? virtualAccounts.find((account) => account.id === virtualAccountId)
        : undefined;

    if (virtualAccount) {
      return virtualAccount.name;
    }

    if (
      name &&
      accountNumber &&
      accountId == null &&
      virtualAccountId == null
    ) {
      return `${name} · ${accountNumber}`;
    }

    return name || accountNumber || null;
  };
  const manualFrom = (transaction: ManualTransaction) =>
    manualLabel(
      transaction.fromAccountId,
      transaction.fromVirtualAccountId,
      transaction.fromAccountNumber,
      transaction.fromName,
    );
  const manualTo = (transaction: ManualTransaction) =>
    manualLabel(
      transaction.toAccountId,
      transaction.toVirtualAccountId,
      transaction.toAccountNumber,
      transaction.toName,
    ) ?? "another account";
  const manualRows: CalendarTransaction[] = (
    covered ? (data.value!.manualTransactions ?? []) : []
  )
    .filter((transaction) =>
      filteredVirtual
        ? transaction.fromVirtualAccountId === filteredVirtual.id ||
          transaction.toVirtualAccountId === filteredVirtual.id
        : filteredAccount === null ||
          transaction.fromAccountId === filteredAccount ||
          transaction.toAccountId === filteredAccount,
    )
    .map((transaction) => ({
      id: -transaction.id,
      accountId:
        filteredAccount !== null && transaction.toAccountId === filteredAccount
          ? filteredAccount
          : (transaction.fromAccountId ?? transaction.toAccountId ?? 0),
      date: transaction.date,
      amount: manualSignedAmount(transaction, {
        accountId: filteredAccount,
        virtualAccountId: filteredVirtual?.id ?? null,
      }),
      currency: transaction.currency,
      status: transaction.matchedTransactionId != null ? "MTCH" : "BOOK",
      description: transaction.description || `To ${manualTo(transaction)}`,
      remittanceInformation: [],
      counting: transaction.counting,
      ...manualLedger(
        transaction,
        manualSignedAmount(transaction, {
          accountId: filteredAccount,
          virtualAccountId: filteredVirtual?.id ?? null,
        }),
      ),
      manual: transaction,
    }));
  const accountTransactions = [
    ...manualRows,
    ...(covered ? data.value!.transactions : []).filter((transaction) =>
      filteredVirtual
        ? matchesVirtualAccount(
            transaction.counterpartyAccount,
            filteredVirtual,
          )
        : filteredAccount === null || transaction.accountId === filteredAccount,
    ),
  ];
  const hasUncategorized = accountTransactions.some(
    (transaction) => !transaction.manual && !transaction.tagId,
  );
  const visible = accountTransactions.filter((transaction) =>
    direction !== null
      ? inFlowDirection(transaction)
      : filteredTag === null ||
        (!transaction.manual &&
          (transaction.tagId ?? uncategorized.id) === filteredTag),
  );
  const monthTransactions = visible.filter((transaction) =>
    transaction.date.startsWith(monthPrefix),
  );
  const currency = mostCommonCurrency(
    monthTransactions,
    (filteredAccount !== null
      ? accountsById[filteredAccount]?.currency
      : undefined) ??
      accounts[0]?.currency ??
      "SEK",
  );
  const days = summarizeDays(visible, currency, showingNeutral);
  const neutralInMonth =
    direction !== null
      ? 0
      : visibleNeutral(accountTransactions, monthPrefix, filteredTag);
  const setByYou = monthTransactions.filter(
    (transaction) =>
      !transaction.manual &&
      (transaction.effectSource === "USER" ||
        transaction.typeSource === "USER"),
  ).length;

  const categorySpending = spendingByCategory(
    accountTransactions.filter((transaction) =>
      transaction.date.startsWith(monthPrefix),
    ),
    currency,
    tagsById,
  );
  const totalSpending = categorySpending.reduce(
    (sum, category) => sum + category.amount,
    0,
  );
  const breakdownRows: CategoryBreakdownRow[] = categorySpending.map(
    ({ tag, amount }) => ({
      id: tag.id,
      selectable: tag.id >= 0,
      name: tag.name,
      icon: tag.icon,
      color: tag.color,
      amountLabel: hidden.value
        ? maskedAmount
        : formatMoney(amount / 100, currency),
      shareLabel: formatShare(amount, totalSpending),
      width:
        categorySpending[0].amount > 0
          ? (amount / categorySpending[0].amount) * 100
          : 0,
    }),
  );

  let income = 0;
  let expenses = 0;
  let otherCurrencies = 0;
  const dailyExpenses: number[] = [];

  for (const [date, day] of days) {
    if (!date.startsWith(monthPrefix)) {
      continue;
    }

    income += day.income;
    expenses += day.expenses;
    otherCurrencies += day.otherCurrencies;

    if (day.expenses > 0) {
      dailyExpenses.push(day.expenses);
    }
  }

  const cap = heatCap(dailyExpenses);

  const salaryVisible =
    accountFilter.value === "all" && categoryFilter.value === "all";
  const expectedEntry = (data.value?.expectedSalaries ?? [])
    .filter((entry) => entry.month <= monthPrefix)
    .at(-1);
  const expectedSalary = expectedEntry
    ? Math.round(expectedEntry.amount * 100)
    : null;
  const incomeTagIds = tags
    .filter((tag) => tag.kind === "INCOME")
    .map((tag) => tag.id);
  const salaryIn = monthTransactions.reduce((sum, transaction) => {
    if (
      transaction.currency !== currency ||
      uncountedStatuses.includes(transaction.status) ||
      !incomeTagIds.includes(transaction.tagId ?? uncategorized.id)
    ) {
      return sum;
    }

    const amount = flowAmount(transaction);

    return amount !== null && amount > 0 && transaction.type === "INCOME"
      ? sum + Math.round(amount * 100)
      : sum;
  }, 0);
  const salaryDifference =
    expectedSalary === null ? null : salaryIn - expectedSalary;

  const money = (minor: number, signed = false) =>
    hidden.value ? maskedAmount : formatMoney(minor / 100, currency, signed);

  const focusDate =
    selectedDate.value && cells.some((cell) => cell.date === selectedDate.value)
      ? selectedDate.value
      : isCurrentMonth
        ? today
        : `${monthPrefix}-01`;

  const detailDays = selectedDate.value
    ? [{ date: selectedDate.value, summary: days.get(selectedDate.value) }]
    : Array.from(days.entries())
        .filter(([date]) => date.startsWith(monthPrefix))
        .sort(([a], [b]) => b.localeCompare(a))
        .map(([date, summary]) => ({ date, summary }));
  const detailCount = detailDays.reduce(
    (count, day) => count + (day.summary?.transactions.length ?? 0),
    0,
  );
  const detailNet = detailDays.reduce(
    (sum, day) =>
      sum + (day.summary ? day.summary.income - day.summary.expenses : 0),
    0,
  );

  const neverSynced = accounts.every(
    (account) => !account.transactionsSyncedAt,
  );

  return (
    <main id="main" class="page-shell cal-shell">
      <section class="page-heading" aria-labelledby="page-title">
        <div>
          <h1 id="page-title">
            Calendar<span>.</span>
          </h1>
          <p class="page-subtitle">
            Every transaction across your accounts, day by day.
          </p>
        </div>

        <div class="cal-actions">
          <span class="cal-sync-label" aria-live="polite">
            {syncing.value
              ? "Syncing with your banks…"
              : data.value
                ? syncLabel(accounts, loadedAt.value)
                : ""}
          </span>

          <button
            type="button"
            class="visibility-button"
            disabled={syncing.value || !data.value}
            onClick$={() => sync(true)}
          >
            <Icon name="refresh" class={syncing.value ? "cal-spin" : ""} />
            Refresh
          </button>

          <button
            type="button"
            class="visibility-button"
            aria-pressed={hidden.value}
            onClick$={() => {
              hidden.value = !hidden.value;
            }}
          >
            <Icon name={hidden.value ? "eyeoff" : "eye"} />
            {hidden.value ? "Show amounts" : "Hide amounts"}
          </button>

          <button
            type="button"
            class="visibility-button cal-new-button"
            onClick$={() => openManualTransaction()}
          >
            <Icon name="plus" />
            New transaction
          </button>
        </div>
      </section>

      {syncProblems.value.length > 0 && (
        <div class="cal-alert" role="status">
          <Icon name="info" />
          <div>
            <strong>Some accounts could not be refreshed.</strong>
            <ul>
              {syncProblems.value.map((problem) => (
                <li key={String(problem.accountId)}>
                  {problem.accountId === null
                    ? "All accounts"
                    : `${accountName(accountsById[problem.accountId])} (${
                        accountsById[problem.accountId]?.bankName ??
                        "unknown bank"
                      })`}
                  : {problem.message}
                </li>
              ))}
            </ul>
            <span>Showing the transactions synced so far.</span>
          </div>
        </div>
      )}

      <div class="cal-toolbar">
        <div class="cal-month-nav">
          <button
            type="button"
            class="cal-icon-button"
            aria-label="Previous month"
            onClick$={() => changeMonth(-1)}
          >
            <Icon name="chevronLeft" />
          </button>

          <h2 id="cal-month-title" class="cal-month-title" aria-live="polite">
            {monthTitle}
          </h2>

          <button
            type="button"
            class="cal-icon-button"
            aria-label="Next month"
            onClick$={() => changeMonth(1)}
          >
            <Icon name="chevronRight" />
          </button>

          <button
            type="button"
            class="cal-chip"
            disabled={selectedDate.value === today}
            onClick$={() => showMonth(todayYear, todayMonth - 1, today)}
          >
            Today
          </button>
        </div>

        <label class="cal-select-wrap">
          <span class="cal-visually-hidden">Account</span>
          <select
            class="cal-select"
            onChange$={(_, element) => {
              accountFilter.value = element.value;
            }}
          >
            <option value="all" selected={accountFilter.value === "all"}>
              All accounts
            </option>
            {accounts.map((account) => (
              <option
                key={account.id}
                value={String(account.id)}
                selected={accountFilter.value === String(account.id)}
              >
                {`${accountName(account)} · ${account.bankName}${
                  account.isActive ? "" : " (disconnected)"
                }`}
              </option>
            ))}
            {creditAccounts.length > 0 && (
              <optgroup label="Credit accounts">
                {creditAccounts.map((creditAccount) => (
                  <option
                    key={`credit-${creditAccount.id}`}
                    value={`credit:${creditAccount.id}`}
                    selected={
                      accountFilter.value === `credit:${creditAccount.id}`
                    }
                  >
                    {`${creditAccount.name} · ${creditModeLabel(creditAccount.mode)}`}
                  </option>
                ))}
              </optgroup>
            )}
            {virtualAccounts.length > 0 && (
              <optgroup label="Virtual accounts">
                {virtualAccounts.map((virtualAccount) => (
                  <option
                    key={`virtual-${virtualAccount.id}`}
                    value={`virtual:${virtualAccount.id}`}
                    selected={
                      accountFilter.value === `virtual:${virtualAccount.id}`
                    }
                  >
                    {`${virtualAccount.name} · ${countingLabel(virtualAccount.counting)}`}
                  </option>
                ))}
              </optgroup>
            )}
          </select>
          <Icon name="chevronDown" class="cal-select-icon" />
        </label>

        <label class="cal-select-wrap">
          <span class="cal-visually-hidden">Category</span>
          <select
            class="cal-select cal-select-category"
            onChange$={(_, element) => {
              categoryFilter.value = element.value;
            }}
          >
            <option value="all" selected={categoryFilter.value === "all"}>
              All categories
            </option>
            <option value="in" selected={categoryFilter.value === "in"}>
              Money in
            </option>
            <option value="out" selected={categoryFilter.value === "out"}>
              Money out
            </option>
            <option
              value="neutral"
              selected={categoryFilter.value === "neutral"}
            >
              Neutral
            </option>
            <optgroup label="Categories">
              {tags.map((tag) => (
                <option
                  key={tag.id}
                  value={String(tag.id)}
                  selected={categoryFilter.value === String(tag.id)}
                >
                  {tag.name}
                </option>
              ))}
              {hasUncategorized && (
                <option
                  value={String(uncategorized.id)}
                  selected={categoryFilter.value === String(uncategorized.id)}
                >
                  {uncategorized.name}
                </option>
              )}
            </optgroup>
          </select>
          <Icon name="chevronDown" class="cal-select-icon" />
        </label>
      </div>

      <section
        class={{
          "overview-panel": true,
          "cal-panel": true,
          "cal-busy": loading.value,
        }}
        aria-labelledby="cal-month-title"
        aria-busy={loading.value}
      >
        <div class="cal-summary">
          <div class="cal-stat">
            <span class="cal-stat-label">
              <span class="cal-key cal-key-income" aria-hidden="true" />
              Money in
            </span>
            <strong class="cal-stat-value">{money(income, true)}</strong>
          </div>

          <div class="cal-stat">
            <span class="cal-stat-label">
              <span class="cal-key cal-key-expense" aria-hidden="true" />
              Money out
            </span>
            <strong class="cal-stat-value">{money(-expenses, true)}</strong>
          </div>

          <div class="cal-stat">
            <span class="cal-stat-label">Net</span>
            <strong class="cal-stat-value">
              {money(income - expenses, true)}
            </strong>
          </div>

          <span class="soft-badge cal-count">
            {monthTransactions.length}{" "}
            {monthTransactions.length === 1 ? "transaction" : "transactions"}
          </span>
        </div>

        {salaryVisible && covered && (
          <div class="cal-salary">
            <div class="cal-stat">
              <span class="cal-stat-label">Income this month</span>
              <strong class="cal-stat-value cal-salary-value">
                {money(salaryIn)}
              </strong>
              <span class="cal-salary-detail">
                {salaryDifference === null || expectedSalary === null
                  ? "No expected salary set"
                  : salaryDifference === 0
                    ? `Exactly the expected ${money(expectedSalary)}`
                    : `${money(Math.abs(salaryDifference))} ${
                        salaryDifference > 0 ? "more" : "less"
                      } than the expected ${money(expectedSalary)}`}
              </span>
            </div>

            <div class="cal-stat">
              <span class="cal-stat-label">Left after money out</span>
              <strong class="cal-stat-value cal-salary-value">
                {money(salaryIn - expenses, true)}
              </strong>
              <span class="cal-salary-detail">
                Income minus this month's money out
              </span>
            </div>

            <div class="cal-salary-expected">
              {salaryEditor.open ? (
                <form
                  class="cal-salary-form"
                  preventdefault:submit
                  onSubmit$={() => saveSalary(monthPrefix)}
                >
                  <label for="cal-salary-input">
                    Expected salary from {monthName}
                  </label>
                  <input
                    id="cal-salary-input"
                    class="cal-salary-input"
                    type="text"
                    inputMode="decimal"
                    placeholder="For example 32 000"
                    value={salaryEditor.amount}
                    onInput$={(_, element) => {
                      salaryEditor.amount = element.value;
                    }}
                  />
                  <button
                    type="submit"
                    class="cal-chip"
                    disabled={salaryEditor.saving}
                  >
                    {salaryEditor.saving ? "Saving…" : "Save"}
                  </button>
                  <button
                    type="button"
                    class="cal-chip"
                    onClick$={() => {
                      salaryEditor.open = false;
                    }}
                  >
                    Cancel
                  </button>
                  {expectedEntry?.month === monthPrefix && (
                    <button
                      type="button"
                      class="cal-chip"
                      disabled={salaryEditor.saving}
                      onClick$={() => removeSalary(monthPrefix)}
                    >
                      Remove
                    </button>
                  )}
                  {salaryEditor.error && (
                    <p class="cal-salary-error" role="alert">
                      {salaryEditor.error}
                    </p>
                  )}
                </form>
              ) : (
                <button
                  type="button"
                  class="cal-chip"
                  onClick$={() =>
                    openSalaryEditor(
                      expectedEntry ? expectedEntry.amount : null,
                    )
                  }
                >
                  {expectedSalary === null
                    ? "Set expected salary"
                    : `Expected salary ${money(expectedSalary)} · Edit`}
                </button>
              )}
            </div>
          </div>
        )}

        {showingNeutral && (
          <p class="cal-note">
            Neutral transactions are not part of your monthly totals. The
            figures above only show what moved.
          </p>
        )}

        {neutralInMonth > 0 && (
          <p class="cal-note">
            {neutralInMonth}{" "}
            {neutralInMonth === 1 ? "transaction is" : "transactions are"}{" "}
            neutral, such as transfers between your accounts, card bills and
            investments, and {neutralInMonth === 1 ? "is" : "are"} left out of
            these totals.
          </p>
        )}

        {setByYou > 0 && (
          <p class="cal-note">
            {setByYou} {setByYou === 1 ? "transaction is" : "transactions are"}{" "}
            counted the way you set {setByYou === 1 ? "it" : "them"}.
          </p>
        )}

        {otherCurrencies > 0 && (
          <p class="cal-note">
            Totals are in {currency}. {otherCurrencies}{" "}
            {otherCurrencies === 1 ? "transaction" : "transactions"} in other
            currencies are listed but not added up.
          </p>
        )}

        <div class="cal-body">
          <div class="cal-grid-area">
            <div class="cal-weekdays" aria-hidden="true">
              {weekdays.map((weekday) => (
                <span key={weekday}>{weekday}</span>
              ))}
            </div>

            <div
              class="cal-grid"
              role="group"
              aria-label={`Days in ${monthTitle}`}
              onKeyDown$={[
                sync$((event: KeyboardEvent) => {
                  if (event.key.startsWith("Arrow")) {
                    event.preventDefault();
                  }
                }),
                $((event: KeyboardEvent) => {
                  const offsets: Record<string, number> = {
                    ArrowLeft: -1,
                    ArrowRight: 1,
                    ArrowUp: -7,
                    ArrowDown: 7,
                  };
                  const offset = offsets[event.key];
                  const date = (event.target as HTMLElement | null)?.dataset
                    .date;

                  if (!offset || !date) {
                    return;
                  }

                  document
                    .querySelector<HTMLButtonElement>(
                      `.cal-day[data-date="${addDays(date, offset)}"]`,
                    )
                    ?.focus();
                }),
              ]}
            >
              {cells.map((cell) => {
                const summary = days.get(cell.date);
                const level = cell.inMonth
                  ? heatLevel(summary?.expenses ?? 0, cap)
                  : 0;

                return (
                  <button
                    key={cell.date}
                    type="button"
                    data-date={cell.date}
                    data-heat={level > 0 ? level : undefined}
                    class={{
                      "cal-day": true,
                      "cal-day-outside": !cell.inMonth,
                      "cal-day-today": cell.date === today,
                      "cal-day-selected": cell.date === selectedDate.value,
                    }}
                    tabIndex={cell.date === focusDate ? 0 : -1}
                    aria-pressed={cell.date === selectedDate.value}
                    aria-label={dayLabel(cell, summary, currency, hidden.value)}
                    onClick$={() => selectDay(cell)}
                  >
                    <span class="cal-day-number">{cell.day}</span>

                    {summary &&
                      (summary.expenses > 0 || summary.income > 0) && (
                        <span class="cal-day-amounts" aria-hidden="true">
                          {summary.expenses > 0 && (
                            <span class="cal-day-out">
                              {hidden.value
                                ? "•••"
                                : formatCompact(-summary.expenses)}
                            </span>
                          )}
                          {summary.income > 0 && (
                            <span class="cal-day-in">
                              <span class="cal-dot" />
                              <span class="cal-day-in-value">
                                {hidden.value
                                  ? "•••"
                                  : formatCompact(summary.income)}
                              </span>
                            </span>
                          )}
                        </span>
                      )}
                  </button>
                );
              })}
            </div>

            <div class="cal-legend" aria-hidden="true">
              <span class="cal-legend-scale">
                Money out per day
                <span class="cal-legend-swatches">
                  Less
                  <i class="cal-swatch" />
                  <i class="cal-swatch" data-heat="1" />
                  <i class="cal-swatch" data-heat="2" />
                  <i class="cal-swatch" data-heat="3" />
                  <i class="cal-swatch" data-heat="4" />
                  More
                </span>
              </span>
              <span class="cal-legend-income">
                <span class="cal-dot" />
                Money in
              </span>
            </div>
          </div>

          <aside class="cal-detail" aria-labelledby="cal-detail-title">
            <div class="cal-detail-head">
              <div>
                <h3 id="cal-detail-title">
                  {selectedDate.value
                    ? formatDate(selectedDate.value, {
                        weekday: "long",
                        day: "numeric",
                        month: "long",
                      })
                    : `All of ${monthName}`}
                </h3>
                <p>
                  {detailCount}{" "}
                  {detailCount === 1 ? "transaction" : "transactions"}
                  {detailCount > 0 && ` · net ${money(detailNet, true)}`}
                </p>
              </div>

              {selectedDate.value && (
                <button
                  type="button"
                  class="cal-chip"
                  onClick$={() => {
                    selectedDate.value = null;
                  }}
                >
                  Whole month
                </button>
              )}
            </div>

            <p class="cal-visually-hidden" aria-live="polite">
              {notice.value?.text ?? ""}
            </p>

            {error.value ? (
              <div class="cal-empty" role="alert">
                <p>Could not load transactions: {error.value}</p>
                <button type="button" class="cal-chip" onClick$={load}>
                  Try again
                </button>
              </div>
            ) : !covered ? (
              <p class="cal-empty">Loading transactions…</p>
            ) : accounts.length === 0 ? (
              <p class="cal-empty">
                No bank accounts connected yet. Connect a bank to see its
                transactions here.
              </p>
            ) : detailCount === 0 && syncing.value && neverSynced ? (
              <p class="cal-empty">
                Fetching a year of transactions from your banks. The first sync
                can take a minute.
              </p>
            ) : detailCount === 0 ? (
              <p class="cal-empty">
                {selectedDate.value
                  ? "No transactions on this day."
                  : `No transactions in ${monthName}.`}
              </p>
            ) : (
              <div class="cal-detail-list">
                {detailDays.map(({ date, summary }) =>
                  summary ? (
                    <section key={date} class="cal-daygroup">
                      {!selectedDate.value && (
                        <h4 class="cal-daygroup-head">
                          <span>
                            {formatDate(date, {
                              weekday: "short",
                              day: "numeric",
                              month: "short",
                            })}
                          </span>
                          {(summary.income > 0 || summary.expenses > 0) && (
                            <span>
                              {money(summary.income - summary.expenses, true)}
                            </span>
                          )}
                        </h4>
                      )}

                      <ul class="cal-tx-list">
                        {summary.transactions.map((transaction) => {
                          const title = transactionTitle(transaction);
                          const detail = transactionDetail(transaction, title);
                          const status = statusLabel(transaction.status);
                          const account = accountsById[transaction.accountId];
                          const manual = transaction.manual ?? null;
                          const tag = manual
                            ? addedByYou
                            : (tagsById[
                                transaction.tagId ?? uncategorized.id
                              ] ?? uncategorized);
                          const pickerOpen = picker.id === transaction.id;
                          const rowNotice =
                            notice.value?.id === transaction.id
                              ? notice.value.text
                              : null;

                          return (
                            <li
                              key={transaction.id}
                              class={{
                                "cal-tx": true,
                                "cal-tx-open": pickerOpen,
                              }}
                              onKeyDown$={(event) => {
                                if (event.key === "Escape" && pickerOpen) {
                                  closePicker(transaction.id);
                                }
                              }}
                            >
                              <button
                                type="button"
                                class="cal-tag-tile cal-tag-button"
                                style={{ "--tag-color": tag.color }}
                                data-tag-button={transaction.id}
                                title={
                                  manual ? "Show details" : "Change category"
                                }
                                aria-label={
                                  manual
                                    ? `${tag.name}. Show details`
                                    : `${tag.name}. Change category`
                                }
                                aria-expanded={pickerOpen}
                                aria-controls={
                                  pickerOpen
                                    ? `cal-picker-${transaction.id}`
                                    : undefined
                                }
                                onClick$={() => openPicker(transaction.id)}
                              >
                                <Icon key={tag.icon} name={tag.icon} />
                              </button>

                              <span class="cal-tx-main">
                                <span class="cal-tx-title" title={title}>
                                  {title}
                                </span>
                                {manual ? (
                                  <span class="cal-tx-meta">
                                    <span
                                      class="cal-ai-badge cal-added-badge"
                                      title="You added this transaction yourself"
                                    >
                                      Added by you
                                    </span>
                                    <span
                                      class={`cal-ai-badge cal-type-badge cal-type-${transaction.effect.toLowerCase()}`}
                                      title={`${typeLabel(transaction.type)} · ${effectLabel(transaction.effect)}. ${transaction.typeReason}.`}
                                    >
                                      {subtypeLabel(transaction.subtype)}
                                    </span>
                                    {" · "}
                                    {manualFrom(manual)
                                      ? `${manualFrom(manual)} → ${manualTo(manual)}`
                                      : `To ${manualTo(manual)}`}
                                  </span>
                                ) : (
                                  <span class="cal-tx-meta">
                                    <span
                                      class="cal-account-dot"
                                      style={{
                                        backgroundColor:
                                          colors[transaction.accountId] ??
                                          inactiveAccountColor,
                                      }}
                                    />
                                    {`${accountKind(account)} / ${accountName(account)}`}
                                    {transaction.sharedBy && (
                                      <span
                                        class="cal-ai-badge cal-origin-user"
                                        title={`Shared with you by ${transaction.sharedBy}`}
                                      >
                                        {transaction.sharedBy.split(" ")[0]}
                                      </span>
                                    )}
                                    {" · "}
                                    {tag.name}
                                    {categoryOrigin(transaction.tagSource) && (
                                      <span
                                        class={`cal-ai-badge cal-origin-badge cal-origin-${(transaction.tagSource ?? "").toLowerCase()}`}
                                        title={
                                          categoryOrigin(transaction.tagSource)!
                                            .long
                                        }
                                      >
                                        {
                                          categoryOrigin(transaction.tagSource)!
                                            .short
                                        }
                                      </span>
                                    )}
                                    {transaction.shared?.status ===
                                      "SHARED" && (
                                      <span
                                        class="cal-ai-badge cal-origin-user"
                                        title={transaction.shared.reason}
                                      >
                                        Shared
                                      </span>
                                    )}
                                    {transaction.recurring && (
                                      <span
                                        class="cal-ai-badge cal-recurring-badge"
                                        title={recurringTitle(
                                          transaction.recurring,
                                        )}
                                      >
                                        <Icon name="refresh" />
                                        {transaction.recurring.label}
                                      </span>
                                    )}
                                    <span
                                      class={`cal-ai-badge cal-type-badge cal-type-${transaction.effect.toLowerCase()}`}
                                      title={`${typeLabel(transaction.type)} · ${effectLabel(transaction.effect)}. ${typeOrigin(transaction.typeSource)}: ${transaction.typeReason}.`}
                                    >
                                      {subtypeLabel(transaction.subtype)}
                                    </span>
                                  </span>
                                )}
                                {detail && (
                                  <span class="cal-tx-detail" title={detail}>
                                    {detail}
                                  </span>
                                )}
                              </span>

                              <span class="cal-tx-side">
                                <span
                                  class={{
                                    "cal-tx-amount": true,
                                    "cal-tx-income": transaction.amount > 0,
                                    "cal-tx-uncounted": !isCounted(transaction),
                                    "cal-tx-void":
                                      transaction.status === "CNCL" ||
                                      transaction.status === "RJCT",
                                  }}
                                >
                                  {hidden.value
                                    ? maskedAmount
                                    : formatMoney(
                                        transaction.amount,
                                        transaction.currency,
                                        true,
                                      )}
                                </span>
                                <span
                                  class={`cal-tx-effect cal-tx-effect-${transaction.effect.toLowerCase()}`}
                                >
                                  {status && !transaction.counted
                                    ? status
                                    : status
                                      ? `${effectLabel(transaction.effect)} · ${status}`
                                      : effectLabel(transaction.effect)}
                                </span>
                                {!status &&
                                  transaction.originalAmount != null &&
                                  transaction.originalCurrency &&
                                  !hidden.value && (
                                    <span class="cal-tx-original">
                                      {formatMoney(
                                        transaction.originalAmount,
                                        transaction.originalCurrency,
                                      )}
                                    </span>
                                  )}
                              </span>

                              {pickerOpen && manual && (
                                <div
                                  id={`cal-picker-${transaction.id}`}
                                  class="cal-picker"
                                  role="group"
                                  aria-label={`Details for ${title}`}
                                >
                                  <div class="cal-picker-head">
                                    <span class="cal-picker-title">
                                      Added by you
                                    </span>
                                    <button
                                      type="button"
                                      class="cal-icon-button cal-picker-close"
                                      aria-label="Close"
                                      onClick$={() =>
                                        closePicker(transaction.id)
                                      }
                                    >
                                      <Icon name="close" />
                                    </button>
                                  </div>

                                  <dl class="cal-manual-facts">
                                    <div>
                                      <dt>From</dt>
                                      <dd>
                                        {manualFrom(manual) ?? "Not specified"}
                                      </dd>
                                    </div>
                                    <div>
                                      <dt>To</dt>
                                      <dd>{manualTo(manual)}</dd>
                                    </div>
                                    <div>
                                      <dt>Counts as</dt>
                                      <dd>{countingLabel(manual.counting)}</dd>
                                    </div>
                                  </dl>

                                  <p class="cal-manual-note">
                                    {manual.matchedTransactionId != null
                                      ? "Your bank shows this payment now, so Zenith counts the bank's version and leaves this one out."
                                      : manual.fromAccountId != null ||
                                          manual.toAccountId != null
                                        ? "Counted until your bank shows the same payment. Then the bank's version takes over."
                                        : "None of your connected banks are involved, so this one keeps counting."}
                                  </p>

                                  {picker.error && (
                                    <p class="cal-picker-error" role="alert">
                                      {picker.error}
                                    </p>
                                  )}

                                  <div class="cal-manual-actions">
                                    <button
                                      type="button"
                                      class="cal-chip cal-manual-remove"
                                      disabled={picker.saving}
                                      onClick$={() => removeManual(manual.id)}
                                    >
                                      {picker.saving
                                        ? "Removing…"
                                        : "Remove transaction"}
                                    </button>
                                  </div>
                                </div>
                              )}

                              {pickerOpen && !manual && (
                                <div
                                  id={`cal-picker-${transaction.id}`}
                                  class="cal-picker"
                                  role="group"
                                  aria-label={`Edit ${title}`}
                                >
                                  <div class="cal-picker-head">
                                    <span class="cal-picker-title">
                                      Edit transaction
                                    </span>
                                    <button
                                      type="button"
                                      class="cal-icon-button cal-picker-close"
                                      aria-label="Close"
                                      onClick$={() =>
                                        closePicker(transaction.id)
                                      }
                                    >
                                      <Icon name="close" />
                                    </button>
                                  </div>

                                  <dl class="cal-classification">
                                    <div>
                                      <dt>Account</dt>
                                      <dd>
                                        {`${accountKind(account)} / ${accountName(account)}`}
                                        <small>
                                          {account?.creditName
                                            ? `Linked to the credit account ${account.creditName}`
                                            : account?.roleSource === "USER"
                                              ? "Account type set by you"
                                              : account?.roleSource === "BANK"
                                                ? "Account type from your bank"
                                                : "No account type set, so it is treated as an everyday account"}
                                        </small>
                                      </dd>
                                    </div>
                                    <div>
                                      <dt>Category</dt>
                                      <dd>
                                        {tag.name}
                                        <small>
                                          {categoryOrigin(transaction.tagSource)
                                            ?.long ?? "Not sorted yet"}
                                        </small>
                                      </dd>
                                    </div>
                                    <div>
                                      <dt>Type</dt>
                                      <dd>
                                        {`${typeLabel(transaction.type)} · ${subtypeLabel(transaction.subtype)}`}
                                        <small>
                                          {`${typeOrigin(transaction.typeSource)}: ${transaction.typeReason}.`}
                                        </small>
                                      </dd>
                                    </div>
                                    <div>
                                      <dt>Counts as</dt>
                                      <dd>
                                        {effectLabel(transaction.effect)}
                                        <small>
                                          {effectOrigin(
                                            transaction.effectSource,
                                          )}
                                        </small>
                                      </dd>
                                    </div>
                                    {transaction.shared && (
                                      <div>
                                        <dt>Shared</dt>
                                        <dd>
                                          {transaction.shared.status ===
                                          "SHARED"
                                            ? `Yes, paid by ${transaction.shared.paidBy ?? "you"}`
                                            : "Not verified as shared"}
                                          <small>
                                            {transaction.shared.status ===
                                              "SHARED" &&
                                            transaction.shared.myShare != null
                                              ? `${transaction.shared.reason}. Your share is ${formatMoney(transaction.shared.myShare, transaction.currency)}.`
                                              : `${transaction.shared.reason}.`}
                                          </small>
                                        </dd>
                                      </div>
                                    )}
                                  </dl>

                                  {transaction.shared?.status ===
                                    "NOT_VERIFIED" &&
                                    transaction.editable !== false && (
                                      <button
                                        type="button"
                                        class="cal-chip"
                                        disabled={picker.saving}
                                        onClick$={() =>
                                          proposeShare(transaction)
                                        }
                                      >
                                        Share this payment with your partner
                                        from now on
                                      </button>
                                    )}

                                  {transaction.editable === false ? (
                                    <p class="cal-manual-note">
                                      {`Shared with you by ${transaction.sharedBy ?? "your partner"}. Only they can change how it is sorted and counted.`}
                                    </p>
                                  ) : (
                                    <>
                                      <div class="cal-picker-section-head">
                                        <span>Category</span>
                                        {transaction.tagSource === "USER" && (
                                          <button
                                            type="button"
                                            class="cal-chip"
                                            disabled={picker.saving}
                                            onClick$={() =>
                                              resetCategory(transaction)
                                            }
                                          >
                                            Back to automatic
                                          </button>
                                        )}
                                      </div>

                                      {transaction.description && (
                                        <label class="cal-picker-remember">
                                          <input
                                            type="checkbox"
                                            checked={picker.remember}
                                            onChange$={(_, element) => {
                                              picker.remember = element.checked;
                                            }}
                                          />
                                          <span>
                                            {`Remember for “${title}”`}
                                            <small>
                                              Similar transactions move too, now
                                              and later, and the AI learns from
                                              it.
                                            </small>
                                          </span>
                                        </label>
                                      )}

                                      <div class="cal-picker-options">
                                        {tags.map((option) => (
                                          <button
                                            key={option.id}
                                            type="button"
                                            class="cal-picker-option"
                                            aria-pressed={
                                              option.id === transaction.tagId
                                            }
                                            disabled={picker.saving}
                                            onClick$={() =>
                                              changeTag(transaction, option)
                                            }
                                          >
                                            <span
                                              class="cal-picker-swatch"
                                              style={{
                                                "--tag-color": option.color,
                                              }}
                                              aria-hidden="true"
                                            >
                                              <Icon name={option.icon} />
                                            </span>
                                            <span class="cal-picker-name">
                                              {option.name}
                                            </span>
                                          </button>
                                        ))}
                                      </div>

                                      {transaction.description && (
                                        <label class="cal-picker-remember cal-picker-recurring">
                                          <input
                                            type="checkbox"
                                            checked={!!transaction.recurring}
                                            disabled={picker.saving}
                                            onChange$={(_, element) =>
                                              setRecurring(
                                                transaction,
                                                element.checked,
                                              )
                                            }
                                          />
                                          <span>
                                            Recurring payment
                                            <small>
                                              {transaction.recurring
                                                ? `${transaction.recurring.label} · ${
                                                    transaction.recurring.manual
                                                      ? "marked by you"
                                                      : "found from your history"
                                                  }`
                                                : "Tick it to follow these payments as recurring. With only one so far, Zenith assumes it repeats monthly."}
                                            </small>
                                          </span>
                                        </label>
                                      )}

                                      <div
                                        class="cal-picker-counting"
                                        role="group"
                                        aria-labelledby={`cal-type-${transaction.id}`}
                                      >
                                        <span
                                          id={`cal-type-${transaction.id}`}
                                          class="cal-picker-counting-label"
                                        >
                                          Type
                                          <small>
                                            {transaction.typeOverride
                                              ? `Set by you. Automatic would make it ${subtypeLabel(transaction.automaticSubtype).toLowerCase()} (${typeLabel(transaction.automaticType).toLowerCase()}): ${transaction.automaticReason}.`
                                              : `Automatic: ${transaction.automaticReason}.`}
                                          </small>
                                        </span>
                                        <div class="cal-picker-counting-options">
                                          <button
                                            type="button"
                                            class="cal-picker-counting-option"
                                            aria-pressed={
                                              !transaction.typeOverride
                                            }
                                            disabled={picker.saving}
                                            onClick$={() =>
                                              setType(transaction, null)
                                            }
                                          >
                                            {`Automatic (${typeLabel(transaction.automaticType)})`}
                                          </button>
                                          {typeChoices.map((choice) => (
                                            <button
                                              key={choice.value}
                                              type="button"
                                              class="cal-picker-counting-option"
                                              aria-pressed={
                                                transaction.typeOverride ===
                                                choice.value
                                              }
                                              disabled={picker.saving}
                                              onClick$={() =>
                                                setType(
                                                  transaction,
                                                  choice.value,
                                                )
                                              }
                                            >
                                              {choice.label}
                                            </button>
                                          ))}
                                        </div>
                                      </div>

                                      <div
                                        class="cal-picker-counting"
                                        role="group"
                                        aria-labelledby={`cal-counting-${transaction.id}`}
                                      >
                                        <span
                                          id={`cal-counting-${transaction.id}`}
                                          class="cal-picker-counting-label"
                                        >
                                          Counts as
                                          <small>
                                            {transaction.counting
                                              ? `Set by you. Automatic would count it as ${effectLabel(transaction.typeEffect).toLowerCase()}, from its type (${typeLabel(transaction.type).toLowerCase()}).`
                                              : `Automatic counts it as ${effectLabel(transaction.typeEffect).toLowerCase()}, from its type (${typeLabel(transaction.type).toLowerCase()}).`}
                                          </small>
                                        </span>
                                        <div class="cal-picker-counting-options">
                                          {manualCountingChoices.map(
                                            (choice) => (
                                              <button
                                                key={choice.label}
                                                type="button"
                                                class="cal-picker-counting-option"
                                                aria-pressed={
                                                  (transaction.counting ??
                                                    null) === choice.value
                                                }
                                                disabled={picker.saving}
                                                onClick$={() =>
                                                  setCounting(
                                                    transaction,
                                                    choice.value,
                                                  )
                                                }
                                              >
                                                {choice.value === null
                                                  ? `Automatic (${effectLabel(transaction.typeEffect)})`
                                                  : choice.label}
                                              </button>
                                            ),
                                          )}
                                        </div>
                                      </div>
                                    </>
                                  )}

                                  {picker.error && (
                                    <p class="cal-picker-error" role="alert">
                                      {picker.error}
                                    </p>
                                  )}
                                </div>
                              )}

                              {rowNotice && (
                                <p class="cal-tx-notice">
                                  <Icon name="check" />
                                  {rowNotice}
                                </p>
                              )}
                            </li>
                          );
                        })}
                      </ul>
                    </section>
                  ) : null,
                )}
              </div>
            )}
          </aside>
        </div>
      </section>

      {covered && (
        <CategoryBreakdown
          rows={breakdownRows}
          monthName={monthName}
          selectedId={filteredTag}
          onSelect$={selectCategory}
        />
      )}
    </main>
  );
});
