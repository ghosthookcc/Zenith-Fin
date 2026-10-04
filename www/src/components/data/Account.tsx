import {
    component$,
    $,
    useSignal,
    useVisibleTask$,
} from '@builder.io/qwik';

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

interface AccountBalance {
    id: number;
    enableBankingUid: string;
    bankName: string;
    iban?: string | null;
    name?: string | null;
    currency: string;
    balances: Balance[];
}

/*
 * Keep ordinary helper functions OUTSIDE component$.
 */
function formatMoney(amount: string, currency: string): string {
    const numericAmount = Number(amount);

    if (!Number.isFinite(numericAmount)) {
        return `${amount} ${currency}`;
    }

    try {
        return new Intl.NumberFormat('sv-SE', {
            style: 'currency',
            currency,
        }).format(numericAmount);
    } catch {
        return `${amount} ${currency}`;
    }
}

function getMainBalance(account: AccountBalance): Balance | undefined {
    if (!account.balances?.length) {
        return undefined;
    }

    return (
        account.balances.find(
            (balance) =>
                balance.balanceType?.toLowerCase() === 'closingbooked',
        ) ??
        account.balances.find(
            (balance) =>
                balance.balanceType?.toLowerCase() === 'expected',
        ) ??
        account.balances[0]
    );
}

function isAccountBalanceArray(value: unknown): value is AccountBalance[] {
    return Array.isArray(value);
}

export const AccountBalances = component$(() => {
    const accounts = useSignal<AccountBalance[]>([]);
    const balanceLoading = useSignal(true);
    const balanceError = useSignal<string | null>(null);

    const loadBalances = $(async () => {
        balanceLoading.value = true;
        balanceError.value = null;

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
                        `Balance server returned invalid JSON (${response.status})`,
                    );
                }
            }

            if (!response.ok) {
                let message = `Failed to fetch balances (${response.status})`;

                if (data &&
                    typeof data === 'object' &&
                    !Array.isArray(data) &&
                    'message' in data
                ) {
                    const maybeMessage = (data as { message?: unknown }).message;

                    if (typeof maybeMessage === 'string') {
                        message = maybeMessage;
                    }
                }

                throw new Error(message);
            }

            if (!isAccountBalanceArray(data)) {
                throw new Error('Expected the balance API to return an array.',);
            }
            accounts.value = data;
        } catch (error) {
            if (controller.signal.aborted) {
                balanceError.value = 'The balance request timed out. Please try again.';
            } else if (error instanceof Error) {
                balanceError.value = error.message;
            } else {
                balanceError.value = 'Unknown error while fetching balances.';
            }
        } finally {
            clearTimeout(timeout);
            balanceLoading.value = false;
        }
    });

    useVisibleTask$(
        () => {
            void loadBalances();
        },
        {
            strategy: 'document-ready',
        },
    );

    return (
        <section aria-busy={balanceLoading.value}>
            <h2>Accounts</h2>

            <button
                type="button"
                onClick$={loadBalances}
                disabled={balanceLoading.value}
            >
                {balanceLoading.value
                    ? 'Loading...'
                    : 'Refresh balances'}
            </button>

            {balanceLoading.value && (
                <p>Loading account balances...</p>
            )}

            {balanceError.value && (
                <p role="alert">
                    Could not load balances: {balanceError.value}
                </p>
            )}

            {!balanceLoading.value &&
                !balanceError.value &&
                accounts.value.length === 0 && (
                    <p>No connected accounts found.</p>
                )}

            {!balanceLoading.value &&
                !balanceError.value &&
                accounts.value.map((account) => {
                    const mainBalance = getMainBalance(account);

                    return (
                        <article
                            key={account.enableBankingUid}
                            style={{
                                border: '1px solid #444',
                                borderRadius: '8px',
                                padding: '1rem',
                                marginTop: '1rem',
                            }}
                        >
                            <h3>
                                {account.name || 'Bank account'}
                            </h3>

                            <p>
                                <strong>Bank:</strong>{' '}
                                {account.bankName || 'Unknown bank'}
                            </p>

                            {account.iban && (
                                <p>
                                    <strong>IBAN:</strong>{' '}
                                    {account.iban}
                                </p>
                            )}

                            <p>
                                <strong>Currency:</strong>{' '}
                                {account.currency}
                            </p>

                            {mainBalance && (
                                <p>
                                    <strong>Balance:</strong>{' '}
                                    {formatMoney(
                                        mainBalance.balanceAmount.amount,
                                        mainBalance.balanceAmount.currency,
                                    )}
                                </p>
                            )}

                            {!account.balances?.length ? (
                                <p>No balances returned.</p>
                            ) : (
                                <div>
                                    {account.balances.map(
                                        (balance, index) => (
                                            <div
                                                key={`${balance.balanceType}-${index}`}
                                            >
                                                <p>
                                                    <strong>
                                                        {balance.name ||
                                                            balance.balanceType}
                                                        :
                                                    </strong>{' '}

                                                    {formatMoney(
                                                        balance.balanceAmount
                                                            .amount,
                                                        balance.balanceAmount
                                                            .currency,
                                                    )}
                                                </p>
                                            </div>
                                        ),
                                    )}
                                </div>
                            )}
                        </article>
                    );
                })}
        </section>
    );
});