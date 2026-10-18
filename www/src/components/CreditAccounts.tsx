import { $, component$, useSignal, useStore, useVisibleTask$ } from '@builder.io/qwik';

import { Icon } from './Icon';

export interface CreditAccountOption {
    id: number;
    name: string;
    bank: string;
    iban?: string | null;
    color: string;
}

interface CreditAccount {
    id: number;
    name: string;
    accountId: number;
    counting: string;
}

interface CreditAccountsResponse {
    creditAccounts: CreditAccount[];
}

interface Editor {
    open: boolean;
    id: number | null;
    name: string;
    accountId: string;
    counting: string;
    saving: boolean;
    error: string | null;
}

const countingChoices = [
    {
        value: 'NEUTRAL',
        label: 'Neutral',
        hint: "Its transactions are left out of the month's money in and out.",
    },
    {
        value: 'FLOW',
        label: 'Money in & out',
        hint: 'Its transactions count like any other money in and out.',
    },
];

function countingLabel(counting: string): string {
    return countingChoices.find((choice) => choice.value === counting)?.label ?? counting;
}

function accountLabel(account: CreditAccountOption | undefined): string {
    if (!account) {
        return 'an account that is no longer connected';
    }

    const iban = account.iban?.replace(/\s+/g, '');

    return `${account.name}${iban ? ` ··${iban.slice(-4)}` : ''} · ${account.bank}`;
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

function isCreditAccountsResponse(value: unknown): value is CreditAccountsResponse {
    return (
        !!value &&
        typeof value === 'object' &&
        Array.isArray((value as CreditAccountsResponse).creditAccounts)
    );
}

export const CreditAccounts = component$<{ accounts: CreditAccountOption[] }>((props) => {
    const data = useSignal<CreditAccount[] | null>(null);
    const loading = useSignal(true);
    const error = useSignal<string | null>(null);
    const confirmingDelete = useSignal<number | null>(null);
    const deleting = useSignal<number | null>(null);

    const editor = useStore<Editor>({
        open: false,
        id: null,
        name: '',
        accountId: '',
        counting: 'NEUTRAL',
        saving: false,
        error: null,
    });

    const load = $(async () => {
        loading.value = true;
        error.value = null;

        try {
            const response = await fetch('/api/credit-accounts', {
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
                    messageFrom(payload) ?? `Failed to fetch credit accounts (${response.status})`,
                );
            }

            if (!isCreditAccountsResponse(payload)) {
                throw new Error('Unexpected response from the credit account API.');
            }

            data.value = payload.creditAccounts;
        } catch (err) {
            error.value = err instanceof Error ? err.message : 'Could not load credit accounts.';
        } finally {
            loading.value = false;
        }
    });

    const openEditor = $((creditAccount: CreditAccount | null) => {
        editor.open = true;
        editor.id = creditAccount?.id ?? null;
        editor.name = creditAccount?.name ?? '';
        editor.accountId = creditAccount ? String(creditAccount.accountId) : '';
        editor.counting = creditAccount?.counting ?? 'NEUTRAL';
        editor.error = null;
        confirmingDelete.value = null;
    });

    const save = $(async () => {
        if (editor.saving) {
            return;
        }

        editor.saving = true;
        editor.error = null;

        try {
            const response = await fetch(
                editor.id === null ? '/api/credit-accounts' : `/api/credit-accounts/${editor.id}`,
                {
                    method: editor.id === null ? 'POST' : 'PUT',
                    headers: {
                        'Accept': 'application/json',
                        'Content-Type': 'application/json',
                    },
                    credentials: 'same-origin',
                    body: JSON.stringify({
                        name: editor.name,
                        accountId: editor.accountId ? Number(editor.accountId) : null,
                        counting: editor.counting,
                    }),
                },
            );

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(
                    messageFrom(payload) ?? `Could not save the credit account (${response.status})`,
                );
            }

            editor.open = false;
            await load();
        } catch (err) {
            editor.error = err instanceof Error ? err.message : 'Could not save the credit account.';
        } finally {
            editor.saving = false;
        }
    });

    const remove = $(async (id: number) => {
        deleting.value = id;
        error.value = null;

        try {
            const response = await fetch(`/api/credit-accounts/${id}`, {
                method: 'DELETE',
                headers: {
                    Accept: 'application/json',
                },
                credentials: 'same-origin',
            });

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(
                    messageFrom(payload) ?? `Could not delete the credit account (${response.status})`,
                );
            }

            confirmingDelete.value = null;

            if (editor.id === id) {
                editor.open = false;
            }

            await load();
        } catch (err) {
            error.value = err instanceof Error ? err.message : 'Could not delete the credit account.';
        } finally {
            deleting.value = null;
        }
    });

    useVisibleTask$(
        () => {
            void load();
        },
        {
            strategy: 'document-ready',
        },
    );

    const creditAccounts = data.value ?? [];
    const accountsById: Record<number, CreditAccountOption> = Object.fromEntries(
        props.accounts.map((account) => [account.id, account]),
    );
    const linkedElsewhere = creditAccounts
        .filter((creditAccount) => creditAccount.id !== editor.id)
        .map((creditAccount) => creditAccount.accountId);
    const selectedCounting = countingChoices.find((choice) => choice.value === editor.counting);

    return (
        <section class="accounts-section cal-shell credit-section" aria-labelledby="credit-title">
            <div class="section-heading">
                <div>
                    <h2 id="credit-title">
                        Credit accounts <span>{String(creditAccounts.length).padStart(2, '0')}</span>
                    </h2>
                    <p>Link one of your accounts. Everything on it counts toward the credit account.</p>
                </div>

                {!editor.open && (
                    <button
                        type="button"
                        class="cat-primary credit-new"
                        disabled={!data.value}
                        onClick$={() => openEditor(null)}
                    >
                        New credit account
                    </button>
                )}
            </div>

            {error.value && (
                <div class="cal-alert" role="alert">
                    <Icon name="info" />
                    <div>
                        <strong>{error.value}</strong>
                        <button type="button" class="cal-chip cat-retry" onClick$={load}>
                            Try again
                        </button>
                    </div>
                </div>
            )}

            {editor.open && (
                <div class="overview-panel cat-editor">
                    <form preventdefault:submit onSubmit$={save} aria-labelledby="credit-editor-title">
                        <div class="cat-editor-head">
                            <div>
                                <h2 id="credit-editor-title">
                                    {editor.id === null ? 'New credit account' : 'Edit credit account'}
                                </h2>
                                <p>
                                    {editor.name.trim() || 'Untitled'} ·{' '}
                                    {countingLabel(editor.counting)}
                                </p>
                            </div>
                        </div>

                        <div class="cat-fields">
                            <div class="cat-field">
                                <label for="credit-name" class="cat-label">
                                    Name
                                </label>
                                <input
                                    id="credit-name"
                                    class="cat-input"
                                    type="text"
                                    maxLength={40}
                                    required
                                    placeholder="For example Credit card"
                                    value={editor.name}
                                    onInput$={(_, element) => {
                                        editor.name = element.value;
                                    }}
                                />
                            </div>

                            <div class="cat-field">
                                <label for="credit-account" class="cat-label">
                                    Linked account
                                </label>
                                <select
                                    id="credit-account"
                                    class="cat-input"
                                    required
                                    aria-describedby="credit-account-hint"
                                    onChange$={(_, element) => {
                                        editor.accountId = element.value;
                                    }}
                                >
                                    <option value="" selected={editor.accountId === ''}>
                                        Pick an account
                                    </option>
                                    {props.accounts.map((account) => (
                                        <option
                                            key={account.id}
                                            value={String(account.id)}
                                            selected={editor.accountId === String(account.id)}
                                            disabled={linkedElsewhere.includes(account.id)}
                                        >
                                            {`${accountLabel(account)}${
                                                linkedElsewhere.includes(account.id)
                                                    ? ' (already a credit account)'
                                                    : ''
                                            }`}
                                        </option>
                                    ))}
                                </select>
                                <span id="credit-account-hint" class="cat-hint">
                                    Every transaction on this account counts toward the credit account.
                                </span>
                            </div>

                            <fieldset class="cat-field cat-field-wide">
                                <legend class="cat-label">Counts as</legend>
                                <div class="cat-kind-row">
                                    {countingChoices.map((choice) => (
                                        <label
                                            key={choice.value}
                                            class={{
                                                'cat-kind-option': true,
                                                'cat-option-selected': editor.counting === choice.value,
                                            }}
                                        >
                                            <input
                                                type="radio"
                                                name="credit-counting"
                                                value={choice.value}
                                                checked={editor.counting === choice.value}
                                                onChange$={() => {
                                                    editor.counting = choice.value;
                                                }}
                                            />
                                            {choice.label}
                                        </label>
                                    ))}
                                </div>
                                {selectedCounting && <p class="cat-hint">{selectedCounting.hint}</p>}
                            </fieldset>
                        </div>

                        {editor.error && (
                            <p class="cat-error" role="alert">
                                {editor.error}
                            </p>
                        )}

                        <div class="cat-actions">
                            <button
                                type="button"
                                class="cal-chip"
                                onClick$={() => {
                                    editor.open = false;
                                }}
                            >
                                Cancel
                            </button>
                            <button type="submit" class="cat-primary" disabled={editor.saving}>
                                {editor.saving
                                    ? 'Saving…'
                                    : editor.id === null
                                      ? 'Create credit account'
                                      : 'Save changes'}
                            </button>
                        </div>
                    </form>
                </div>
            )}

            {loading.value && !data.value ? (
                <p class="cal-empty credit-empty">Loading credit accounts…</p>
            ) : creditAccounts.length === 0 ? (
                !editor.open && (
                    <div class="overview-panel">
                        <p class="cal-empty">
                            No credit accounts yet. Create one and link it to the account your credit
                            runs through, then choose whether it counts as neutral or as money in and
                            out.
                        </p>
                    </div>
                )
            ) : (
                <div class="accounts-grid">
                    {creditAccounts.map((creditAccount) => {
                        const account = accountsById[creditAccount.accountId];

                        return (
                            <article
                                key={creditAccount.id}
                                class="account-card credit-card"
                                style={{ '--account-color': account?.color ?? '#8792a5' }}
                            >
                                <div class="card-top">
                                    <span class="account-icon">
                                        <Icon name="wallet" />
                                    </span>
                                    <span class="card-bank">Credit account</span>
                                    <span class="credit-counting">
                                        {countingLabel(creditAccount.counting)}
                                    </span>
                                </div>

                                <h3>{creditAccount.name}</h3>

                                <p class="account-purpose" title={accountLabel(account)}>
                                    {`Linked to ${accountLabel(account)}`}
                                </p>

                                {confirmingDelete.value === creditAccount.id ? (
                                    <div class="credit-actions" role="group" aria-label="Confirm delete">
                                        <span class="cat-confirm">
                                            Its transactions count normally again.
                                        </span>
                                        <button
                                            type="button"
                                            class="cal-chip"
                                            onClick$={() => {
                                                confirmingDelete.value = null;
                                            }}
                                        >
                                            Keep
                                        </button>
                                        <button
                                            type="button"
                                            class="cat-danger"
                                            disabled={deleting.value === creditAccount.id}
                                            onClick$={() => remove(creditAccount.id)}
                                        >
                                            {deleting.value === creditAccount.id ? 'Deleting…' : 'Delete'}
                                        </button>
                                    </div>
                                ) : (
                                    <div class="credit-actions">
                                        <button
                                            type="button"
                                            class="cal-chip"
                                            onClick$={() => openEditor(creditAccount)}
                                        >
                                            Edit
                                        </button>
                                        <button
                                            type="button"
                                            class="cal-chip"
                                            onClick$={() => {
                                                confirmingDelete.value = creditAccount.id;
                                            }}
                                        >
                                            Delete
                                        </button>
                                    </div>
                                )}
                            </article>
                        );
                    })}
                </div>
            )}
        </section>
    );
});
