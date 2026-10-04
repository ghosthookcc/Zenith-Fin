import { component$, $, useSignal, useVisibleTask$ } from '@builder.io/qwik';

import { Icon } from './Icon';

interface BalanceAmount {
    currency: string;
    amount: string;
}

interface Balance {
    name?: string | null;
    balanceAmount: BalanceAmount;
    balanceType: string;
    lastChangeDateTime?: string | null;
    referenceDate?: string | null;
    lastCommittedTransaction?: string | null;
}

interface BackendAccount {
    id: number;
    enableBankingUid: string;
    bankName: string;
    iban?: string | null;
    name?: string | null;
    currency: string;
    balances: Balance[];
}

interface DisplayAccount {
    id: string;
    backendId: number;
    name: string;
    bank: string;
    iban?: string | null;
    currency: string;
    balance: number;
    color: string;
    purpose: string;
    icon: 'plane' | 'home' | 'heart' | 'wallet' | 'shield' | 'layers';
}

const colors = [
    '#d482c4',
    '#96cba0',
    '#b8a0e7',
    '#83a9eb',
    '#dfc17d',
    '#7a829b',
    '#cf8e73',
    '#68a7a3',
];

const icons: DisplayAccount['icon'][] = [
    'wallet',
    'home',
    'shield',
    'layers',
    'heart',
    'plane',
];

function arcPath(start: number, end: number) {
    const point = (angle: number) => {
        const radians = ((angle - 90) * Math.PI) / 180;

        return `${180 + 137 * Math.cos(radians)} ${
            180 + 137 * Math.sin(radians)
        }`;
    };

    return `M ${point(start)} A 137 137 0 ${
        end - start > 180 ? 1 : 0
    } 1 ${point(end)}`;
}

function money(amount: number): string {
    return new Intl.NumberFormat('sv-SE', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(amount);
}

function percent(amount: number, total: number): string {
    if (total <= 0) {
        return '0';
    }

    return new Intl.NumberFormat('en-GB', {
        maximumFractionDigits: 1,
    }).format((amount / total) * 100);
}

function getMainBalance(account: BackendAccount): number {
    if (!account.balances?.length) {
        return 0;
    }

    const balance =
        account.balances.find(
            (item) =>
                item.balanceType?.toLowerCase() === 'closingbooked',
        ) ??
        account.balances.find(
            (item) =>
                item.balanceType?.toLowerCase() === 'expected',
        ) ??
        account.balances[0];

    if (!balance) {
        return 0;
    }

    const amount = Number(balance.balanceAmount.amount);

    return Number.isFinite(amount) ? amount : 0;
}

function makeDisplayAccount(
    account: BackendAccount,
    index: number,
): DisplayAccount {
    return {
        id: account.enableBankingUid || String(account.id),
        backendId: account.id,
        name: account.name || 'Bank account',
        bank: account.bankName || 'Unknown bank',
        iban: account.iban,
        currency: account.currency,
        balance: getMainBalance(account),
        color: colors[index % colors.length],
        purpose: account.iban
            ? `IBAN ${account.iban}`
            : 'Connected bank account',
        icon: icons[index % icons.length],
    };
}

export const AccountOverview = component$(() => {
    const backendAccounts = useSignal<BackendAccount[]>([]);

    const loading = useSignal(true);
    const error = useSignal<string | null>(null);

    const hidden = useSignal(false);
    const selected = useSignal<string | null>(null);

    const loadAccounts = $(async () => {
        loading.value = true;
        error.value = null;

        const controller = new AbortController();

        const timeout = setTimeout(() => {
            controller.abort();
        }, 300_000);

        try {
            const response = await fetch('/api/accounts/expanded', {
                method: 'GET',
                headers: {
                    Accept: 'application/json',
                },
                credentials: 'same-origin',
                cache: 'no-store',
                signal: controller.signal,
            });

            const text = await response.text();

            let data: unknown = null;

            if (text) {
                try {
                    data = JSON.parse(text);
                } catch {
                    throw new Error(
                        `Account server returned invalid JSON (${response.status})`,
                    );
                }
            }

            if (!response.ok) {
                let message = `Failed to fetch accounts (${response.status})`;

                if (
                    data &&
                    typeof data === 'object' &&
                    !Array.isArray(data) &&
                    'message' in data
                ) {
                    const backendMessage = (
                        data as { message?: unknown }
                    ).message;

                    if (typeof backendMessage === 'string') {
                        message = backendMessage;
                    }
                }

                throw new Error(message);
            }

            if (!Array.isArray(data)) {
                throw new Error(
                    'Expected the account API to return an array.',
                );
            }

            backendAccounts.value = data as BackendAccount[];
        } catch (err) {
            if (controller.signal.aborted) {
                error.value = 'The account request timed out. Please try again.';
            } else if (err instanceof Error) {
                error.value = err.message;
            } else {
                error.value = 'Could not load accounts.';
            }
        } finally {
            clearTimeout(timeout);
            loading.value = false;
        }
    });

    useVisibleTask$(
        () => {
            void loadAccounts();
        },
        {
            strategy: 'document-ready',
        },
    );
    
    const accounts = backendAccounts.value.map(makeDisplayAccount);

    const totalBalance = accounts.reduce((sum, account) => sum + account.balance, 0);

    const active = accounts.find((account) => account.id === selected.value);

    let angle = 0;

    const segments = accounts.map((account) => 
    {
        const start = angle;

        const share = totalBalance > 0 ? account.balance / totalBalance : 0;

        angle += share * 360;

        return { ...account, path: arcPath(start + 1.7, angle - 1.7) };
    });

    const banks = accounts.reduce<Record<string, { count: number; total: number; }>>((result, account) => {
        const current = result[account.bank] ?? { count: 0, total: 0 };

        current.count += 1;
        current.total += account.balance;

        result[account.bank] = current;

        return result;
    }, {});

    return (
        <>
            <main id="main" class="page-shell">
                <section
                    class="page-heading"
                    aria-labelledby="page-title"
                >
                    <div>
                        <h1 id="page-title">
                            Accounts overview<span>.</span>
                        </h1>
                    </div>

                    <button
                        class="visibility-button"
                        type="button"
                        onClick$={() => {
                            hidden.value = !hidden.value;
                        }}
                        aria-pressed={hidden.value}
                    >
                        <Icon
                            name={
                                hidden.value
                                    ? 'eyeoff'
                                    : 'eye'
                            }
                        />

                        {hidden.value
                            ? 'Show balances'
                            : 'Hide balances'}
                    </button>
                </section>

                {loading.value && (
                    <section class="overview-panel">
                        <p>Loading account balances...</p>
                    </section>
                )}

                {error.value && (
                    <section class="overview-panel">
                        <p role="alert">
                            Could not load accounts: {error.value}
                        </p>

                        <button
                            type="button"
                            onClick$={loadAccounts}
                        >
                            Try again
                        </button>
                    </section>
                )}

                {!loading.value &&
                    !error.value &&
                    accounts.length === 0 && (
                        <section class="overview-panel">
                            <p>
                                No connected accounts found.
                            </p>
                        </section>
                    )}

                {!loading.value &&
                    !error.value &&
                    accounts.length > 0 && (
                        <>
                            <section
                                class="overview-panel"
                                aria-labelledby="overview-title"
                            >
                                <div class="panel-top">
                                    <h2 id="overview-title">
                                        <span class="tiny-mark" />
                                        Financial overview
                                    </h2>

                                    <span class="soft-badge">
                                        {accounts.length}{' '}
                                        {accounts.length === 1
                                            ? 'account'
                                            : 'accounts'}
                                        {' · '}
                                        {Object.keys(banks).length}{' '}
                                        {Object.keys(banks).length === 1
                                            ? 'bank'
                                            : 'banks'}
                                    </span>
                                </div>

                                <div class="overview-content">
                                    <div class="chart-area">
                                        <div class="donut-wrap">
                                            <svg
                                                viewBox="0 0 360 360"
                                                class="donut"
                                                role="group"
                                                aria-label="Select an account to explore its share of your balance."
                                            >
                                                <circle
                                                    cx="180"
                                                    cy="180"
                                                    r="137"
                                                    fill="none"
                                                    stroke="#222835"
                                                    stroke-width="31"
                                                />

                                                {segments.map(
                                                    (account) => (
                                                        <path
                                                            key={
                                                                account.id
                                                            }
                                                            d={
                                                                account.path
                                                            }
                                                            fill="none"
                                                            stroke={
                                                                account.color
                                                            }
                                                            stroke-width={
                                                                selected.value ===
                                                                account.id
                                                                    ? 37
                                                                    : 31
                                                            }
                                                            stroke-linecap="butt"
                                                            opacity={
                                                                !selected.value ||
                                                                selected.value ===
                                                                account.id
                                                                    ? 1
                                                                    : 0.19
                                                            }
                                                            role="button"
                                                            tabindex={
                                                                0
                                                            }
                                                            aria-label={`${account.name}, ${percent(
                                                                account.balance,
                                                                totalBalance,
                                                            )} percent of total`}
                                                            aria-pressed={
                                                                selected.value ===
                                                                account.id
                                                            }
                                                            onClick$={() => {
                                                                selected.value =
                                                                    selected.value ===
                                                                    account.id
                                                                        ? null
                                                                        : account.id;
                                                            }}
                                                            onKeyDown$={(
                                                                event,
                                                            ) => {
                                                                if (
                                                                    event.key ===
                                                                    'Enter' ||
                                                                    event.key ===
                                                                    ' '
                                                                ) {
                                                                    selected.value =
                                                                        selected.value ===
                                                                        account.id
                                                                            ? null
                                                                            : account.id;
                                                                }
                                                            }}
                                                        />
                                                    ),
                                                )}

                                                <circle
                                                    cx="180"
                                                    cy="180"
                                                    r="111"
                                                    fill="none"
                                                    stroke="#303647"
                                                    stroke-width="1"
                                                    stroke-dasharray="1 7"
                                                />
                                            </svg>

                                            <div
                                                class="donut-center"
                                                aria-live="polite"
                                                aria-atomic="true"
                                            >
                                                <span class="chart-label">
                                                    {active
                                                        ? active.name
                                                        : 'Total balance'}
                                                </span>

                                                <strong
                                                    class={{
                                                        'balance-value': true,
                                                        masked:
                                                        hidden.value,
                                                    }}
                                                >
                                                    {hidden.value
                                                        ? '•• •••,••'
                                                        : money(
                                                            active?.balance ??
                                                            totalBalance,
                                                        )}
                                                </strong>

                                                <span class="chart-currency">
                                                    SEK{' '}
                                                    <span>
                                                        Swedish krona
                                                    </span>
                                                </span>

                                                {active && (
                                                    <span
                                                        class="chart-share"
                                                        style={{
                                                            color: active.color,
                                                        }}
                                                    >
                                                        {percent(
                                                            active.balance,
                                                            totalBalance,
                                                        )}
                                                        % of total
                                                    </span>
                                                )}
                                            </div>
                                        </div>

                                        <button
                                            class="chart-reset"
                                            type="button"
                                            disabled={
                                                !selected.value
                                            }
                                            onClick$={() => {
                                                selected.value =
                                                    null;
                                            }}
                                        >
                                            {selected.value
                                                ? 'Show total balance'
                                                : 'Across all your accounts'}
                                        </button>
                                    </div>

                                    <div class="allocation">
                                        <div class="allocation-heading">
                                            <h3>
                                                Where your money sits
                                            </h3>
                                            <span>Share</span>
                                        </div>

                                        <div class="allocation-list">
                                            {accounts.map(
                                                (account) => (
                                                    <button
                                                        type="button"
                                                        key={
                                                            account.id
                                                        }
                                                        class={{
                                                            'allocation-row':
                                                                true,
                                                            selected:
                                                                selected.value ===
                                                                account.id,
                                                        }}
                                                        aria-pressed={
                                                            selected.value ===
                                                            account.id
                                                        }
                                                        onClick$={() => {
                                                            selected.value =
                                                                selected.value ===
                                                                account.id
                                                                    ? null
                                                                    : account.id;
                                                        }}
                                                    >
                                                        <span
                                                            class="allocation-dot"
                                                            style={{
                                                                backgroundColor:
                                                                account.color,
                                                            }}
                                                        />

                                                        <span class="allocation-name">
                                                            {
                                                                account.name
                                                            }

                                                            <small>
                                                                {
                                                                    account.bank
                                                                }
                                                            </small>
                                                        </span>

                                                        <span class="allocation-amount">
                                                            {hidden.value
                                                                ? '•• •••,••'
                                                                : money(
                                                                    account.balance,
                                                                )}

                                                            <small>
                                                                kr
                                                            </small>
                                                        </span>

                                                        <span class="allocation-percent">
                                                            {percent(
                                                                account.balance,
                                                                totalBalance,
                                                            )}
                                                            %
                                                        </span>
                                                    </button>
                                                ),
                                            )}
                                        </div>
                                    </div>
                                </div>

                                <div class="bank-breakdown">
                                    {Object.entries(banks).map(
                                        ([bank, data]) => (
                                            <div
                                                class="bank-summary"
                                                key={bank}
                                            >
                                                <span class="bank-monogram">
                                                    {bank
                                                        .charAt(0)
                                                        .toUpperCase()}
                                                </span>

                                                <span class="bank-label">
                                                    {bank}

                                                    <small>
                                                        {data.count}{' '}
                                                        {data.count ===
                                                        1
                                                            ? 'account'
                                                            : 'accounts'}
                                                    </small>
                                                </span>

                                                <span class="bank-total">
                                                    {hidden.value
                                                        ? '•• •••,••'
                                                        : money(
                                                            data.total,
                                                        )}{' '}
                                                    <small>
                                                        kr
                                                    </small>
                                                </span>

                                                <span class="bank-share">
                                                    {percent(
                                                        data.total,
                                                        totalBalance,
                                                    )}
                                                    %
                                                </span>
                                            </div>
                                        ),
                                    )}
                                </div>
                            </section>

                            <section
                                class="accounts-section"
                                aria-labelledby="accounts-title"
                            >
                                <div class="section-heading">
                                    <div>
                                        <h2 id="accounts-title">
                                            Your accounts{' '}
                                            <span>
                                                {String(
                                                    accounts.length,
                                                ).padStart(
                                                    2,
                                                    '0',
                                                )}
                                            </span>
                                        </h2>

                                        <p>
                                            Balances and share of
                                            your total.
                                        </p>
                                    </div>

                                    <span class="section-meta">
                                        Balances in SEK
                                    </span>
                                </div>

                                <div class="accounts-grid">
                                    {accounts.map(
                                        (account, index) => (
                                            <article
                                                key={
                                                    account.id
                                                }
                                                class={{
                                                    'account-card':
                                                        true,
                                                    'account-highlighted':
                                                        selected.value ===
                                                        account.id,
                                                }}
                                                style={{
                                                    '--account-color':
                                                    account.color,
                                                    '--account-share':
                                                        `${
                                                            totalBalance >
                                                            0
                                                                ? (account.balance /
                                                                    totalBalance) *
                                                                100
                                                                : 0
                                                        }%`,
                                                }}
                                            >
                                                <div class="card-top">
                                                    <span class="account-icon">
                                                        <Icon
                                                            name={
                                                                account.icon
                                                            }
                                                        />
                                                    </span>

                                                    <span class="card-bank">
                                                        {
                                                            account.bank
                                                        }
                                                    </span>

                                                    <span class="account-index">
                                                        {String(
                                                            index +
                                                            1,
                                                        ).padStart(
                                                            2,
                                                            '0',
                                                        )}
                                                    </span>
                                                </div>

                                                <h3>
                                                    {account.name}
                                                </h3>

                                                <p class="account-purpose">
                                                    {
                                                        account.purpose
                                                    }
                                                </p>

                                                <div class="card-balance">
                                                    {hidden.value
                                                        ? '•• •••,••'
                                                        : money(
                                                            account.balance,
                                                        )}{' '}
                                                    <span>
                                                        kr
                                                    </span>
                                                </div>

                                                <div
                                                    class="share-track"
                                                    aria-hidden="true"
                                                >
                                                    <span />
                                                </div>

                                                <div class="card-footer">
                                                    <span>
                                                        Share of total
                                                    </span>

                                                    <strong>
                                                        {percent(
                                                            account.balance,
                                                            totalBalance,
                                                        )}
                                                        %
                                                    </strong>
                                                </div>
                                            </article>
                                        ),
                                    )}
                                </div>
                            </section>
                        </>
                    )}
            </main>
        </>
    );
});