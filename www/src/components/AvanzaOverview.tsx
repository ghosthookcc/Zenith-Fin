import { $, component$, useSignal, useVisibleTask$ } from "@builder.io/qwik";

import { AvanzaChart } from "./AvanzaChart";
import { Icon } from "./Icon";
import {
  type AvanzaBalances,
  type AvanzaStatus,
  avanzaTypeLabel,
  loadAvanzaBalances,
  loadAvanzaStatus,
} from "../utils/avanza";

const maskedAmount = "•• •••,••";

function formatKr(amount: number): string {
  return new Intl.NumberFormat("sv-SE", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
}

function formatTime(value: string | null | undefined): string {
  if (!value) {
    return "never";
  }

  return new Intl.DateTimeFormat("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

function nextFetch(now: Date): string {
  const next = new Date(now);

  next.setHours(7, 0, 0, 0);

  while (next <= now || (next.getDay() !== 1 && next.getDay() !== 4)) {
    next.setDate(next.getDate() + 1);
    next.setHours(7, 0, 0, 0);
  }

  return new Intl.DateTimeFormat("en-GB", {
    weekday: "short",
    day: "numeric",
    month: "short",
  }).format(next);
}

function totalsByTime(balances: AvanzaBalances): [string, number][] {
  const totals = new Map<string, number>();

  for (const point of balances.history) {
    totals.set(
      point.fetchedAt,
      (totals.get(point.fetchedAt) ?? 0) + point.totalValue,
    );
  }

  return [...totals.entries()].sort(
    (a, b) => Date.parse(a[0]) - Date.parse(b[0]),
  );
}

function statusText(status: AvanzaStatus): {
  label: string;
  detail: string;
  tone: string;
} {
  if (!status.keyMatches || status.status === "KEY_MISMATCH") {
    return {
      label: "Enter your details again",
      detail:
        "The server's key was replaced, so the saved details can no longer be read.",
      tone: "warn",
    };
  }

  switch (status.status) {
    case "OK":
      return {
        label: "Working",
        detail: `Last fetched ${formatTime(status.lastSuccessAt)}.`,
        tone: "ok",
      };
    case "AUTH_FAILED":
      return {
        label: "Login refused",
        detail:
          "Avanza refused the login. Zenith will not try again until you replace your details.",
        tone: "bad",
      };
    case "ERROR":
      return {
        label: "Last fetch failed",
        detail: `The fetch failed${status.errorClass ? ` (${status.errorClass})` : ""}. It runs again at the next scheduled time.`,
        tone: "warn",
      };
    default:
      return {
        label: "Waiting for the first fetch",
        detail: "Values are fetched on Monday and Thursday mornings.",
        tone: "idle",
      };
  }
}

export const AvanzaOverview = component$(() => {
  const balances = useSignal<AvanzaBalances | null>(null);
  const status = useSignal<AvanzaStatus | null>(null);
  const loading = useSignal(true);
  const error = useSignal<string | null>(null);
  const hidden = useSignal(false);
  const replacing = useSignal(false);
  const confirmRemove = useSignal(false);
  const busy = useSignal(false);
  const formError = useSignal<string | null>(null);
  const notice = useSignal<string | null>(null);
  const form = useSignal<HTMLFormElement>();
  const usernameInput = useSignal<HTMLInputElement>();
  const passwordInput = useSignal<HTMLInputElement>();
  const totpInput = useSignal<HTMLInputElement>();

  const load = $(async () => {
    error.value = null;

    try {
      const [nextStatus, nextBalances] = await Promise.all([
        loadAvanzaStatus(),
        loadAvanzaBalances(),
      ]);

      status.value = nextStatus;
      balances.value = nextBalances;
    } catch (err) {
      error.value =
        err instanceof Error
          ? err.message
          : "Could not load your Avanza values.";
    } finally {
      loading.value = false;
    }
  });

  const save = $(async () => {
    if (busy.value) {
      return;
    }

    busy.value = true;
    formError.value = null;
    notice.value = null;

    try {
      const response = await fetch("/api/avanza/credentials", {
        method: "PUT",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
        body: JSON.stringify({
          username: usernameInput.value?.value ?? "",
          password: passwordInput.value?.value ?? "",
          totpSecret: totpInput.value?.value ?? "",
        }),
      });

      if (!response.ok) {
        const payload = (await response.json().catch(() => null)) as {
          message?: string;
        } | null;

        throw new Error(
          response.status === 429
            ? "Too many attempts. Wait a few minutes and try again."
            : (payload?.message ??
                `Could not save your details (${response.status})`),
        );
      }

      replacing.value = false;
      notice.value =
        "Your Avanza details are saved. Values are fetched on Monday and Thursday mornings.";
      await load();
    } catch (err) {
      formError.value =
        err instanceof Error ? err.message : "Could not save your details.";
    } finally {
      form.value?.reset();
      busy.value = false;
    }
  });

  const remove = $(async () => {
    if (busy.value) {
      return;
    }

    busy.value = true;
    formError.value = null;
    notice.value = null;

    try {
      const response = await fetch("/api/avanza/credentials", {
        method: "DELETE",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      if (!response.ok) {
        const payload = (await response.json().catch(() => null)) as {
          message?: string;
        } | null;

        throw new Error(
          payload?.message ??
            `Could not remove your details (${response.status})`,
        );
      }

      confirmRemove.value = false;
      notice.value =
        "Your Avanza details are removed. Zenith keeps the values it already fetched.";
      await load();
    } catch (err) {
      formError.value =
        err instanceof Error ? err.message : "Could not remove your details.";
    } finally {
      busy.value = false;
    }
  });

  useVisibleTask$(
    () => {
      void load();
    },
    { strategy: "document-ready" },
  );

  const kr = (amount: number) =>
    hidden.value ? maskedAmount : formatKr(amount);
  const data = balances.value;
  const current = status.value;
  const totals = data ? totalsByTime(data) : [];
  const total =
    data?.accounts.reduce((sum, account) => sum + account.totalValue, 0) ?? 0;
  const previousTotal = totals.length > 1 ? totals[totals.length - 2] : null;
  const firstTotal = totals.length > 1 ? totals[0] : null;
  const previousByAccount = new Map<string, number>();

  if (data && totals.length > 1) {
    for (const point of data.history) {
      if (point.fetchedAt === totals[totals.length - 2][0]) {
        previousByAccount.set(point.accountId, point.totalValue);
      }
    }
  }

  const change = (now: number, before: number) => {
    const difference = now - before;
    const percent = before !== 0 ? (difference / Math.abs(before)) * 100 : 0;
    const sign = difference > 0 ? "+" : difference < 0 ? "−" : "±";

    return {
      text: `${sign}${kr(Math.abs(difference))} kr (${sign}${new Intl.NumberFormat(
        "sv-SE",
        {
          maximumFractionDigits: 1,
        },
      ).format(Math.abs(percent))} %)`,
      tone: difference > 0 ? "up" : difference < 0 ? "down" : "flat",
    };
  };
  const showForm =
    current?.configured === true && (!current.exists || replacing.value);
  const state = current ? statusText(current) : null;

  return (
    <main id="main" class="page-shell cal-shell avz-shell">
      <section class="page-heading" aria-labelledby="page-title">
        <div>
          <h1 id="page-title">
            Avanza<span>.</span>
          </h1>
          <p class="page-subtitle">
            The value of your Avanza accounts, fetched on Monday and Thursday
            mornings.
          </p>
        </div>

        <div class="cal-actions">
          <button
            type="button"
            class="visibility-button"
            aria-pressed={hidden.value}
            onClick$={() => {
              hidden.value = !hidden.value;
            }}
          >
            <Icon name={hidden.value ? "eyeoff" : "eye"} />
            {hidden.value ? "Show amounts" : "Hide amounts"}
          </button>
        </div>
      </section>

      {notice.value && (
        <div class="cal-alert avz-notice" role="status">
          <Icon name="check" />
          <div>
            <strong>{notice.value}</strong>
          </div>
        </div>
      )}

      {error.value && (
        <div class="cal-alert" role="alert">
          <Icon name="info" />
          <div>
            <strong>{error.value}</strong>
            <button type="button" class="cal-chip avz-retry" onClick$={load}>
              Try again
            </button>
          </div>
        </div>
      )}

      {loading.value && !current && (
        <section class="overview-panel avz-panel">
          <p class="avz-hint">Loading your Avanza values…</p>
        </section>
      )}

      {current && !current.configured && (
        <section class="overview-panel avz-panel">
          <h2>Avanza is not set up on this server yet</h2>
          <p class="avz-hint">
            Run <code>sudo ./deploy/avanza-sync/setup.sh</code> on the server
            that runs Zenith, then come back here to enter your details.
          </p>
        </section>
      )}

      {data && data.accounts.length > 0 && (
        <>
          <section class="overview-panel avz-panel" aria-labelledby="avz-total">
            <dl class="avz-stats">
              <div>
                <dt id="avz-total">Total value</dt>
                <dd class="avz-hero">{`${kr(total)} kr`}</dd>
                <small>{`Fetched ${formatTime(data.fetchedAt)}`}</small>
              </div>
              {previousTotal && (
                <div>
                  <dt>Since the fetch before</dt>
                  <dd
                    class={`avz-change-${change(total, previousTotal[1]).tone}`}
                  >
                    {change(total, previousTotal[1]).text}
                  </dd>
                  <small>{`Compared with ${formatTime(previousTotal[0])}`}</small>
                </div>
              )}
              {firstTotal && totals.length > 2 && (
                <div>
                  <dt>Since the first fetch</dt>
                  <dd class={`avz-change-${change(total, firstTotal[1]).tone}`}>
                    {change(total, firstTotal[1]).text}
                  </dd>
                  <small>{`Compared with ${formatTime(firstTotal[0])}`}</small>
                </div>
              )}
            </dl>

            <AvanzaChart
              history={data.history}
              accounts={data.accounts}
              hidden={hidden.value}
            />
          </section>

          <section class="accounts-section" aria-labelledby="avz-accounts">
            <div class="avz-head">
              <h2 id="avz-accounts">Your Avanza accounts</h2>
              <span class="soft-badge">
                {`${data.accounts.length} ${data.accounts.length === 1 ? "account" : "accounts"}`}
              </span>
            </div>

            <div class="avz-cards">
              {data.accounts.map((account) => {
                const before = previousByAccount.get(account.accountId);
                const moved =
                  before === undefined
                    ? null
                    : change(account.totalValue, before);

                return (
                  <article key={account.accountId} class="avz-card">
                    <span class="avz-card-type">
                      {avanzaTypeLabel(account.accountType)}
                    </span>
                    <h3>{account.accountName}</h3>
                    <strong class="avz-card-value">
                      {`${kr(account.totalValue)} ${account.currency === "SEK" ? "kr" : account.currency}`}
                    </strong>
                    {moved && (
                      <span class={`avz-card-change avz-change-${moved.tone}`}>
                        {`${moved.text} since the fetch before`}
                      </span>
                    )}
                    <small>{`Fetched ${formatTime(account.fetchedAt)}`}</small>
                  </article>
                );
              })}
            </div>
          </section>
        </>
      )}

      {current?.configured &&
        current.exists &&
        data &&
        data.accounts.length === 0 && (
          <section class="overview-panel avz-panel">
            <p class="avz-hint">
              {`No values yet. The next fetch is planned for ${nextFetch(new Date())} around 07:00.`}
            </p>
          </section>
        )}

      {current?.configured && (
        <section
          class="overview-panel avz-panel"
          aria-labelledby="avz-settings"
        >
          <div class="avz-head">
            <h2 id="avz-settings">Avanza login</h2>
            {state && current.exists && !replacing.value && (
              <span class={`avz-state avz-state-${state.tone}`}>
                {state.label}
              </span>
            )}
          </div>

          {formError.value && (
            <p class="avz-error" role="alert">
              {formError.value}
            </p>
          )}

          {current.exists && !replacing.value && state && (
            <>
              <dl class="avz-status">
                <div>
                  <dt>Details saved</dt>
                  <dd>{formatTime(current.updatedAt)}</dd>
                </div>
                <div>
                  <dt>Last login attempt</dt>
                  <dd>{formatTime(current.lastAttemptAt)}</dd>
                </div>
                <div>
                  <dt>Last successful fetch</dt>
                  <dd>{formatTime(current.lastSuccessAt)}</dd>
                </div>
                <div>
                  <dt>Next fetch</dt>
                  <dd>{`${nextFetch(new Date())}, around 07:00`}</dd>
                </div>
              </dl>
              <p class="avz-hint">{state.detail}</p>
              <p class="avz-hint">
                Your username, password and TOTP secret are stored encrypted and
                can never be shown again, only replaced or removed.
              </p>

              {confirmRemove.value ? (
                <div
                  class="avz-actions"
                  role="group"
                  aria-label="Confirm removal"
                >
                  <span class="avz-hint">
                    Remove your Avanza details? The values already fetched stay.
                  </span>
                  <button
                    type="button"
                    class="cal-chip avz-danger"
                    disabled={busy.value}
                    onClick$={remove}
                  >
                    {busy.value ? "Removing…" : "Remove"}
                  </button>
                  <button
                    type="button"
                    class="cal-chip"
                    onClick$={() => {
                      confirmRemove.value = false;
                    }}
                  >
                    Cancel
                  </button>
                </div>
              ) : (
                <div class="avz-actions">
                  <button
                    type="button"
                    class="cal-chip"
                    onClick$={() => {
                      replacing.value = true;
                      formError.value = null;
                    }}
                  >
                    Replace
                  </button>
                  <button
                    type="button"
                    class="cal-chip"
                    onClick$={() => {
                      confirmRemove.value = true;
                    }}
                  >
                    Remove
                  </button>
                </div>
              )}
            </>
          )}

          {showForm && (
            <form
              ref={form}
              class="avz-form"
              method="post"
              autocomplete="off"
              preventdefault:submit
              onSubmit$={save}
            >
              <p class="avz-hint">
                Your details go over HTTPS to Zenith's API, which encrypts them
                with a key that only the fetcher on the server can open. They
                are never shown again.
              </p>
              <label class="avz-field">
                <span class="avz-label">Username</span>
                <input
                  ref={usernameInput}
                  class="avz-input"
                  type="password"
                  autocomplete="off"
                  autocapitalize="off"
                  spellcheck={false}
                  required
                  maxLength={100}
                />
              </label>
              <label class="avz-field">
                <span class="avz-label">Password</span>
                <input
                  ref={passwordInput}
                  class="avz-input"
                  type="password"
                  autocomplete="off"
                  required
                  maxLength={200}
                />
              </label>
              <label class="avz-field">
                <span class="avz-label">TOTP secret</span>
                <input
                  ref={totpInput}
                  class="avz-input"
                  type="password"
                  autocomplete="off"
                  autocapitalize="off"
                  spellcheck={false}
                  required
                  maxLength={160}
                />
                <small class="avz-hint">
                  The text key Avanza shows next to the QR code when you add an
                  authenticator app.
                </small>
              </label>
              <div class="avz-actions">
                <button
                  type="submit"
                  class="cal-chip avz-primary"
                  disabled={busy.value}
                >
                  {busy.value ? "Saving…" : "Save"}
                </button>
                {current.exists && (
                  <button
                    type="button"
                    class="cal-chip"
                    onClick$={() => {
                      replacing.value = false;
                      formError.value = null;
                    }}
                  >
                    Cancel
                  </button>
                )}
              </div>
            </form>
          )}
        </section>
      )}
    </main>
  );
});
