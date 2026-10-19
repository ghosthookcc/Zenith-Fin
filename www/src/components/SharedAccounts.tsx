import { $, component$, useSignal, useStore, type QRL } from '@builder.io/qwik';

import { Icon } from './Icon';

export interface AccountShare {
    id: number;
    accountId: number | null;
    virtualAccountId: number | null;
    firstName: string;
    lastName: string;
    hasPersonalNumber: boolean;
}

export interface ShareTarget {
    key: string;
    kind: 'account' | 'virtual';
    id: number;
    name: string;
    bank: string;
    iban?: string | null;
    accountNumber?: string | null;
    color: string;
}

interface Editor {
    open: boolean;
    id: number | null;
    target: string;
    firstName: string;
    lastName: string;
    personalNumber: string;
    hasSavedNumber: boolean;
    removeSavedNumber: boolean;
    saving: boolean;
    error: string | null;
}

interface Revealed {
    id: number | null;
    value: string | null;
    secondsLeft: number;
    token: number;
    loadingId: number | null;
    errorId: number | null;
    error: string | null;
}

interface SharedAccountsProps {
    targets: ShareTarget[];
    shares: AccountShare[] | null;
    error: string | null;
    onChanged$: QRL<() => Promise<void>>;
}

const revealSeconds = 10;
const maskedPersonalNumber = '••••••••-••••';

export function personName(share: AccountShare): string {
    return `${share.firstName} ${share.lastName}`;
}

export function sharedWithLabel(shares: AccountShare[]): string {
    if (shares.length === 1) {
        return `Shared with ${personName(shares[0])}`;
    }

    if (shares.length === 2) {
        return `Shared with ${personName(shares[0])} and ${personName(shares[1])}`;
    }

    return `Shared with ${personName(shares[0])} and ${shares.length - 1} others`;
}

export function accountShareKey(accountId: number): string {
    return `account:${accountId}`;
}

export function virtualShareKey(virtualAccountId: number): string {
    return `virtual:${virtualAccountId}`;
}

function shareKey(share: AccountShare): string {
    return share.virtualAccountId !== null
        ? virtualShareKey(share.virtualAccountId)
        : accountShareKey(share.accountId ?? 0);
}

export function sharesByTarget(shares: AccountShare[]): Map<string, AccountShare[]> {
    const grouped = new Map<string, AccountShare[]>();

    for (const share of shares) {
        const key = shareKey(share);

        grouped.set(key, [...(grouped.get(key) ?? []), share]);
    }

    return grouped;
}

function targetLabel(target: ShareTarget): string {
    if (target.kind === 'virtual') {
        const digits = target.accountNumber?.replace(/\D/g, '') ?? '';

        return `${target.name}${digits ? ` ··${digits.slice(-4)}` : ''} · ${target.bank}`;
    }

    const iban = target.iban?.replace(/\s+/g, '');

    return `${target.name}${iban ? ` ··${iban.slice(-4)}` : ''} · ${target.bank}`;
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

export const SharedAccounts = component$<SharedAccountsProps>((props) => {
    const confirmingRemove = useSignal<number | null>(null);
    const removing = useSignal<number | null>(null);
    const actionError = useSignal<string | null>(null);

    const editor = useStore<Editor>({
        open: false,
        id: null,
        target: '',
        firstName: '',
        lastName: '',
        personalNumber: '',
        hasSavedNumber: false,
        removeSavedNumber: false,
        saving: false,
        error: null,
    });

    const revealed = useStore<Revealed>({
        id: null,
        value: null,
        secondsLeft: 0,
        token: 0,
        loadingId: null,
        errorId: null,
        error: null,
    });

    const hideNumber = $(() => {
        revealed.token += 1;
        revealed.id = null;
        revealed.value = null;
        revealed.secondsLeft = 0;
    });

    const revealNumber = $(async (share: AccountShare) => {
        if (revealed.id === share.id) {
            await hideNumber();
            return;
        }

        revealed.loadingId = share.id;
        revealed.errorId = null;
        revealed.error = null;

        try {
            const response = await fetch(`/api/account-shares/${share.id}/personal-number`, {
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
                    messageFrom(payload) ?? `Could not show the personal number (${response.status})`,
                );
            }

            const value = (payload as { personalNumber?: unknown } | null)?.personalNumber;

            if (typeof value !== 'string') {
                throw new Error('Unexpected response from the server.');
            }

            const token = revealed.token + 1;

            revealed.token = token;
            revealed.id = share.id;
            revealed.value = value;
            revealed.secondsLeft = revealSeconds;

            const timer = window.setInterval(() => {
                if (revealed.token !== token) {
                    window.clearInterval(timer);
                    return;
                }

                revealed.secondsLeft -= 1;

                if (revealed.secondsLeft <= 0) {
                    window.clearInterval(timer);
                    revealed.id = null;
                    revealed.value = null;
                }
            }, 1000);
        } catch (err) {
            revealed.errorId = share.id;
            revealed.error = err instanceof Error ? err.message : 'Could not show the personal number.';
        } finally {
            revealed.loadingId = null;
        }
    });

    const openEditor = $((share: AccountShare | null, targetKey: string | null) => {
        editor.open = true;
        editor.id = share?.id ?? null;
        editor.target = share ? shareKey(share) : (targetKey ?? '');
        editor.firstName = share?.firstName ?? '';
        editor.lastName = share?.lastName ?? '';
        editor.personalNumber = '';
        editor.hasSavedNumber = share?.hasPersonalNumber ?? false;
        editor.removeSavedNumber = false;
        editor.error = null;
        confirmingRemove.value = null;
    });

    const closeEditor = $(() => {
        editor.open = false;
        editor.personalNumber = '';
        editor.error = null;
    });

    const save = $(async () => {
        if (editor.saving) {
            return;
        }

        editor.saving = true;
        editor.error = null;

        const target = props.targets.find((candidate) => candidate.key === editor.target);

        try {
            const response = await fetch(
                editor.id === null ? '/api/account-shares' : `/api/account-shares/${editor.id}`,
                {
                    method: editor.id === null ? 'POST' : 'PUT',
                    headers: {
                        'Accept': 'application/json',
                        'Content-Type': 'application/json',
                    },
                    credentials: 'same-origin',
                    body: JSON.stringify({
                        accountId: target?.kind === 'account' ? target.id : null,
                        virtualAccountId: target?.kind === 'virtual' ? target.id : null,
                        firstName: editor.firstName,
                        lastName: editor.lastName,
                        personalNumber: editor.removeSavedNumber ? null : editor.personalNumber.trim() || null,
                        removePersonalNumber: editor.removeSavedNumber,
                    }),
                },
            );

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(messageFrom(payload) ?? `Could not save the person (${response.status})`);
            }

            if (editor.id !== null && revealed.id === editor.id) {
                await hideNumber();
            }

            await closeEditor();
            await props.onChanged$();
        } catch (err) {
            editor.error = err instanceof Error ? err.message : 'Could not save the person.';
        } finally {
            editor.saving = false;
        }
    });

    const remove = $(async (share: AccountShare) => {
        removing.value = share.id;
        actionError.value = null;

        try {
            const response = await fetch(`/api/account-shares/${share.id}`, {
                method: 'DELETE',
                headers: {
                    Accept: 'application/json',
                },
                credentials: 'same-origin',
            });

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(
                    messageFrom(payload) ?? `Could not remove ${personName(share)} (${response.status})`,
                );
            }

            confirmingRemove.value = null;

            if (revealed.id === share.id) {
                await hideNumber();
            }

            if (editor.id === share.id) {
                await closeEditor();
            }

            await props.onChanged$();
        } catch (err) {
            actionError.value = err instanceof Error ? err.message : `Could not remove ${personName(share)}.`;
        } finally {
            removing.value = null;
        }
    });

    const grouped = sharesByTarget(props.shares ?? []);
    const sharedTargets = props.targets.filter((target) => grouped.has(target.key));
    const editedTarget = props.targets.find((target) => target.key === editor.target);
    const connectedTargets = props.targets.filter((target) => target.kind === 'account');
    const virtualTargets = props.targets.filter((target) => target.kind === 'virtual');
    const alert = actionError.value ?? props.error;

    return (
        <section class="accounts-section cal-shell credit-section" aria-labelledby="shared-title">
            <div class="section-heading">
                <div>
                    <h2 id="shared-title">
                        Shared accounts <span>{String(sharedTargets.length).padStart(2, '0')}</span>
                    </h2>
                    <p>
                        Connected or virtual accounts you share with someone else. Personal numbers stay
                        hidden until you choose to show them.
                    </p>
                </div>

                {!editor.open && (
                    <button
                        type="button"
                        class="cat-primary credit-new"
                        disabled={!props.shares || props.targets.length === 0}
                        onClick$={() => openEditor(null, null)}
                    >
                        Share an account
                    </button>
                )}
            </div>

            {alert && (
                <div class="cal-alert" role="alert">
                    <Icon name="info" />
                    <div>
                        <strong>{alert}</strong>
                        {!actionError.value && (
                            <button type="button" class="cal-chip cat-retry" onClick$={() => props.onChanged$()}>
                                Try again
                            </button>
                        )}
                    </div>
                </div>
            )}

            {editor.open && (
                <div class="overview-panel cat-editor">
                    <form preventdefault:submit onSubmit$={save} aria-labelledby="share-editor-title">
                        <div class="cat-editor-head">
                            <div>
                                <h2 id="share-editor-title">
                                    {editor.id === null ? 'Add a person to an account' : 'Edit person'}
                                </h2>
                                <p>{editedTarget ? targetLabel(editedTarget) : 'Pick the account you share'}</p>
                            </div>
                        </div>

                        <div class="cat-fields">
                            <div class="cat-field cat-field-wide">
                                <label for="share-account" class="cat-label">
                                    Account
                                </label>
                                <select
                                    id="share-account"
                                    class="cat-input"
                                    required
                                    disabled={editor.id !== null}
                                    aria-describedby="share-account-hint"
                                    onChange$={(_, element) => {
                                        editor.target = element.value;
                                    }}
                                >
                                    <option value="" selected={editor.target === ''}>
                                        Pick an account
                                    </option>
                                    {connectedTargets.length > 0 && (
                                        <optgroup label="Your accounts">
                                            {connectedTargets.map((target) => (
                                                <option
                                                    key={target.key}
                                                    value={target.key}
                                                    selected={editor.target === target.key}
                                                >
                                                    {targetLabel(target)}
                                                </option>
                                            ))}
                                        </optgroup>
                                    )}
                                    {virtualTargets.length > 0 && (
                                        <optgroup label="Virtual accounts">
                                            {virtualTargets.map((target) => (
                                                <option
                                                    key={target.key}
                                                    value={target.key}
                                                    selected={editor.target === target.key}
                                                >
                                                    {targetLabel(target)}
                                                </option>
                                            ))}
                                        </optgroup>
                                    )}
                                </select>
                                <span id="share-account-hint" class="cat-hint">
                                    {editor.id === null
                                        ? 'Add one person at a time. An account can be shared with several people.'
                                        : 'To move someone to another account, remove them here and add them there.'}
                                </span>
                            </div>

                            <div class="cat-field">
                                <label for="share-first-name" class="cat-label">
                                    First name
                                </label>
                                <input
                                    id="share-first-name"
                                    class="cat-input"
                                    type="text"
                                    maxLength={50}
                                    required
                                    autoComplete="off"
                                    value={editor.firstName}
                                    onInput$={(_, element) => {
                                        editor.firstName = element.value;
                                    }}
                                />
                            </div>

                            <div class="cat-field">
                                <label for="share-last-name" class="cat-label">
                                    Last name
                                </label>
                                <input
                                    id="share-last-name"
                                    class="cat-input"
                                    type="text"
                                    maxLength={50}
                                    required
                                    autoComplete="off"
                                    value={editor.lastName}
                                    onInput$={(_, element) => {
                                        editor.lastName = element.value;
                                    }}
                                />
                            </div>

                            <div class="cat-field cat-field-wide">
                                <label for="share-personal-number" class="cat-label">
                                    Personal number <span class="cat-optional">(optional)</span>
                                </label>
                                <input
                                    id="share-personal-number"
                                    class="cat-input"
                                    type="text"
                                    inputMode="numeric"
                                    autoComplete="off"
                                    maxLength={15}
                                    placeholder={
                                        editor.hasSavedNumber
                                            ? 'Saved and hidden · type a new one to replace it'
                                            : 'YYYYMMDD-XXXX'
                                    }
                                    disabled={editor.removeSavedNumber}
                                    aria-describedby="share-personal-number-hint"
                                    value={editor.personalNumber}
                                    onInput$={(_, element) => {
                                        editor.personalNumber = element.value;
                                    }}
                                />
                                <span id="share-personal-number-hint" class="cat-hint">
                                    {`Saved encrypted. It stays hidden until you press Show and hides again after ${revealSeconds} seconds.`}
                                </span>
                                {editor.hasSavedNumber && (
                                    <label class="share-remove-number">
                                        <input
                                            type="checkbox"
                                            checked={editor.removeSavedNumber}
                                            onChange$={(_, element) => {
                                                editor.removeSavedNumber = element.checked;

                                                if (element.checked) {
                                                    editor.personalNumber = '';
                                                }
                                            }}
                                        />
                                        Remove the saved personal number
                                    </label>
                                )}
                            </div>
                        </div>

                        {editor.error && (
                            <p class="cat-error" role="alert">
                                {editor.error}
                            </p>
                        )}

                        <div class="cat-actions">
                            <button type="button" class="cal-chip" onClick$={closeEditor}>
                                Cancel
                            </button>
                            <button type="submit" class="cat-primary" disabled={editor.saving}>
                                {editor.saving ? 'Saving…' : editor.id === null ? 'Add person' : 'Save changes'}
                            </button>
                        </div>
                    </form>
                </div>
            )}

            {!props.shares ? (
                !props.error && <p class="cal-empty credit-empty">Loading shared accounts…</p>
            ) : sharedTargets.length === 0 ? (
                !editor.open && (
                    <div class="overview-panel">
                        <p class="cal-empty">
                            No shared accounts yet. Mark an account as shared to keep track of who you share
                            it with.
                        </p>
                    </div>
                )
            ) : (
                <div class="accounts-grid">
                    {sharedTargets.map((target) => {
                        const people = grouped.get(target.key) ?? [];

                        return (
                            <article
                                key={target.key}
                                class="account-card credit-card share-card"
                                style={{ '--account-color': target.color }}
                            >
                                <div class="card-top">
                                    <span class="account-icon">
                                        <Icon name="users" />
                                    </span>
                                    <span class="card-bank">{target.bank}</span>
                                    <span class="credit-counting">
                                        {people.length === 1 ? '1 person' : `${people.length} people`}
                                    </span>
                                </div>

                                <h3>{target.name}</h3>

                                <p class="account-purpose" title={targetLabel(target)}>
                                    {targetLabel(target)}
                                </p>

                                <ul class="share-people">
                                    {people.map((share) => {
                                        const isRevealed = revealed.id === share.id && revealed.value !== null;

                                        return (
                                            <li key={share.id} class="share-person">
                                                <div class="share-person-head">
                                                    <strong>{personName(share)}</strong>

                                                    {confirmingRemove.value !== share.id && (
                                                        <div class="share-person-actions">
                                                            <button
                                                                type="button"
                                                                class="cal-chip"
                                                                onClick$={() => openEditor(share, null)}
                                                            >
                                                                Edit
                                                            </button>
                                                            <button
                                                                type="button"
                                                                class="cal-chip"
                                                                onClick$={() => {
                                                                    confirmingRemove.value = share.id;
                                                                }}
                                                            >
                                                                Remove
                                                            </button>
                                                        </div>
                                                    )}
                                                </div>

                                                {share.hasPersonalNumber ? (
                                                    <div
                                                        class={{
                                                            'share-number': true,
                                                            'share-number-revealed': isRevealed,
                                                        }}
                                                    >
                                                        <span>Personal number</span>
                                                        <code aria-live="polite">
                                                            {isRevealed ? revealed.value : maskedPersonalNumber}
                                                        </code>
                                                        <button
                                                            type="button"
                                                            class="cal-chip share-reveal"
                                                            disabled={revealed.loadingId === share.id}
                                                            aria-label={`${isRevealed ? 'Hide' : 'Show'} the personal number for ${personName(share)}`}
                                                            onClick$={() => revealNumber(share)}
                                                        >
                                                            <Icon key={isRevealed ? 'eyeoff' : 'eye'} name={isRevealed ? 'eyeoff' : 'eye'} />
                                                            {revealed.loadingId === share.id
                                                                ? 'Showing…'
                                                                : isRevealed
                                                                  ? 'Hide'
                                                                  : 'Show'}
                                                        </button>
                                                        {isRevealed && (
                                                            <span class="share-countdown">
                                                                {`Hides in ${revealed.secondsLeft} s`}
                                                            </span>
                                                        )}
                                                    </div>
                                                ) : (
                                                    <p class="share-number">No personal number saved</p>
                                                )}

                                                {revealed.errorId === share.id && revealed.error && (
                                                    <p class="cat-error" role="alert">
                                                        {revealed.error}
                                                    </p>
                                                )}

                                                {confirmingRemove.value === share.id && (
                                                    <div class="share-confirm" role="group" aria-label="Confirm remove">
                                                        <span class="cat-confirm">
                                                            {share.hasPersonalNumber
                                                                ? `Remove ${share.firstName}? Their saved personal number is deleted too.`
                                                                : `Remove ${share.firstName} from this account?`}
                                                        </span>
                                                        <button
                                                            type="button"
                                                            class="cal-chip"
                                                            onClick$={() => {
                                                                confirmingRemove.value = null;
                                                            }}
                                                        >
                                                            Keep
                                                        </button>
                                                        <button
                                                            type="button"
                                                            class="cat-danger"
                                                            disabled={removing.value === share.id}
                                                            onClick$={() => remove(share)}
                                                        >
                                                            {removing.value === share.id ? 'Removing…' : 'Remove'}
                                                        </button>
                                                    </div>
                                                )}
                                            </li>
                                        );
                                    })}
                                </ul>

                                <div class="credit-actions">
                                    <button type="button" class="cal-chip" onClick$={() => openEditor(null, target.key)}>
                                        Add person
                                    </button>
                                </div>
                            </article>
                        );
                    })}
                </div>
            )}
        </section>
    );
});
