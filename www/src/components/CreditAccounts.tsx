import {
  $,
  component$,
  useSignal,
  useStore,
  useVisibleTask$,
} from "@builder.io/qwik";

import { Icon } from "./Icon";
import {
  creditModeChoices,
  creditModeLabel,
  creditUsed,
  currentMonthRange,
  formatKr,
  type CreditAccountRule,
  type MonthTransaction,
} from "../utils/credit";

export interface CreditAccountOption {
  id: number;
  name: string;
  bank: string;
  iban?: string | null;
  currency: string;
  color: string;
  inConsent: boolean;
}

interface CreditAccountsResponse {
  creditAccounts: CreditAccountRule[];
}

interface Editor {
  open: boolean;
  id: number | null;
  name: string;
  accountId: string;
  mode: string;
  creditLimit: string;
  saving: boolean;
  error: string | null;
}

const maskedAmount = "•• •••,••";

function accountLabel(account: CreditAccountOption | undefined): string {
  if (!account) {
    return "an account that is no longer connected";
  }

  const iban = account.iban?.replace(/\s+/g, "");

  return `${account.name}${iban ? ` ··${iban.slice(-4)}` : ""} · ${account.bank}`;
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
  if (typeof payload === "string") {
    return payload;
  }

  if (payload && typeof payload === "object" && "message" in payload) {
    const message = (payload as { message?: unknown }).message;

    if (typeof message === "string") {
      return message;
    }
  }

  return null;
}

function isCreditAccountsResponse(
  value: unknown,
): value is CreditAccountsResponse {
  return (
    !!value &&
    typeof value === "object" &&
    Array.isArray((value as CreditAccountsResponse).creditAccounts)
  );
}

interface CreditAccountsProps {
  accounts: CreditAccountOption[];
  hidden: boolean;
  monthTransactions: MonthTransaction[] | null;
  monthError: boolean;
  syncing: boolean;
}

export const CreditAccounts = component$<CreditAccountsProps>((props) => {
  const data = useSignal<CreditAccountRule[] | null>(null);
  const loading = useSignal(true);
  const error = useSignal<string | null>(null);
  const confirmingDelete = useSignal<number | null>(null);
  const deleting = useSignal<number | null>(null);

  const editor = useStore<Editor>({
    open: false,
    id: null,
    name: "",
    accountId: "",
    mode: "MIRROR",
    creditLimit: "",
    saving: false,
    error: null,
  });

  const load = $(async () => {
    loading.value = true;
    error.value = null;

    try {
      const response = await fetch("/api/credit-accounts", {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ??
            `Failed to fetch credit accounts (${response.status})`,
        );
      }

      if (!isCreditAccountsResponse(payload)) {
        throw new Error("Unexpected response from the credit account API.");
      }

      data.value = payload.creditAccounts;
    } catch (err) {
      error.value =
        err instanceof Error ? err.message : "Could not load credit accounts.";
    } finally {
      loading.value = false;
    }
  });

  const openEditor = $((creditAccount: CreditAccountRule | null) => {
    editor.open = true;
    editor.id = creditAccount?.id ?? null;
    editor.name = creditAccount?.name ?? "";
    editor.accountId = creditAccount ? String(creditAccount.accountId) : "";
    editor.mode = creditAccount?.mode ?? "MIRROR";
    editor.creditLimit =
      creditAccount?.creditLimit != null
        ? String(creditAccount.creditLimit)
        : "";
    editor.error = null;
    confirmingDelete.value = null;
  });

  const save = $(async () => {
    if (editor.saving) {
      return;
    }

    const limitText = editor.creditLimit
      .trim()
      .replace(/\s+/g, "")
      .replace(",", ".");
    const creditLimit = limitText === "" ? null : Number(limitText);

    if (
      creditLimit !== null &&
      (!Number.isFinite(creditLimit) || creditLimit < 0)
    ) {
      editor.error = "The credit limit must be a number of kronor, 0 or more";
      return;
    }

    editor.saving = true;
    editor.error = null;

    try {
      const response = await fetch(
        editor.id === null
          ? "/api/credit-accounts"
          : `/api/credit-accounts/${editor.id}`,
        {
          method: editor.id === null ? "POST" : "PUT",
          headers: {
            Accept: "application/json",
            "Content-Type": "application/json",
          },
          credentials: "same-origin",
          body: JSON.stringify({
            name: editor.name,
            accountId: editor.accountId ? Number(editor.accountId) : null,
            mode: editor.mode,
            creditLimit,
          }),
        },
      );

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ??
            `Could not save the credit account (${response.status})`,
        );
      }

      editor.open = false;
      await load();
    } catch (err) {
      editor.error =
        err instanceof Error
          ? err.message
          : "Could not save the credit account.";
    } finally {
      editor.saving = false;
    }
  });

  const remove = $(async (id: number) => {
    deleting.value = id;
    error.value = null;

    try {
      const response = await fetch(`/api/credit-accounts/${id}`, {
        method: "DELETE",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
      });

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ??
            `Could not delete the credit account (${response.status})`,
        );
      }

      confirmingDelete.value = null;

      if (editor.id === id) {
        editor.open = false;
      }

      await load();
    } catch (err) {
      error.value =
        err instanceof Error
          ? err.message
          : "Could not delete the credit account.";
    } finally {
      deleting.value = null;
    }
  });

  useVisibleTask$(
    () => {
      void load();
    },
    {
      strategy: "document-ready",
    },
  );

  const month = currentMonthRange();
  const creditAccounts = data.value ?? [];
  const accountsById: Record<number, CreditAccountOption> = Object.fromEntries(
    props.accounts.map((account) => [account.id, account]),
  );
  const linkedElsewhere = creditAccounts
    .filter((creditAccount) => creditAccount.id !== editor.id)
    .map((creditAccount) => creditAccount.accountId);
  const selectedMode = creditModeChoices.find(
    (choice) => choice.value === editor.mode,
  );
  const kr = (minor: number) => (props.hidden ? maskedAmount : formatKr(minor));

  return (
    <section
      class="accounts-section cal-shell credit-section"
      aria-labelledby="credit-title"
    >
      <div class="section-heading">
        <div>
          <h2 id="credit-title">
            Credit accounts{" "}
            <span>{String(creditAccounts.length).padStart(2, "0")}</span>
          </h2>
          <p>
            Link the account where the card shows up. Card purchases count
            toward it and its limit; paying the card bill doesn't count a second
            time.
          </p>
          <p class="credit-sync" aria-live="polite">
            {props.syncing ? "Checking your banks for new transactions…" : ""}
          </p>
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
          <form
            preventdefault:submit
            onSubmit$={save}
            aria-labelledby="credit-editor-title"
          >
            <div class="cat-editor-head">
              <div>
                <h2 id="credit-editor-title">
                  {editor.id === null
                    ? "New credit account"
                    : "Edit credit account"}
                </h2>
                <p>
                  {editor.name.trim() || "Untitled"} ·{" "}
                  {creditModeLabel(editor.mode)}
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
                  <option value="" selected={editor.accountId === ""}>
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
                          ? " (already a credit account)"
                          : account.inConsent
                            ? ""
                            : " (not shared by the bank)"
                      }`}
                    </option>
                  ))}
                </select>
                <span id="credit-account-hint" class="cat-hint">
                  Every transaction on this account counts toward the credit
                  account.
                </span>
              </div>

              <fieldset class="cat-field cat-field-wide">
                <legend class="cat-label">
                  How the card shows up on the linked account
                </legend>
                <div class="cat-kind-row">
                  {creditModeChoices.map((choice) => (
                    <label
                      key={choice.value}
                      class={{
                        "cat-kind-option": true,
                        "cat-option-selected": editor.mode === choice.value,
                      }}
                    >
                      <input
                        type="radio"
                        name="credit-mode"
                        value={choice.value}
                        checked={editor.mode === choice.value}
                        onChange$={() => {
                          editor.mode = choice.value;
                        }}
                      />
                      {choice.label}
                    </label>
                  ))}
                </div>
                {selectedMode && <p class="cat-hint">{selectedMode.hint}</p>}
              </fieldset>

              <div class="cat-field">
                <label for="credit-limit" class="cat-label">
                  Credit limit in kr
                </label>
                <input
                  id="credit-limit"
                  class="cat-input"
                  type="text"
                  inputMode="decimal"
                  placeholder="For example 10 000"
                  aria-describedby="credit-limit-hint"
                  value={editor.creditLimit}
                  onInput$={(_, element) => {
                    editor.creditLimit = element.value;
                  }}
                />
                <span id="credit-limit-hint" class="cat-hint">
                  Optional, only for you to keep track. Going over is fine.
                </span>
              </div>
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
              <button
                type="submit"
                class="cat-primary"
                disabled={editor.saving}
              >
                {editor.saving
                  ? "Saving…"
                  : editor.id === null
                    ? "Create credit account"
                    : "Save changes"}
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
              No credit accounts yet. Create one, link it to the account your
              credit runs through, and choose how its transactions count.
            </p>
          </div>
        )
      ) : (
        <div class="accounts-grid">
          {creditAccounts.map((creditAccount) => {
            const account = accountsById[creditAccount.accountId];
            const used = props.monthTransactions
              ? creditUsed(
                  props.monthTransactions.filter(
                    (transaction) =>
                      transaction.accountId === creditAccount.accountId &&
                      transaction.currency === (account?.currency ?? "SEK"),
                  ),
                )
              : null;
            const limit =
              creditAccount.creditLimit != null
                ? Math.round(creditAccount.creditLimit * 100)
                : null;
            const over = used !== null && limit !== null && used > limit;

            return (
              <article
                key={creditAccount.id}
                class="account-card credit-card"
                style={{ "--account-color": account?.color ?? "#8792a5" }}
              >
                <div class="card-top">
                  <span class="account-icon">
                    <Icon name="wallet" />
                  </span>
                  <span class="card-bank">Credit account</span>
                  <span class="credit-counting">
                    {creditModeLabel(creditAccount.mode)}
                  </span>
                </div>

                <h3>{creditAccount.name}</h3>

                <p class="account-purpose" title={accountLabel(account)}>
                  {`Linked to ${accountLabel(account)}`}
                </p>

                {account && !account.inConsent && (
                  <p class="credit-note credit-stale" role="note">
                    <Icon name="info" />
                    {`${account.bank} no longer shares this account with Zenith, so new transactions aren't counted. Reconnect ${account.bank} and tick the account ending ${(account.iban ?? "").replace(/\s+/g, "").slice(-4)}.`}
                  </p>
                )}

                {used === null ? (
                  <p class="credit-note">
                    {props.monthError
                      ? `Could not load ${month.name}'s transactions.`
                      : `Adding up ${month.name}…`}
                  </p>
                ) : limit === null ? (
                  <>
                    <div class="card-balance">
                      {kr(used)} <span>kr</span>
                    </div>
                    <p class="credit-note">
                      Used in {month.name}. Add a credit limit to see what is
                      left.
                    </p>
                  </>
                ) : (
                  <>
                    <div class="card-balance">
                      {kr(Math.abs(limit - used))} <span>kr</span>
                    </div>
                    <p class={{ "credit-note": true, "credit-over": over }}>
                      {over && <Icon name="info" />}
                      {over ? "over the credit limit" : "left to use"}
                    </p>
                    <div
                      class={{
                        "credit-meter": true,
                        "credit-meter-over": over,
                      }}
                      aria-hidden="true"
                    >
                      <span
                        style={{
                          width: `${
                            limit > 0
                              ? Math.min(100, (used / limit) * 100)
                              : 100
                          }%`,
                        }}
                      />
                    </div>
                    <div class="card-footer">
                      <span>Used in {month.name}</span>
                      <strong>
                        {kr(used)} of {kr(limit)} kr
                      </strong>
                    </div>
                  </>
                )}

                {confirmingDelete.value === creditAccount.id ? (
                  <div
                    class="credit-actions"
                    role="group"
                    aria-label="Confirm delete"
                  >
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
                      {deleting.value === creditAccount.id
                        ? "Deleting…"
                        : "Delete"}
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
