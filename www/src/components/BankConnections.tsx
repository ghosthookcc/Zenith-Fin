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
    error: string | null;
    onReconnect$: QRL<(connection: BankConnection) => void>;
}

export const BankConnectionsSection = component$<SectionProps>((props) => {
    const now = Date.now();
    const connections = props.connections ?? [];

    return (
        <section class="accounts-section cal-shell banks-section" aria-labelledby="banks-title">
            <div class="section-heading">
                <div>
                    <h2 id="banks-title">
                        Connected banks <span>{String(connections.length).padStart(2, '0')}</span>
                    </h2>
                    <p>Reconnect a bank to renew Zenith's access or to share accounts you opened since.</p>
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

                            return (
                                <li key={connection.id} class="bank-row">
                                    <span class="bank-monogram" aria-hidden="true">
                                        {connection.bank.charAt(0).toUpperCase()}
                                    </span>
                                    <span class="bank-row-main">
                                        <strong>{connection.bank}</strong>
                                        <small>
                                            {`${connection.accounts} ${
                                                connection.accounts === 1 ? 'account' : 'accounts'
                                            } shared · ${accessLabel(connection, now)}`}
                                        </small>
                                    </span>
                                    <span class={{ 'bank-state': true, 'bank-state-off': !active }}>
                                        {active ? 'Connected' : 'Needs reconnecting'}
                                    </span>
                                    <button
                                        type="button"
                                        class="cal-chip"
                                        disabled={props.reconnectingId !== null}
                                        onClick$={() => props.onReconnect$(connection)}
                                    >
                                        {props.reconnectingId === connection.id ? 'Opening the bank…' : 'Reconnect'}
                                    </button>
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
