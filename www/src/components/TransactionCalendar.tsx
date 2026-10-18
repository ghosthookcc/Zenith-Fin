import { $, component$, sync$, useSignal, useVisibleTask$ } from '@builder.io/qwik';

import { Icon } from './Icon';
import { accountColors } from '../utils/accounts';

interface CalendarAccount {
    id: number;
    name?: string | null;
    bankName: string;
    iban?: string | null;
    currency: string;
    isActive: boolean;
    transactionsSyncedAt?: string | null;
    isStale: boolean;
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
}

interface TransactionsResponse {
    from: string;
    to: string;
    accounts: CalendarAccount[];
    transactions: CalendarTransaction[];
}

interface SyncResult {
    accountId: number;
    status: string;
    error?: string | null;
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

const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

const uncountedStatuses = ['CNCL', 'RJCT', 'SCHD'];

const inactiveAccountColor = '#5d6678';
const maskedAmount = '•• •••,••';

function isoDate(year: number, monthIndex: number, day: number): string {
    return new Date(Date.UTC(year, monthIndex, day)).toISOString().slice(0, 10);
}

function addDays(date: string, days: number): string {
    const [year, month, day] = date.split('-').map(Number);

    return isoDate(year, month - 1, day + days);
}

function localToday(): string {
    const now = new Date();

    return isoDate(now.getFullYear(), now.getMonth(), now.getDate());
}

function buildMonthGrid(year: number, monthIndex: number): DayCell[] {
    const leadingDays = (new Date(Date.UTC(year, monthIndex, 1)).getUTCDay() + 6) % 7;
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
    return new Intl.DateTimeFormat('en-GB', { ...options, timeZone: 'UTC' }).format(
        new Date(`${date}T00:00:00Z`),
    );
}

function formatMoney(amount: number, currency: string, signed = false): string {
    try {
        return new Intl.NumberFormat('sv-SE', {
            style: 'currency',
            currency,
            signDisplay: signed ? 'exceptZero' : 'auto',
        }).format(amount);
    } catch {
        return `${amount.toFixed(2)} ${currency}`;
    }
}

function formatCompact(minor: number): string {
    return new Intl.NumberFormat('sv-SE', {
        maximumFractionDigits: 0,
        signDisplay: 'exceptZero',
    }).format(minor / 100);
}

function accountName(account: CalendarAccount | undefined): string {
    if (!account) {
        return 'Unknown account';
    }

    const name = account.name?.trim() || 'Bank account';
    const iban = account.iban?.replace(/\s+/g, '');

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
        (transaction.amount < 0 ? 'Payment' : 'Deposit')
    );
}

function transactionDetail(transaction: CalendarTransaction, title: string): string | null {
    const lines = transaction.remittanceInformation.filter(
        (line) => /\p{L}/u.test(line) && !line.includes(title) && !title.includes(line),
    );

    return lines.length > 0 ? lines.join(' · ') : null;
}

function statusLabel(status: string): string | null {
    switch (status) {
        case 'PDNG':
        case 'HOLD':
            return 'Pending';
        case 'SCHD':
            return 'Scheduled';
        case 'CNCL':
            return 'Cancelled';
        case 'RJCT':
            return 'Rejected';
        default:
            return null;
    }
}

function mostCommonCurrency(transactions: CalendarTransaction[], fallback: string): string {
    const counts: Record<string, number> = {};

    for (const transaction of transactions) {
        counts[transaction.currency] = (counts[transaction.currency] ?? 0) + 1;
    }

    return Object.entries(counts).sort((a, b) => b[1] - a[1])[0]?.[0] ?? fallback;
}

function summarizeDays(
    transactions: CalendarTransaction[],
    currency: string,
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

        if (!uncountedStatuses.includes(transaction.status)) {
            const minor = Math.round(transaction.amount * 100);

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

    return Math.min(4, Math.max(1, Math.ceil(Math.sqrt(Math.min(1, expenses / cap)) * 4)));
}

function dayLabel(
    cell: DayCell,
    summary: DaySummary | undefined,
    currency: string,
    hidden: boolean,
): string {
    const date = formatDate(cell.date, { weekday: 'long', day: 'numeric', month: 'long' });
    const count = summary?.transactions.length ?? 0;

    if (count === 0) {
        return `${date}, no transactions`;
    }

    const parts = [`${count} ${count === 1 ? 'transaction' : 'transactions'}`];

    if (!hidden && summary) {
        if (summary.expenses > 0) {
            parts.push(`spent ${formatMoney(summary.expenses / 100, currency)}`);
        }

        if (summary.income > 0) {
            parts.push(`received ${formatMoney(summary.income / 100, currency)}`);
        }
    }

    return `${date}, ${parts.join(', ')}`;
}

function syncLabel(accounts: CalendarAccount[], now: number): string {
    const active = accounts.filter((account) => account.isActive);

    if (active.length === 0) {
        return 'No active bank connections';
    }

    const syncTimes = active.map((account) =>
        account.transactionsSyncedAt ? Date.parse(account.transactionsSyncedAt) : NaN,
    );

    if (syncTimes.some((time) => Number.isNaN(time))) {
        return 'Not synced yet';
    }

    const minutes = Math.floor((now - Math.min(...syncTimes)) / 60_000);

    if (minutes < 1) {
        return 'Synced just now';
    }

    if (minutes < 60) {
        return `Synced ${minutes} min ago`;
    }

    if (minutes < 24 * 60) {
        return `Synced ${Math.floor(minutes / 60)} h ago`;
    }

    return `Synced ${formatDate(new Date(Math.min(...syncTimes)).toISOString().slice(0, 10), {
        day: 'numeric',
        month: 'short',
    })}`;
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
    if (typeof payload === 'string') {
        return payload;
    }

    if (payload && typeof payload === 'object' && 'message' in payload) {
        const message = (payload as { message?: unknown }).message;

        if (typeof message === 'string') {
            return message;
        }
    }

    return null;
}

function isTransactionsResponse(value: unknown): value is TransactionsResponse {
    return (
        !!value &&
        typeof value === 'object' &&
        typeof (value as TransactionsResponse).from === 'string' &&
        typeof (value as TransactionsResponse).to === 'string' &&
        Array.isArray((value as TransactionsResponse).accounts) &&
        Array.isArray((value as TransactionsResponse).transactions)
    );
}

export const TransactionCalendar = component$(() => {
    const today = localToday();
    const [todayYear, todayMonth] = today.split('-').map(Number);

    const viewYear = useSignal(todayYear);
    const viewMonth = useSignal(todayMonth - 1);
    const selectedDate = useSignal<string | null>(null);
    const accountFilter = useSignal('all');
    const hidden = useSignal(false);

    const data = useSignal<TransactionsResponse | null>(null);
    const loading = useSignal(true);
    const error = useSignal<string | null>(null);
    const syncing = useSignal(false);
    const syncProblems = useSignal<SyncProblem[]>([]);
    const loadedAt = useSignal(Date.now());
    const latestRequest = useSignal(0);

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
                method: 'GET',
                headers: {
                    Accept: 'application/json',
                },
                credentials: 'same-origin',
                cache: 'no-store',
            });

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(
                    messageFrom(payload) ?? `Failed to fetch transactions (${response.status})`,
                );
            }

            if (!isTransactionsResponse(payload)) {
                throw new Error('Unexpected response from the transaction API.');
            }

            if (request !== latestRequest.value) {
                return null;
            }

            data.value = payload;
            loadedAt.value = Date.now();

            return payload;
        } catch (err) {
            if (request === latestRequest.value) {
                error.value = err instanceof Error ? err.message : 'Could not load transactions.';
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
                method: 'POST',
                headers: {
                    Accept: 'application/json',
                },
                credentials: 'same-origin',
                cache: 'no-store',
            });

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(messageFrom(payload) ?? `Sync failed (${response.status})`);
            }

            const results = (payload as { accounts?: SyncResult[] } | null)?.accounts ?? [];

            syncProblems.value = results
                .filter((result) => result.status === 'failed')
                .map((result) => ({
                    accountId: result.accountId,
                    message: result.error || 'Sync failed',
                }));
        } catch (err) {
            syncProblems.value = [
                {
                    accountId: null,
                    message: err instanceof Error ? err.message : 'Sync failed',
                },
            ];
        } finally {
            syncing.value = false;
        }

        await load();
    });

    const showMonth = $(async (year: number, monthIndex: number, select: string | null) => {
        viewYear.value = year;
        viewMonth.value = monthIndex;
        selectedDate.value = select;

        await load();
    });

    const changeMonth = $(async (offset: number) => {
        const target = new Date(Date.UTC(viewYear.value, viewMonth.value + offset, 1));

        await showMonth(target.getUTCFullYear(), target.getUTCMonth(), null);
    });

    const selectDay = $(async (cell: DayCell) => {
        if (!cell.inMonth) {
            const [year, month] = cell.date.split('-').map(Number);

            await showMonth(year, month - 1, cell.date);
            return;
        }

        selectedDate.value = selectedDate.value === cell.date ? null : cell.date;
    });

    useVisibleTask$(
        async () => {
            const initial = await load();

            if (initial?.accounts.some((account) => account.isActive && account.isStale)) {
                await sync(false);
            }
        },
        {
            strategy: 'document-ready',
        },
    );

    const cells = buildMonthGrid(viewYear.value, viewMonth.value);
    const monthPrefix = `${viewYear.value}-${String(viewMonth.value + 1).padStart(2, '0')}`;
    const monthName = formatDate(`${monthPrefix}-01`, { month: 'long' });
    const monthTitle = formatDate(`${monthPrefix}-01`, { month: 'long', year: 'numeric' });
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

    const filteredAccount = accountFilter.value === 'all' ? null : Number(accountFilter.value);
    const visible = (covered ? data.value!.transactions : []).filter(
        (transaction) => filteredAccount === null || transaction.accountId === filteredAccount,
    );
    const monthTransactions = visible.filter((transaction) =>
        transaction.date.startsWith(monthPrefix),
    );
    const currency = mostCommonCurrency(
        monthTransactions,
        (filteredAccount !== null ? accountsById[filteredAccount]?.currency : undefined) ??
            accounts[0]?.currency ??
            'SEK',
    );
    const days = summarizeDays(visible, currency);

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
        (sum, day) => sum + (day.summary ? day.summary.income - day.summary.expenses : 0),
        0,
    );

    const neverSynced = accounts.every((account) => !account.transactionsSyncedAt);

    return (
        <main id="main" class="page-shell cal-shell">
            <section class="page-heading" aria-labelledby="page-title">
                <div>
                    <h1 id="page-title">
                        Calendar<span>.</span>
                    </h1>
                    <p class="page-subtitle">Every transaction across your accounts, day by day.</p>
                </div>

                <div class="cal-actions">
                    <span class="cal-sync-label" aria-live="polite">
                        {syncing.value
                            ? 'Syncing with your banks…'
                            : data.value
                              ? syncLabel(accounts, loadedAt.value)
                              : ''}
                    </span>

                    <button
                        type="button"
                        class="visibility-button"
                        disabled={syncing.value || !data.value}
                        onClick$={() => sync(true)}
                    >
                        <Icon name="refresh" class={syncing.value ? 'cal-spin' : ''} />
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
                        <Icon name={hidden.value ? 'eyeoff' : 'eye'} />
                        {hidden.value ? 'Show amounts' : 'Hide amounts'}
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
                                        ? 'All accounts'
                                        : `${accountName(accountsById[problem.accountId])} (${
                                              accountsById[problem.accountId]?.bankName ??
                                              'unknown bank'
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
                        <option value="all" selected={accountFilter.value === 'all'}>
                            All accounts
                        </option>
                        {accounts.map((account) => (
                            <option
                                key={account.id}
                                value={String(account.id)}
                                selected={accountFilter.value === String(account.id)}
                            >
                                {`${accountName(account)} · ${account.bankName}${
                                    account.isActive ? '' : ' (disconnected)'
                                }`}
                            </option>
                        ))}
                    </select>
                    <Icon name="chevronDown" class="cal-select-icon" />
                </label>
            </div>

            <section
                class={{ 'overview-panel': true, 'cal-panel': true, 'cal-busy': loading.value }}
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
                        <strong class="cal-stat-value">{money(income - expenses, true)}</strong>
                    </div>

                    <span class="soft-badge cal-count">
                        {monthTransactions.length}{' '}
                        {monthTransactions.length === 1 ? 'transaction' : 'transactions'}
                    </span>
                </div>

                {otherCurrencies > 0 && (
                    <p class="cal-note">
                        Totals are in {currency}. {otherCurrencies}{' '}
                        {otherCurrencies === 1 ? 'transaction' : 'transactions'} in other
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
                                    if (event.key.startsWith('Arrow')) {
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
                                            'cal-day': true,
                                            'cal-day-outside': !cell.inMonth,
                                            'cal-day-today': cell.date === today,
                                            'cal-day-selected': cell.date === selectedDate.value,
                                        }}
                                        tabIndex={cell.date === focusDate ? 0 : -1}
                                        aria-pressed={cell.date === selectedDate.value}
                                        aria-label={dayLabel(cell, summary, currency, hidden.value)}
                                        onClick$={() => selectDay(cell)}
                                    >
                                        <span class="cal-day-number">{cell.day}</span>

                                        {summary && (summary.expenses > 0 || summary.income > 0) && (
                                            <span class="cal-day-amounts" aria-hidden="true">
                                                {summary.expenses > 0 && (
                                                    <span class="cal-day-out">
                                                        {hidden.value
                                                            ? '•••'
                                                            : formatCompact(-summary.expenses)}
                                                    </span>
                                                )}
                                                {summary.income > 0 && (
                                                    <span class="cal-day-in">
                                                        <span class="cal-dot" />
                                                        <span class="cal-day-in-value">
                                                            {hidden.value
                                                                ? '•••'
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
                                              weekday: 'long',
                                              day: 'numeric',
                                              month: 'long',
                                          })
                                        : `All of ${monthName}`}
                                </h3>
                                <p>
                                    {detailCount} {detailCount === 1 ? 'transaction' : 'transactions'}
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
                                Fetching a year of transactions from your banks. The first sync can
                                take a minute.
                            </p>
                        ) : detailCount === 0 ? (
                            <p class="cal-empty">
                                {selectedDate.value
                                    ? 'No transactions on this day.'
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
                                                            weekday: 'short',
                                                            day: 'numeric',
                                                            month: 'short',
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

                                                    return (
                                                        <li key={transaction.id} class="cal-tx">
                                                            <span
                                                                class="cal-tx-mark"
                                                                style={{
                                                                    '--tx-color':
                                                                        colors[transaction.accountId] ??
                                                                        inactiveAccountColor,
                                                                }}
                                                                aria-hidden="true"
                                                            >
                                                                {title.charAt(0).toUpperCase()}
                                                            </span>

                                                            <span class="cal-tx-main">
                                                                <span class="cal-tx-title" title={title}>
                                                                    {title}
                                                                </span>
                                                                <span class="cal-tx-meta">
                                                                    {accountName(account)}
                                                                    {transaction.bankTransactionCode &&
                                                                        transaction.bankTransactionCode !==
                                                                            title &&
                                                                        ` · ${transaction.bankTransactionCode}`}
                                                                </span>
                                                                {detail && (
                                                                    <span class="cal-tx-detail" title={detail}>
                                                                        {detail}
                                                                    </span>
                                                                )}
                                                            </span>

                                                            <span class="cal-tx-side">
                                                                <span
                                                                    class={{
                                                                        'cal-tx-amount': true,
                                                                        'cal-tx-income': transaction.amount > 0,
                                                                        'cal-tx-uncounted':
                                                                            uncountedStatuses.includes(
                                                                                transaction.status,
                                                                            ),
                                                                        'cal-tx-void':
                                                                            transaction.status === 'CNCL' ||
                                                                            transaction.status === 'RJCT',
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
                                                                {status ? (
                                                                    <span class="cal-tx-status">{status}</span>
                                                                ) : (
                                                                    transaction.originalAmount != null &&
                                                                    transaction.originalCurrency &&
                                                                    !hidden.value && (
                                                                        <span class="cal-tx-original">
                                                                            {formatMoney(
                                                                                transaction.originalAmount,
                                                                                transaction.originalCurrency,
                                                                            )}
                                                                        </span>
                                                                    )
                                                                )}
                                                            </span>
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
        </main>
    );
});
