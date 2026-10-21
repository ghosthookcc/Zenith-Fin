import {
  component$,
  $,
  useOnWindow,
  useSignal,
  useStore,
  useVisibleTask$,
} from "@builder.io/qwik";

import {
  availableBanksFrom,
  BankConnectionsSection,
  bankLoginUrl,
  ReconnectMenu,
  type AvailableBank,
  type BankConnection,
} from "./BankConnections";
import { CreditAccounts } from "./CreditAccounts";
import { VirtualAccounts } from "./VirtualAccounts";
import {
  accountShareKey,
  SharedAccounts,
  sharedWithLabel,
  sharesByTarget,
  virtualShareKey,
  type AccountShare,
  type ShareTarget,
} from "./SharedAccounts";
import {
  currentMonthRange,
  type MonthTransaction,
  type VirtualAccountRule,
} from "../utils/credit";
import {
  type ManualTransaction,
  manualMonthRows,
  manualTransactionsChangedEvent,
  openManualTransaction,
} from "../utils/manual";
import { Icon } from "./Icon";
import { accountColors as colors } from "../utils/accounts";
import { roleChoices, roleLabel } from "../utils/ledger";

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
  originalName?: string | null;
  alias?: string | null;
  currency: string;
  inConsent?: boolean;
  role?: string | null;
  roleSource?: string | null;
  balances: Balance[];
}

interface Rename {
  id: number | null;
  value: string;
  saving: boolean;
  error: string | null;
}

interface DisplayAccount {
  id: string;
  backendId: number;
  name: string;
  originalName: string | null;
  alias: string | null;
  bank: string;
  iban?: string | null;
  currency: string;
  inConsent: boolean;
  role: string;
  roleSource: string;
  balance: number;
  color: string;
  purpose: string;
  icon: "plane" | "home" | "heart" | "wallet" | "shield" | "layers";
  kind: "connected" | "virtual";
  shareKey: string;
}

const icons: DisplayAccount["icon"][] = [
  "wallet",
  "home",
  "shield",
  "layers",
  "heart",
  "plane",
];

function arcPath(start: number, end: number) {
  const point = (angle: number) => {
    const radians = ((angle - 90) * Math.PI) / 180;

    return `${180 + 137 * Math.cos(radians)} ${180 + 137 * Math.sin(radians)}`;
  };

  return `M ${point(start)} A 137 137 0 ${
    end - start > 180 ? 1 : 0
  } 1 ${point(end)}`;
}

function money(amount: number): string {
  return new Intl.NumberFormat("sv-SE", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
}

function percent(amount: number, total: number): string {
  if (total <= 0) {
    return "0";
  }

  return new Intl.NumberFormat("en-GB", {
    maximumFractionDigits: 1,
  }).format((amount / total) * 100);
}

function getMainBalance(account: BackendAccount): number {
  if (!account.balances?.length) {
    return 0;
  }

  const balance =
    account.balances.find(
      (item) => item.balanceType?.toLowerCase() === "closingbooked",
    ) ??
    account.balances.find(
      (item) => item.balanceType?.toLowerCase() === "expected",
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
    name: account.name || "Bank account",
    originalName: account.originalName ?? null,
    alias: account.alias ?? null,
    bank: account.bankName || "Unknown bank",
    iban: account.iban,
    currency: account.currency,
    inConsent: account.inConsent !== false,
    role: account.role ?? "EVERYDAY",
    roleSource: account.roleSource ?? "DEFAULT",
    balance: getMainBalance(account),
    color: colors[index % colors.length],
    purpose: account.iban ? `IBAN ${account.iban}` : "Connected bank account",
    icon: icons[index % icons.length],
    kind: "connected",
    shareKey: accountShareKey(account.id),
  };
}

export const AccountOverview = component$(() => {
  const backendAccounts = useSignal<BackendAccount[]>([]);

  const loading = useSignal(true);
  const error = useSignal<string | null>(null);

  const hidden = useSignal(false);
  const selected = useSignal<string | null>(null);

  const connections = useSignal<BankConnection[] | null>(null);
  const monthTransactions = useSignal<MonthTransaction[] | null>(null);
  const monthError = useSignal(false);
  const monthSyncing = useSignal(false);
  const shares = useSignal<AccountShare[] | null>(null);
  const sharesError = useSignal<string | null>(null);
  const virtualAccounts = useSignal<VirtualAccountRule[] | null>(null);
  const virtualError = useSignal<string | null>(null);

  const loadMonth = $(async () => {
    monthError.value = false;

    const month = currentMonthRange();
    const query = new URLSearchParams({ from: month.from, to: month.to });

    const fetchMonth = async () => {
      const response = await fetch(`/api/transactions?${query}`, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      const payload = (await response.json().catch(() => null)) as {
        accounts?: { isActive: boolean; isStale: boolean }[];
        transactions?: MonthTransaction[];
        manualTransactions?: ManualTransaction[];
      } | null;

      if (!response.ok || !payload || !Array.isArray(payload.transactions)) {
        throw new Error("Could not load transactions");
      }

      return {
        accounts: payload.accounts,
        transactions: [
          ...payload.transactions,
          ...manualMonthRows(payload.manualTransactions ?? []),
        ],
      };
    };

    try {
      const payload = await fetchMonth();

      monthTransactions.value = payload.transactions ?? [];

      if (
        !payload.accounts?.some(
          (account) => account.isActive && account.isStale,
        )
      ) {
        return;
      }

      monthSyncing.value = true;

      try {
        await fetch("/api/transactions/sync?force=false", {
          method: "POST",
          headers: {
            Accept: "application/json",
          },
          credentials: "same-origin",
          cache: "no-store",
        });

        monthTransactions.value = (await fetchMonth()).transactions ?? [];
      } finally {
        monthSyncing.value = false;
      }
    } catch {
      monthError.value = monthTransactions.value === null;
    }
  });

  const loadShares = $(async () => {
    sharesError.value = null;

    try {
      const response = await fetch("/api/account-shares", {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      const payload = (await response.json().catch(() => null)) as {
        shares?: AccountShare[];
        message?: string;
      } | null;

      if (!response.ok || !payload || !Array.isArray(payload.shares)) {
        throw new Error(
          payload?.message ??
            `Could not load shared accounts (${response.status})`,
        );
      }

      shares.value = payload.shares;
    } catch (err) {
      sharesError.value =
        err instanceof Error ? err.message : "Could not load shared accounts.";
    }
  });

  const loadVirtualAccounts = $(async () => {
    virtualError.value = null;

    try {
      const response = await fetch("/api/virtual-accounts", {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      const payload = (await response.json().catch(() => null)) as {
        virtualAccounts?: VirtualAccountRule[];
        message?: string;
      } | null;

      if (!response.ok || !payload || !Array.isArray(payload.virtualAccounts)) {
        throw new Error(
          payload?.message ??
            `Could not load virtual accounts (${response.status})`,
        );
      }

      virtualAccounts.value = payload.virtualAccounts;
    } catch (err) {
      virtualError.value =
        err instanceof Error ? err.message : "Could not load virtual accounts.";
    }
  });

  const reloadVirtualAccounts = $(async () => {
    await Promise.all([loadVirtualAccounts(), loadShares()]);
  });

  useOnWindow(
    manualTransactionsChangedEvent,
    $(async () => {
      await Promise.all([loadMonth(), loadVirtualAccounts()]);
    }),
  );

  const reconnectingId = useSignal<number | null>(null);
  const removingId = useSignal<number | null>(null);
  const availableBanks = useSignal<AvailableBank[] | null>(null);
  const connectingBank = useSignal<string | null>(null);
  const bankError = useSignal<string | null>(null);
  const bankNotice = useSignal<string | null>(null);

  const loadConnections = $(async () => {
    try {
      const response = await fetch("/api/aspsps/connections", {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      if (!response.ok) {
        throw new Error(`Could not load your banks (${response.status})`);
      }

      connections.value = (await response.json()) as BankConnection[];
    } catch (err) {
      connections.value = [];
      bankError.value =
        err instanceof Error ? err.message : "Could not load your banks.";
    }
  });

  const reconnect = $(async (connection: BankConnection) => {
    if (reconnectingId.value !== null) {
      return;
    }

    reconnectingId.value = connection.id;
    bankError.value = null;

    try {
      window.location.assign(await bankLoginUrl(connection));
    } catch (err) {
      bankError.value =
        err instanceof Error
          ? err.message
          : `Could not start reconnecting ${connection.bank}.`;
      reconnectingId.value = null;
    }
  });

  const loadAvailableBanks = $(async () => {
    try {
      const response = await fetch("/api/aspsps/inactive", {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      const payload = (await response.json().catch(() => null)) as {
        success?: boolean;
        message?: string;
      } | null;

      if (!response.ok || !payload?.success) {
        throw new Error(
          payload?.message ||
            `Could not load the banks you can add (${response.status})`,
        );
      }

      availableBanks.value = availableBanksFrom(payload);
    } catch (err) {
      availableBanks.value = [];
      bankError.value =
        err instanceof Error
          ? err.message
          : "Could not load the banks you can add.";
    }
  });

  const connectBank = $(async (bank: AvailableBank) => {
    if (connectingBank.value !== null || reconnectingId.value !== null) {
      return;
    }

    connectingBank.value = bank.bank;
    bankError.value = null;

    try {
      window.location.assign(await bankLoginUrl(bank));
    } catch (err) {
      bankError.value =
        err instanceof Error ? err.message : `Could not open ${bank.bank}.`;
      connectingBank.value = null;
    }
  });

  const rename = useStore<Rename>({
    id: null,
    value: "",
    saving: false,
    error: null,
  });

  const saveRole = $(async (id: number, role: string) => {
    if (rename.saving) {
      return;
    }

    rename.saving = true;
    rename.error = null;

    try {
      const response = await fetch(`/api/accounts/${id}/role`, {
        method: "PUT",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        credentials: "same-origin",
        body: JSON.stringify({ role }),
      });

      if (!response.ok) {
        const payload = (await response.json().catch(() => null)) as {
          message?: string;
        } | null;

        throw new Error(
          payload?.message ??
            `Could not change the account type (${response.status})`,
        );
      }

      backendAccounts.value = backendAccounts.value.map((account) =>
        account.id === id ? { ...account, role, roleSource: "USER" } : account,
      );
    } catch (err) {
      rename.error =
        err instanceof Error
          ? err.message
          : "Could not change the account type.";
    } finally {
      rename.saving = false;
    }
  });

  const saveName = $(async (id: number, useBankName: boolean) => {
    if (rename.saving) {
      return;
    }

    rename.saving = true;
    rename.error = null;

    try {
      const response = await fetch(`/api/accounts/${id}/alias`, {
        method: "PUT",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        credentials: "same-origin",
        body: JSON.stringify({ alias: useBankName ? null : rename.value }),
      });

      const text = await response.text();
      let payload: unknown = null;

      try {
        payload = text ? JSON.parse(text) : null;
      } catch {
        payload = null;
      }

      if (!response.ok) {
        const message = (payload as { message?: unknown } | null)?.message;

        throw new Error(
          typeof message === "string"
            ? message
            : `Could not rename the account (${response.status})`,
        );
      }

      const alias =
        (payload as { alias?: string | null } | null)?.alias ?? null;

      backendAccounts.value = backendAccounts.value.map((account) =>
        account.id === id
          ? {
              ...account,
              alias,
              name: alias ?? account.originalName ?? account.name,
            }
          : account,
      );
      rename.id = null;
    } catch (err) {
      rename.error =
        err instanceof Error ? err.message : "Could not rename the account.";
    } finally {
      rename.saving = false;
    }
  });

  const loadAccounts = $(async () => {
    loading.value = true;
    error.value = null;

    const controller = new AbortController();

    const timeout = setTimeout(() => {
      controller.abort();
    }, 300_000);

    try {
      const response = await fetch("/api/accounts/expanded", {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
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
          typeof data === "object" &&
          !Array.isArray(data) &&
          "message" in data
        ) {
          const backendMessage = (data as { message?: unknown }).message;

          if (typeof backendMessage === "string") {
            message = backendMessage;
          }
        }

        throw new Error(message);
      }

      if (!Array.isArray(data)) {
        throw new Error("Expected the account API to return an array.");
      }

      backendAccounts.value = data as BackendAccount[];
    } catch (err) {
      if (controller.signal.aborted) {
        error.value = "The account request timed out. Please try again.";
      } else if (err instanceof Error) {
        error.value = err.message;
      } else {
        error.value = "Could not load accounts.";
      }
    } finally {
      clearTimeout(timeout);
      loading.value = false;
    }
  });

  const removeBank = $(async (connection: BankConnection) => {
    if (removingId.value !== null) {
      return;
    }

    removingId.value = connection.id;
    bankError.value = null;
    bankNotice.value = null;

    try {
      const response = await fetch(`/api/aspsps/connections/${connection.id}`, {
        method: "DELETE",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
      });

      const payload = (await response.json().catch(() => null)) as {
        message?: string;
        bank?: string;
        accounts?: number;
        transactions?: number;
        revoked?: boolean;
      } | null;

      if (!response.ok || !payload) {
        throw new Error(
          payload?.message || `Could not remove ${connection.bank}`,
        );
      }

      const accounts = payload.accounts ?? 0;
      const transactions = payload.transactions ?? 0;

      bankNotice.value = `${connection.bank} is removed from Zenith, along with ${accounts} ${
        accounts === 1 ? "account" : "accounts"
      } and ${transactions} ${transactions === 1 ? "transaction" : "transactions"}.${
        payload.revoked
          ? ""
          : " Zenith couldn't end its access at the bank, so it ends when the consent runs out."
      }`;

      await loadConnections();
      await loadAvailableBanks();
      await loadAccounts();
      await loadShares();
    } catch (err) {
      bankError.value =
        err instanceof Error
          ? err.message
          : `Could not remove ${connection.bank}.`;
    } finally {
      removingId.value = null;
    }
  });

  useVisibleTask$(
    () => {
      const status = new URLSearchParams(window.location.search).get("bank");

      if (status === "connected") {
        bankNotice.value =
          "Your bank is connected. The accounts you shared with Zenith show up here.";
      } else if (status === "failed") {
        bankError.value =
          "The bank connection didn't go through. Try reconnecting again.";
      }

      if (status) {
        window.history.replaceState(null, "", window.location.pathname);
      }

      void loadAccounts();
      void loadConnections();
      void loadMonth();
      void loadShares();
      void loadVirtualAccounts();
      void loadAvailableBanks();
    },
    {
      strategy: "document-ready",
    },
  );

  const accounts = backendAccounts.value.map(makeDisplayAccount);

  const virtualOverview: DisplayAccount[] = (virtualAccounts.value ?? [])
    .filter((account) => account.currentBalance != null)
    .map((account, index) => ({
      id: `virtual-${account.id}`,
      backendId: account.id,
      name: account.name,
      originalName: null,
      alias: null,
      bank: "Virtual account",
      iban: account.iban ?? null,
      currency: "SEK",
      inConsent: true,
      role: "EVERYDAY",
      roleSource: "DEFAULT",
      balance: account.currentBalance ?? 0,
      color: colors[(accounts.length + index) % colors.length],
      purpose: `Account ${account.accountNumber}`,
      icon: "layers",
      kind: "virtual",
      shareKey: virtualShareKey(account.id),
    }));

  const overviewAccounts = [...accounts, ...virtualOverview];

  const connectedBanks = new Set(accounts.map((account) => account.bank)).size;

  const sharedByTarget = sharesByTarget(shares.value ?? []);

  const shareTargets: ShareTarget[] = [
    ...accounts.map((account) => ({
      key: accountShareKey(account.backendId),
      kind: "account" as const,
      id: account.backendId,
      name: account.name,
      bank: account.bank,
      iban: account.iban,
      color: account.color,
    })),
    ...(virtualAccounts.value ?? []).map((account) => ({
      key: virtualShareKey(account.id),
      kind: "virtual" as const,
      id: account.id,
      name: account.name,
      bank: "Virtual account",
      accountNumber: account.accountNumber,
      color: "#8792a5",
    })),
  ];

  const totalBalance = overviewAccounts.reduce(
    (sum, account) => sum + account.balance,
    0,
  );

  const active = overviewAccounts.find(
    (account) => account.id === selected.value,
  );

  let angle = 0;

  const segments = overviewAccounts.map((account) => {
    const start = angle;

    const share = totalBalance > 0 ? account.balance / totalBalance : 0;

    angle += share * 360;

    return { ...account, path: arcPath(start + 1.7, angle - 1.7) };
  });

  const banks = overviewAccounts.reduce<
    Record<string, { count: number; total: number }>
  >((result, account) => {
    const current = result[account.bank] ?? { count: 0, total: 0 };

    current.count += 1;
    current.total += account.balance;

    result[account.bank] = current;

    return result;
  }, {});

  return (
    <>
      <main id="main" class="page-shell">
        <section class="page-heading" aria-labelledby="page-title">
          <div>
            <h1 id="page-title">
              Accounts overview<span>.</span>
            </h1>
          </div>

          <div class="page-actions">
            <ReconnectMenu
              connections={connections.value}
              reconnectingId={reconnectingId.value}
              onReconnect$={reconnect}
            />

            <button
              class="visibility-button"
              type="button"
              onClick$={() => {
                hidden.value = !hidden.value;
              }}
              aria-pressed={hidden.value}
            >
              <Icon name={hidden.value ? "eyeoff" : "eye"} />

              {hidden.value ? "Show balances" : "Hide balances"}
            </button>

            <button
              class="visibility-button"
              type="button"
              onClick$={() => openManualTransaction()}
            >
              <Icon name="plus" />
              New transaction
            </button>
          </div>
        </section>

        {bankNotice.value && (
          <div class="cal-alert bank-notice" role="status">
            <Icon name="check" />
            <div>
              <strong>{bankNotice.value}</strong>
            </div>
          </div>
        )}

        {loading.value && (
          <section class="overview-panel">
            <p>Loading account balances...</p>
          </section>
        )}

        {error.value && (
          <section class="overview-panel">
            <p role="alert">Could not load accounts: {error.value}</p>

            <button
              type="button"
              class="cal-chip cat-retry"
              onClick$={loadAccounts}
            >
              Try again
            </button>
          </section>
        )}

        {!loading.value && !error.value && accounts.length === 0 && (
          <section class="overview-panel">
            <p>No connected accounts found.</p>
          </section>
        )}

        {!loading.value && !error.value && accounts.length > 0 && (
          <>
            <section class="overview-panel" aria-labelledby="overview-title">
              <div class="panel-top">
                <h2 id="overview-title">
                  <span class="tiny-mark" />
                  Financial overview
                </h2>

                <span class="soft-badge">
                  {overviewAccounts.length}{" "}
                  {overviewAccounts.length === 1 ? "account" : "accounts"}
                  {" · "}
                  {connectedBanks} {connectedBanks === 1 ? "bank" : "banks"}
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

                      {segments.map((account) => (
                        <path
                          key={account.id}
                          d={account.path}
                          fill="none"
                          stroke={account.color}
                          stroke-width={selected.value === account.id ? 37 : 31}
                          stroke-linecap="butt"
                          opacity={
                            !selected.value || selected.value === account.id
                              ? 1
                              : 0.19
                          }
                          role="button"
                          tabindex={0}
                          aria-label={`${account.name}, ${percent(
                            account.balance,
                            totalBalance,
                          )} percent of total`}
                          aria-pressed={selected.value === account.id}
                          onClick$={() => {
                            selected.value =
                              selected.value === account.id ? null : account.id;
                          }}
                          onKeyDown$={(event) => {
                            if (event.key === "Enter" || event.key === " ") {
                              selected.value =
                                selected.value === account.id
                                  ? null
                                  : account.id;
                            }
                          }}
                        />
                      ))}

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
                        {active ? active.name : "Total balance"}
                      </span>

                      <strong
                        class={{
                          "balance-value": true,
                          masked: hidden.value,
                        }}
                      >
                        {hidden.value
                          ? "•• •••,••"
                          : money(active?.balance ?? totalBalance)}
                      </strong>

                      <span class="chart-currency">
                        SEK <span>Swedish krona</span>
                      </span>

                      {active && (
                        <span
                          class="chart-share"
                          style={{
                            color: active.color,
                          }}
                        >
                          {percent(active.balance, totalBalance)}% of total
                        </span>
                      )}
                    </div>
                  </div>

                  <button
                    class="chart-reset"
                    type="button"
                    disabled={!selected.value}
                    onClick$={() => {
                      selected.value = null;
                    }}
                  >
                    {selected.value
                      ? "Show total balance"
                      : "Across all your accounts"}
                  </button>
                </div>

                <div class="allocation">
                  <div class="allocation-heading">
                    <h3>Where your money sits</h3>
                    <span>Share</span>
                  </div>

                  <div class="allocation-list">
                    {overviewAccounts.map((account) => (
                      <button
                        type="button"
                        key={account.id}
                        class={{
                          "allocation-row": true,
                          selected: selected.value === account.id,
                        }}
                        aria-pressed={selected.value === account.id}
                        onClick$={() => {
                          selected.value =
                            selected.value === account.id ? null : account.id;
                        }}
                      >
                        <span
                          class="allocation-dot"
                          style={{
                            backgroundColor: account.color,
                          }}
                        />

                        <span class="allocation-name">
                          {account.name}

                          <small>{account.bank}</small>
                        </span>

                        <span class="allocation-amount">
                          {hidden.value ? "•• •••,••" : money(account.balance)}

                          <small>kr</small>
                        </span>

                        <span class="allocation-percent">
                          {percent(account.balance, totalBalance)}%
                        </span>
                      </button>
                    ))}
                  </div>
                </div>
              </div>

              <div class="bank-breakdown">
                {Object.entries(banks).map(([bank, data]) => (
                  <div class="bank-summary" key={bank}>
                    <span class="bank-monogram">
                      {bank.charAt(0).toUpperCase()}
                    </span>

                    <span class="bank-label">
                      {bank}

                      <small>
                        {data.count} {data.count === 1 ? "account" : "accounts"}
                      </small>
                    </span>

                    <span class="bank-total">
                      {hidden.value ? "•• •••,••" : money(data.total)}{" "}
                      <small>kr</small>
                    </span>

                    <span class="bank-share">
                      {percent(data.total, totalBalance)}%
                    </span>
                  </div>
                ))}
              </div>
            </section>

            <section class="accounts-section" aria-labelledby="accounts-title">
              <div class="section-heading">
                <div>
                  <h2 id="accounts-title">
                    Your accounts{" "}
                    <span>
                      {String(overviewAccounts.length).padStart(2, "0")}
                    </span>
                  </h2>

                  <p>Balances and share of your total.</p>
                </div>

                <span class="section-meta">Balances in SEK</span>
              </div>

              <div class="accounts-grid">
                {overviewAccounts.map((account, index) => (
                  <article
                    key={account.id}
                    class={{
                      "account-card": true,
                      "account-highlighted": selected.value === account.id,
                    }}
                    style={{
                      "--account-color": account.color,
                      "--account-share": `${
                        totalBalance > 0
                          ? (account.balance / totalBalance) * 100
                          : 0
                      }%`,
                    }}
                  >
                    <div class="card-top">
                      <span class="account-icon">
                        <Icon name={account.icon} />
                      </span>

                      <span class="card-bank">
                        {account.kind === "connected"
                          ? `${account.bank} · ${roleLabel(account.role)}`
                          : account.bank}
                      </span>

                      <span class="account-index">
                        {String(index + 1).padStart(2, "0")}
                      </span>
                    </div>

                    {account.kind === "connected" &&
                    rename.id === account.backendId ? (
                      <form
                        class="account-rename cal-shell"
                        preventdefault:submit
                        onSubmit$={() => saveName(account.backendId, false)}
                      >
                        <label
                          class="account-rename-label"
                          for={`account-name-${account.backendId}`}
                        >
                          Name in Zenith
                        </label>
                        <input
                          id={`account-name-${account.backendId}`}
                          class="account-rename-input"
                          type="text"
                          maxLength={40}
                          placeholder={account.originalName ?? "Bank account"}
                          value={rename.value}
                          onInput$={(_, element) => {
                            rename.value = element.value;
                          }}
                        />
                        <label
                          class="account-rename-label"
                          for={`account-role-${account.backendId}`}
                        >
                          Account type
                        </label>
                        <select
                          id={`account-role-${account.backendId}`}
                          class="account-rename-input"
                          disabled={rename.saving}
                          onChange$={(_, element) =>
                            saveRole(account.backendId, element.value)
                          }
                        >
                          {roleChoices.map((choice) => (
                            <option
                              key={choice.value}
                              value={choice.value}
                              selected={account.role === choice.value}
                            >
                              {choice.label}
                            </option>
                          ))}
                        </select>
                        <div class="account-rename-actions">
                          <button
                            type="submit"
                            class="cal-chip"
                            disabled={rename.saving}
                          >
                            {rename.saving ? "Saving…" : "Save"}
                          </button>
                          <button
                            type="button"
                            class="cal-chip"
                            onClick$={() => {
                              rename.id = null;
                            }}
                          >
                            Cancel
                          </button>
                          {account.alias && (
                            <button
                              type="button"
                              class="cal-chip"
                              disabled={rename.saving}
                              onClick$={() => saveName(account.backendId, true)}
                            >
                              Use the bank's name
                            </button>
                          )}
                        </div>
                        {rename.error && (
                          <p class="account-rename-error" role="alert">
                            {rename.error}
                          </p>
                        )}
                      </form>
                    ) : (
                      <div class="account-name-row">
                        <h3>{account.name}</h3>
                        {account.kind === "connected" && (
                          <button
                            type="button"
                            class="account-rename-button"
                            title="Rename"
                            aria-label={`Rename ${account.name}`}
                            onClick$={() => {
                              rename.id = account.backendId;
                              rename.value = account.alias ?? "";
                              rename.error = null;
                            }}
                          >
                            <Icon name="pencil" />
                          </button>
                        )}
                      </div>
                    )}

                    {account.alias &&
                      account.originalName &&
                      rename.id !== account.backendId && (
                        <p class="account-original">
                          {`At the bank: ${account.originalName}`}
                        </p>
                      )}

                    {!account.inConsent && (
                      <p class="account-stale" role="note">
                        <Icon name="info" />
                        <span>{`${account.bank} no longer shares this account`}</span>
                      </p>
                    )}

                    {sharedByTarget.has(account.shareKey) && (
                      <p
                        class="account-shared"
                        title={sharedWithLabel(
                          sharedByTarget.get(account.shareKey)!,
                        )}
                      >
                        <Icon name="users" />
                        <span>
                          {sharedWithLabel(
                            sharedByTarget.get(account.shareKey)!,
                          )}
                        </span>
                      </p>
                    )}

                    <p class="account-purpose">{account.purpose}</p>

                    <div class="card-balance">
                      {hidden.value ? "•• •••,••" : money(account.balance)}{" "}
                      <span>kr</span>
                    </div>

                    <div class="share-track" aria-hidden="true">
                      <span />
                    </div>

                    <div class="card-footer">
                      <span>Share of total</span>

                      <strong>{percent(account.balance, totalBalance)}%</strong>
                    </div>
                  </article>
                ))}
              </div>
            </section>

            <CreditAccounts
              accounts={accounts.map((account) => ({
                id: account.backendId,
                name: account.name,
                bank: account.bank,
                iban: account.iban,
                currency: account.currency,
                color: account.color,
                inConsent: account.inConsent,
              }))}
              hidden={hidden.value}
              monthTransactions={monthTransactions.value}
              monthError={monthError.value}
              syncing={monthSyncing.value}
            />

            <VirtualAccounts
              virtualAccounts={virtualAccounts.value}
              error={virtualError.value}
              shares={shares.value}
              hidden={hidden.value}
              monthTransactions={monthTransactions.value}
              monthError={monthError.value}
              onChanged$={reloadVirtualAccounts}
            />

            <SharedAccounts
              targets={shareTargets}
              shares={shares.value}
              error={sharesError.value}
              onChanged$={loadShares}
            />
          </>
        )}

        <BankConnectionsSection
          connections={connections.value}
          available={availableBanks.value}
          reconnectingId={reconnectingId.value}
          removingId={removingId.value}
          connectingBank={connectingBank.value}
          error={bankError.value}
          onReconnect$={reconnect}
          onRemove$={removeBank}
          onAdd$={connectBank}
        />
      </main>
    </>
  );
});
