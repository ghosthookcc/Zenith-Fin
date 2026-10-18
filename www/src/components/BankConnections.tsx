import { component$, useSignal, type QRL } from '@builder.io/qwik';

import { Icon } from './Icon';

export interface BankConnection {
    id: number;
    bank: string;
    country: string;
    psuType: string;
    status: string;
    consentExpiresAt: string;
    accounts: number;
    transactions: number;
}

export interface AvailableBank {
    bank: string;
    country: string;
    psuType: string;
}

export async function bankLoginUrl(bank: AvailableBank): Promise<string> {
    const response = await fetch('/api/aspsps', {
        method: 'POST',
        headers: {
            'Accept': 'application/json',
            'Content-Type': 'application/json',
        },
        credentials: 'same-origin',
        body: JSON.stringify([{ bank: bank.bank, country: bank.country, psuType: bank.psuType }]),
    });

    const payload = (await response.json().catch(() => null)) as {
        message?: string;
        urls?: { url?: string }[];
    } | null;
    const url = payload?.urls?.[0]?.url;

    if (!response.ok || typeof url !== 'string') {
        throw new Error(payload?.message || `Could not open ${bank.bank}`);
    }

    return url;
}

export function availableBanksFrom(payload: unknown): AvailableBank[] {
    const groups = (payload as { aspsps?: { aspsps?: Record<string, { country: string; psuType: string }>[] } } | null)
        ?.aspsps?.aspsps ?? [];

    return groups.flatMap((group) =>
        Object.entries(group).map(([bank, details]) => ({
            bank,
            country: details.country,
            psuType: details.psuType,
        })),
    );
}

function consentActive(connection: BankConnection, now: number): boolean {
    return connection.status === 'ACTIVE' && Date.parse(connection.consentExpiresAt) > now;
}

function accessLabel(connection: BankConnection, now: number): string {
    const date = new Intl.DateTimeFormat('en-GB', {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
    }).format(new Date(connection.consentExpiresAt));

    return consentActive(connection, now) ? `Access until ${date}` : `Access ended ${date}`;
}

interface SectionProps {
    connections: BankConnection[] | null;
    available: AvailableBank[] | null;
    reconnectingId: number | null;
    removingId: number | null;
    connectingBank: string | null;
    error: string | null;
    onReconnect$: QRL<(connection: BankConnection) => void>;
    onRemove$: QRL<(connection: BankConnection) => void>;
    onAdd$: QRL<(bank: AvailableBank) => void>;
}

function count(value: number, singular: string, plural: string): string {
    return `${value} ${value === 1 ? singular : plural}`;
}

export const BankConnectionsSection = component$<SectionProps>((props) => {
    const confirming = useSignal<number | null>(null);
    const adding = useSignal(false);
    const now = Date.now();
    const connections = props.connections ?? [];
    const available = props.available;

    return (
        <section class="accounts-section cal-shell banks-section" aria-labelledby="banks-title">
            <div class="section-heading">
                <div>
                    <h2 id="banks-title">
                        Connected banks <span>{String(connections.length).padStart(2, '0')}</span>
                    </h2>
                    <p>
                        Reconnect a bank to renew Zenith's access or to share accounts you opened since,
                        or remove one to delete its data from Zenith.
                    </p>
                </div>

                <button
                    type="button"
                    class="cat-primary bank-add-button"
                    aria-expanded={adding.value}
                    aria-controls="bank-add-panel"
                    onClick$={() => {
                        adding.value = !adding.value;
                    }}
                >
                    Add a bank
                </button>
            </div>

            {adding.value && (
                <div id="bank-add-panel" class="overview-panel bank-add" role="group" aria-labelledby="bank-add-title">
                    <div class="bank-add-head">
                        <h3 id="bank-add-title">Add a bank</h3>
                        <button
                            type="button"
                            class="cal-chip"
                            onClick$={() => {
                                adding.value = false;
                            }}
                        >
                            Close
                        </button>
                    </div>

                    {available === null ? (
                        <p class="cal-empty">Loading the banks you can add…</p>
                    ) : available.length === 0 ? (
                        <p class="cal-empty">Every bank Zenith supports is already connected.</p>
                    ) : (
                        <ul class="bank-list">
                            {available.map((bank) => (
                                <li key={bank.bank} class="bank-row bank-row-add">
                                    <span class="bank-monogram" aria-hidden="true">
                                        {bank.bank.charAt(0).toUpperCase()}
                                    </span>
                                    <span class="bank-row-main">
                                        <strong>{bank.bank}</strong>
                                        <small>{`${bank.country} · ${bank.psuType === 'personal' ? 'Personal' : bank.psuType}`}</small>
                                    </span>
                                    <button
                                        type="button"
                                        class="cal-chip"
                                        disabled={props.connectingBank !== null || props.reconnectingId !== null}
                                        onClick$={() => props.onAdd$(bank)}
                                    >
                                        {props.connectingBank === bank.bank ? 'Opening the bank…' : 'Connect'}
                                    </button>
                                </li>
                            ))}
                        </ul>
                    )}

                    <p class="bank-add-note">
                        You log in at the bank and choose which accounts to share with Zenith.
                    </p>
                </div>
            )}

            {props.error && (
                <div class="cal-alert" role="alert">
                    <Icon name="info" />
                    <div>
                        <strong>{props.error}</strong>
                    </div>
                </div>
            )}

            <div class="overview-panel">
                {props.connections === null ? (
                    <p class="cal-empty">Loading your banks…</p>
                ) : connections.length === 0 ? (
                    <p class="cal-empty">No banks are connected to Zenith yet. Use Add a bank to connect one.</p>
                ) : (
                    <ul class="bank-list">
                        {connections.map((connection) => {
                            const active = consentActive(connection, now);

                            const busy = props.reconnectingId !== null || props.removingId !== null;

                            return (
                                <li key={connection.id} class="bank-row">
                                    <span class="bank-monogram" aria-hidden="true">
                                        {connection.bank.charAt(0).toUpperCase()}
                                    </span>
                                    <span class="bank-row-main">
                                        <strong>{connection.bank}</strong>
                                        <small>
                                            {`${count(connection.accounts, 'account', 'accounts')} shared · ${accessLabel(
                                                connection,
                                                now,
                                            )}`}
                                        </small>
                                    </span>
                                    <span class={{ 'bank-state': true, 'bank-state-off': !active }}>
                                        {active ? 'Connected' : 'Needs reconnecting'}
                                    </span>
                                    <span class="bank-actions">
                                        <button
                                            type="button"
                                            class="cal-chip"
                                            disabled={busy}
                                            onClick$={() => props.onReconnect$(connection)}
                                        >
                                            {props.reconnectingId === connection.id ? 'Opening the bank…' : 'Reconnect'}
                                        </button>
                                        <button
                                            type="button"
                                            class="cal-chip"
                                            disabled={busy}
                                            aria-expanded={confirming.value === connection.id}
                                            onClick$={() => {
                                                confirming.value =
                                                    confirming.value === connection.id ? null : connection.id;
                                            }}
                                        >
                                            Remove
                                        </button>
                                    </span>

                                    {confirming.value === connection.id && (
                                        <div class="bank-confirm" role="group" aria-label={`Remove ${connection.bank}`}>
                                            <p>
                                                {`Remove ${connection.bank}? Zenith ends its access at the bank and deletes ${count(
                                                    connection.accounts,
                                                    'account',
                                                    'accounts',
                                                )} and ${count(
                                                    connection.transactions,
                                                    'transaction',
                                                    'transactions',
                                                )} from Zenith. You can connect it again later.`}
                                            </p>
                                            <span class="bank-actions">
                                                <button
                                                    type="button"
                                                    class="cal-chip"
                                                    onClick$={() => {
                                                        confirming.value = null;
                                                    }}
                                                >
                                                    Keep
                                                </button>
                                                <button
                                                    type="button"
                                                    class="cat-danger"
                                                    disabled={props.removingId !== null}
                                                    onClick$={() => props.onRemove$(connection)}
                                                >
                                                    {props.removingId === connection.id ? 'Removing…' : `Remove ${connection.bank}`}
                                                </button>
                                            </span>
                                        </div>
                                    )}
                                </li>
                            );
                        })}
                    </ul>
                )}
            </div>
        </section>
    );
});

interface MenuProps {
    connections: BankConnection[] | null;
    reconnectingId: number | null;
    onReconnect$: QRL<(connection: BankConnection) => void>;
}

export const ReconnectMenu = component$<MenuProps>((props) => {
    const open = useSignal(false);
    const connections = props.connections ?? [];

    return (
        <div
            class="reconnect-menu"
            onKeyDown$={(event) => {
                if (event.key === 'Escape') {
                    open.value = false;
                }
            }}
        >
            <button
                type="button"
                class="visibility-button"
                aria-expanded={open.value}
                aria-controls="reconnect-menu-list"
                disabled={connections.length === 0}
                onClick$={() => {
                    open.value = !open.value;
                }}
            >
                <Icon name="refresh" />
                Reconnect a bank
            </button>

            {open.value && (
                <div id="reconnect-menu-list" class="reconnect-menu-list" role="group" aria-label="Banks you can reconnect">
                    {connections.map((connection) => (
                        <button
                            key={connection.id}
                            type="button"
                            class="reconnect-menu-item"
                            disabled={props.reconnectingId !== null}
                            onClick$={() => props.onReconnect$(connection)}
                        >
                            <span>{connection.bank}</span>
                            <small>
                                {props.reconnectingId === connection.id
                                    ? 'Opening the bank…'
                                    : accessLabel(connection, Date.now())}
                            </small>
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
});
