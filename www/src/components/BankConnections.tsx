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
    reconnectingId: number | null;
    removingId: number | null;
    error: string | null;
    onReconnect$: QRL<(connection: BankConnection) => void>;
    onRemove$: QRL<(connection: BankConnection) => void>;
}

function count(value: number, singular: string, plural: string): string {
    return `${value} ${value === 1 ? singular : plural}`;
}

export const BankConnectionsSection = component$<SectionProps>((props) => {
    const confirming = useSignal<number | null>(null);
    const now = Date.now();
    const connections = props.connections ?? [];

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
            </div>

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
                    <p class="cal-empty">No banks are connected to Zenith yet.</p>
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
