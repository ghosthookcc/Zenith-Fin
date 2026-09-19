import { component$, $, useSignal, useVisibleTask$ } from '@builder.io/qwik';
import { fetchWithTimeout } from '../../utils/network';

interface Aspsp {
    id: string;
    bank: string;
    country: string;
    psuType: string;
    checked?: boolean;
}

interface AuthUrl {
    bank: string;
    url: string;
}

interface Balance {
    name?: string | null;
    balanceAmount: {
        currency: string;
        amount: string;
    };
    balanceType: string;
    lastChangeDateTime?: string | null;
    referenceDate?: string | null;
    lastCommittedTransaction?: string | null;
}

interface AccountBalance {
    id: number;
    enableBankingUid: string;
    iban?: string | null;
    name?: string | null;
    currency: string;
    balances: Balance[];
}

export default component$(() => {
    const aspsps = useSignal<Aspsp[]>([]);
    const authUrls = useSignal<AuthUrl[]>([]);

    const accounts = useSignal<AccountBalance[]>([]);

    const error = useSignal<string | null>(null);
    const balanceError = useSignal<string | null>(null);

    const loading = useSignal(true);
    const balanceLoading = useSignal(true);

    const toggleAspsp = $((id: string, checked: boolean) => {
        aspsps.value = aspsps.value.map((a) => {
            if (a.id === id) {
                return { ...a, checked };
            }

            return a;
        });
    });

    const loadBalances = $(async () => {
        balanceLoading.value = true;
        balanceError.value = null;

        try {
            const response = await fetchWithTimeout(
                '/api/accounts/balances',
                {
                    method: 'GET',
                    headers: {
                        Accept: 'application/json',
                    },
                },
                10000,
            );

            const text = await response.text();

            let data: any;

            try {
                data = text ? JSON.parse(text) : null;
            } catch {
                throw new Error(
                    `Balance server returned invalid JSON (${response.status})`,
                );
            }

            if (!response.ok) {
                throw new Error(
                    data?.message ||
                    `Failed to fetch balances (${response.status})`,
                );
            }

            /*
             * Accept either:
             *
             * [
             *   { ...account }
             * ]
             *
             * or:
             *
             * {
             *   accounts: [...]
             * }
             */
            const rawAccounts = Array.isArray(data)
                ? data
                : Array.isArray(data?.accounts)
                    ? data.accounts
                    : [];

            accounts.value = rawAccounts;
        } catch (errno) {
            console.error('Balance fetch error:', errno);

            balanceError.value =
                errno instanceof Error
                    ? errno.message
                    : 'Unknown balance error';
        } finally {
            balanceLoading.value = false;
        }
    });

    const handleSubmit = $(async () => {
        error.value = '';
        loading.value = true;

        const payload = aspsps.value
            .filter((a) => a.checked)
            .map(({ bank, country, psuType }) => ({
                bank,
                country,
                psuType,
            }));

        console.log('PAYLOAD:', payload);

        if (payload.length === 0) {
            error.value = 'Select at least one bank.';
            loading.value = false;
            return;
        }

        try {
            const response = await fetchWithTimeout(
                '/api/aspsps/inactive',
                {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                    },
                    body: JSON.stringify({
                        aspsps: payload,
                    }),
                },
                5000,
            );

            const data = await response.json();

            if (!response.ok || !data.success) {
                throw new Error(
                    data.message || 'ASPSP authentication failed',
                );
            }

            authUrls.value = data.urls || [];
        } catch (errno: any) {
            console.error('Authentication error:', errno);
            error.value =
                errno instanceof Error
                    ? errno.message
                    : 'Authentication failed';
        } finally {
            loading.value = false;
        }
    });

    useVisibleTask$(async () => {
        /*
         * Load inactive banks.
         */
        try {
            const response = await fetch('/api/aspsps/inactive', {
                method: 'GET',
                headers: {
                    Accept: 'application/json',
                },
            });

            const data = await response.json();

            if (!response.ok || !data.success) {
                throw new Error(
                    data.message || 'Could not load banks',
                );
            }

            const raw: Record<
                string,
                {
                    country: string;
                    psuType: string;
                }
            >[] = data.aspsps?.aspsps ?? [];

            aspsps.value = raw.flatMap((item) =>
                Object.entries(item).map(([name, details]) => ({
                    id: name,
                    bank: name,
                    country: details.country,
                    psuType: details.psuType,
                })),
            );
        } catch (err) {
            console.error('ASPSP fetch error:', err);

            error.value =
                err instanceof Error
                    ? err.message
                    : 'Unknown ASPSP error';
        } finally {
            loading.value = false;
        }

        /*
         * Load the currently connected accounts/balances.
         *
         * This is deliberately independent from the ASPSP request:
         * if balances fail, the bank list still works.
         */
        await loadBalances();
    });

    return (
        <div>
            <form
                preventdefault:submit
                onSubmit$={handleSubmit}
            >
                {loading.value && <p>Loading banks...</p>}

                {!loading.value && error.value && (
                    <p>Error: {error.value}</p>
                )}

                {!loading.value && !error.value && (
                    <div>
                        <h2>Available banks</h2>

                        <ul>
                            {aspsps.value.map((a) => (
                                <li key={a.id}>
                                    <strong>{a.bank}</strong> — {a.psuType}

                                    <input
                                        type="checkbox"
                                        checked={a.checked ?? false}
                                        onChange$={(e) =>
                                            toggleAspsp(
                                                a.id,
                                                (e.target as HTMLInputElement).checked,
                                            )
                                        }
                                    />
                                </li>
                            ))}
                        </ul>
                    </div>
                )}

                {aspsps.value.length > 0 && (
                    <button type="submit">
                        Authenticate
                    </button>
                )}
            </form>

            {authUrls.value.length > 0 && (
                <div style={{ marginTop: '1rem' }}>
                    <h3>Authenticate with banks:</h3>

                    {authUrls.value.map((auth) => (
                        <a
                            key={auth.bank}
                            href={auth.url}
                            target="_blank"
                            rel="noopener noreferrer"
                        >
                            <button
                                type="button"
                                style={{
                                    display: 'block',
                                    margin: '0.5rem 0',
                                }}
                            >
                                Authenticate with {auth.bank}
                            </button>
                        </a>
                    ))}
                </div>
            )}

            <hr style={{ margin: '2rem 0' }} />

            <section>
                <div
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                    }}
                >
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
                </div>

                {balanceLoading.value && (
                    <p>Loading account balances...</p>
                )}

                {!balanceLoading.value && balanceError.value && (
                    <div>
                        <p>
                            Could not load balances: {balanceError.value}
                        </p>
                    </div>
                )}

                {!balanceLoading.value &&
                    !balanceError.value &&
                    accounts.value.length === 0 && (
                        <p>
                            No connected accounts found.
                            Authenticate a bank first.
                        </p>
                    )}

                {!balanceLoading.value &&
                    !balanceError.value &&
                    accounts.value.length > 0 && (
                        <div>
                            {accounts.value.map((account) => (
                                <article
                                    key={account.enableBankingUid}
                                    style={{
                                        border: '1px solid #444',
                                        borderRadius: '8px',
                                        padding: '1rem',
                                        marginBottom: '1rem',
                                    }}
                                >
                                    <h3>
                                        {account.name || 'Bank account'}
                                    </h3>

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

                                    {account.balances.length === 0 && (
                                        <p>No balances returned.</p>
                                    )}

                                    {account.balances.map((balance) => (
                                        <div
                                            key={`${balance.balanceType}-${balance.balanceAmount.currency}`}
                                            style={{
                                                marginTop: '0.75rem',
                                            }}
                                        >
                                            <strong>
                                                {balance.name ||
                                                    balance.balanceType}
                                                :
                                            </strong>{' '}
                                            {balance.balanceAmount.amount}{' '}
                                            {balance.balanceAmount.currency}
                                        </div>
                                    ))}
                                </article>
                            ))}
                        </div>
                    )}
            </section>
        </div>
    );
});