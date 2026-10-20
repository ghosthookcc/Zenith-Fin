import { $, component$, useSignal, useStore, type QRL } from "@builder.io/qwik";

import { Icon } from "./Icon";
import {
  sharedWithLabel,
  sharesByTarget,
  virtualShareKey,
  type AccountShare,
} from "./SharedAccounts";
import {
  countedAmount,
  countingChoices,
  countingLabel,
  currentMonthRange,
  formatKr,
  matchesVirtualAccount,
  type MonthTransaction,
  uncountedStatuses,
  type VirtualAccountRule,
} from "../utils/credit";

interface Editor {
  open: boolean;
  id: number | null;
  name: string;
  accountNumber: string;
  clearingNumber: string;
  iban: string;
  counting: string;
  balance: string;
  originalBalance: string;
  saving: boolean;
  error: string | null;
}

interface VirtualAccountsProps {
  virtualAccounts: VirtualAccountRule[] | null;
  error: string | null;
  shares: AccountShare[] | null;
  hidden: boolean;
  monthTransactions: MonthTransaction[] | null;
  monthError: boolean;
  onChanged$: QRL<() => Promise<void>>;
}

const maskedAmount = "•• •••,••";

function numberLabel(account: VirtualAccountRule): string {
  const parts = [`Account ${account.accountNumber}`];

  if (account.clearingNumber) {
    parts.push(`clearing ${account.clearingNumber}`);
  }

  if (account.iban) {
    parts.push(`IBAN ${account.iban.replace(/(.{4})/g, "$1 ").trim()}`);
  }

  return parts.join(" · ");
}

function monthSummary(
  transactions: MonthTransaction[],
  account: VirtualAccountRule,
) {
  let counted = 0;
  let moved = 0;
  let count = 0;

  for (const transaction of transactions) {
    if (
      uncountedStatuses.includes(transaction.status) ||
      (transaction.virtualAccountId !== undefined
        ? transaction.virtualAccountId !== account.id
        : !matchesVirtualAccount(transaction.counterpartyAccount, account))
    ) {
      continue;
    }

    count += 1;
    moved += Math.round(Math.abs(transaction.amount) * 100);
    counted += Math.round(
      (countedAmount(
        transaction.amount,
        transaction.counting ?? account.counting,
      ) ?? 0) * 100,
    );
  }

  return { count, counted, moved };
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

function balanceInput(amount: number | null | undefined): string {
  if (amount === null || amount === undefined) {
    return "";
  }

  return Number.isInteger(amount)
    ? String(amount)
    : amount.toFixed(2).replace(".", ",");
}

function parseBalance(text: string): number | null | undefined {
  const cleaned = text
    .replace(/[\s\u00a0]/g, "")
    .replace(/kr$/i, "")
    .replace(",", ".");

  if (cleaned === "") {
    return null;
  }

  const amount = Number(cleaned);

  return Number.isFinite(amount) ? amount : undefined;
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

export const VirtualAccounts = component$<VirtualAccountsProps>((props) => {
  const actionError = useSignal<string | null>(null);
  const confirmingDelete = useSignal<number | null>(null);
  const deleting = useSignal<number | null>(null);

  const editor = useStore<Editor>({
    open: false,
    id: null,
    name: "",
    accountNumber: "",
    clearingNumber: "",
    iban: "",
    counting: "NEUTRAL",
    balance: "",
    originalBalance: "",
    saving: false,
    error: null,
  });

  const openEditor = $((account: VirtualAccountRule | null) => {
    editor.open = true;
    editor.id = account?.id ?? null;
    editor.name = account?.name ?? "";
    editor.accountNumber = account?.accountNumber ?? "";
    editor.clearingNumber = account?.clearingNumber ?? "";
    editor.iban = account?.iban ?? "";
    editor.counting = account?.counting ?? "NEUTRAL";
    editor.balance = balanceInput(account?.currentBalance);
    editor.originalBalance = editor.balance;
    editor.error = null;
    confirmingDelete.value = null;
  });

  const save = $(async () => {
    if (editor.saving) {
      return;
    }

    const balance = parseBalance(editor.balance);

    if (balance === undefined) {
      editor.error =
        "Enter the balance as a number, for example 25 000 or 1 250,50.";
      return;
    }

    editor.saving = true;
    editor.error = null;

    try {
      const response = await fetch(
        editor.id === null
          ? "/api/virtual-accounts"
          : `/api/virtual-accounts/${editor.id}`,
        {
          method: editor.id === null ? "POST" : "PUT",
          headers: {
            Accept: "application/json",
            "Content-Type": "application/json",
          },
          credentials: "same-origin",
          body: JSON.stringify({
            name: editor.name,
            accountNumber: editor.accountNumber,
            clearingNumber: editor.clearingNumber || null,
            iban: editor.iban || null,
            counting: editor.counting,
            balance,
            setBalance: editor.balance.trim() !== editor.originalBalance.trim(),
          }),
        },
      );

      const payload = await readJson(response);

      if (!response.ok) {
        throw new Error(
          messageFrom(payload) ??
            `Could not save the virtual account (${response.status})`,
        );
      }

      editor.open = false;
      await props.onChanged$();
    } catch (err) {
      editor.error =
        err instanceof Error
          ? err.message
          : "Could not save the virtual account.";
    } finally {
      editor.saving = false;
    }
  });

  const remove = $(async (id: number) => {
    deleting.value = id;
    actionError.value = null;

    try {
      const response = await fetch(`/api/virtual-accounts/${id}`, {
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
            `Could not delete the virtual account (${response.status})`,
        );
      }

      confirmingDelete.value = null;

      if (editor.id === id) {
        editor.open = false;
      }

      await props.onChanged$();
    } catch (err) {
      actionError.value =
        err instanceof Error
          ? err.message
          : "Could not delete the virtual account.";
    } finally {
      deleting.value = null;
    }
  });

  const month = currentMonthRange();
  const virtualAccounts = props.virtualAccounts ?? [];
  const sharedByTarget = sharesByTarget(props.shares ?? []);
  const alert = actionError.value ?? props.error;
  const selectedCounting = countingChoices.find(
    (choice) => choice.value === editor.counting,
  );
  const kr = (minor: number) => (props.hidden ? maskedAmount : formatKr(minor));

  return (
    <section
      class="accounts-section cal-shell credit-section"
      aria-labelledby="virtual-title"
    >
      <div class="section-heading">
        <div>
          <h2 id="virtual-title">
            Virtual accounts{" "}
            <span>{String(virtualAccounts.length).padStart(2, "0")}</span>
          </h2>
          <p>
            Accounts Zenith can't connect to. Money sent to or from them counts
            the way you choose.
          </p>
        </div>

        {!editor.open && (
          <button
            type="button"
            class="cat-primary credit-new"
            disabled={!props.virtualAccounts}
            onClick$={() => openEditor(null)}
          >
            New virtual account
          </button>
        )}
      </div>

      {alert && (
        <div class="cal-alert" role="alert">
          <Icon name="info" />
          <div>
            <strong>{alert}</strong>
            {!actionError.value && (
              <button
                type="button"
                class="cal-chip cat-retry"
                onClick$={() => props.onChanged$()}
              >
                Try again
              </button>
            )}
          </div>
        </div>
      )}

      {editor.open && (
        <div class="overview-panel cat-editor">
          <form
            preventdefault:submit
            onSubmit$={save}
            aria-labelledby="virtual-editor-title"
          >
            <div class="cat-editor-head">
              <div>
                <h2 id="virtual-editor-title">
                  {editor.id === null
                    ? "New virtual account"
                    : "Edit virtual account"}
                </h2>
                <p>
                  {editor.name.trim() || "Untitled"} ·{" "}
                  {countingLabel(editor.counting)}
                </p>
              </div>
            </div>

            <div class="cat-fields">
              <div class="cat-field">
                <label for="virtual-name" class="cat-label">
                  Account name
                </label>
                <input
                  id="virtual-name"
                  class="cat-input"
                  type="text"
                  maxLength={40}
                  required
                  placeholder="For example Joint savings"
                  value={editor.name}
                  onInput$={(_, element) => {
                    editor.name = element.value;
                  }}
                />
              </div>

              <div class="cat-field">
                <label for="virtual-number" class="cat-label">
                  Account number
                </label>
                <input
                  id="virtual-number"
                  class="cat-input"
                  type="text"
                  inputMode="numeric"
                  required
                  placeholder="For example 0197868"
                  aria-describedby="virtual-number-hint"
                  value={editor.accountNumber}
                  onInput$={(_, element) => {
                    editor.accountNumber = element.value;
                  }}
                />
                <span id="virtual-number-hint" class="cat-hint">
                  Transactions to or from this number land in the account.
                </span>
              </div>

              <div class="cat-field">
                <label for="virtual-clearing" class="cat-label">
                  Clearing number <span class="cat-optional">(optional)</span>
                </label>
                <input
                  id="virtual-clearing"
                  class="cat-input"
                  type="text"
                  inputMode="numeric"
                  placeholder="For example 3307"
                  value={editor.clearingNumber}
                  onInput$={(_, element) => {
                    editor.clearingNumber = element.value;
                  }}
                />
              </div>

              <div class="cat-field">
                <label for="virtual-iban" class="cat-label">
                  IBAN <span class="cat-optional">(optional)</span>
                </label>
                <input
                  id="virtual-iban"
                  class="cat-input"
                  type="text"
                  placeholder="For example SE45 5000 0000 0583 9825 7466"
                  value={editor.iban}
                  onInput$={(_, element) => {
                    editor.iban = element.value;
                  }}
                />
              </div>

              <div class="cat-field">
                <label for="virtual-balance" class="cat-label">
                  Current balance <span class="cat-optional">(optional)</span>
                </label>
                <input
                  id="virtual-balance"
                  class="cat-input"
                  type="text"
                  inputMode="decimal"
                  placeholder="For example 25 000"
                  aria-describedby="virtual-balance-hint"
                  value={editor.balance}
                  onInput$={(_, element) => {
                    editor.balance = element.value;
                  }}
                />
                <span id="virtual-balance-hint" class="cat-hint">
                  What the account holds right now. Transfers to and from it
                  then move the balance. Leave it empty to stop tracking.
                </span>
              </div>

              <fieldset class="cat-field cat-field-wide">
                <legend class="cat-label">Counts as</legend>
                <div class="cat-kind-row">
                  {countingChoices.map((choice) => (
                    <label
                      key={choice.value}
                      class={{
                        "cat-kind-option": true,
                        "cat-option-selected": editor.counting === choice.value,
                      }}
                    >
                      <input
                        type="radio"
                        name="virtual-counting"
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
                {selectedCounting && (
                  <p class="cat-hint">
                    {selectedCounting.hint.replace(
                      "on the linked account",
                      "with this account",
                    )}
                  </p>
                )}
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
              <button
                type="submit"
                class="cat-primary"
                disabled={editor.saving}
              >
                {editor.saving
                  ? "Saving…"
                  : editor.id === null
                    ? "Create virtual account"
                    : "Save changes"}
              </button>
            </div>
          </form>
        </div>
      )}

      {!props.virtualAccounts ? (
        !props.error && (
          <p class="cal-empty credit-empty">Loading virtual accounts…</p>
        )
      ) : virtualAccounts.length === 0 ? (
        !editor.open && (
          <div class="overview-panel">
            <p class="cal-empty">
              No virtual accounts yet. Add one for an account Zenith can't
              connect to, such as a partner's account or a savings account at
              another bank, and choose how money to and from it counts.
            </p>
          </div>
        )
      ) : (
        <div class="accounts-grid">
          {virtualAccounts.map((account) => {
            const summary = props.monthTransactions
              ? monthSummary(props.monthTransactions, account)
              : null;
            const people =
              sharedByTarget.get(virtualShareKey(account.id)) ?? [];

            return (
              <article
                key={account.id}
                class="account-card credit-card"
                style={{ "--account-color": "#8792a5" }}
              >
                <div class="card-top">
                  <span class="account-icon">
                    <Icon name="bank" />
                  </span>
                  <span class="card-bank">Virtual account</span>
                  <span class="credit-counting">
                    {countingLabel(account.counting)}
                  </span>
                </div>

                <h3>{account.name}</h3>

                <p class="account-purpose" title={numberLabel(account)}>
                  {numberLabel(account)}
                </p>

                {people.length > 0 && (
                  <p class="account-shared" title={sharedWithLabel(people)}>
                    <Icon name="users" />
                    <span>{sharedWithLabel(people)}</span>
                  </p>
                )}

                {summary === null ? (
                  <p class="credit-note">
                    {props.monthError
                      ? `Could not load ${month.name}'s transactions.`
                      : `Adding up ${month.name}…`}
                  </p>
                ) : account.currentBalance != null ? (
                  <>
                    <div class="card-balance">
                      {kr(Math.round(account.currentBalance * 100))}{" "}
                      <span>kr</span>
                    </div>
                    <p class="credit-note">
                      {`Balance · ${kr(account.counting === "NEUTRAL" ? summary.moved : Math.abs(summary.counted))} kr ${
                        account.counting === "NEUTRAL"
                          ? `moved in ${month.name}`
                          : account.counting === "FLOW"
                            ? `${summary.counted < 0 ? "net out" : "net in"} in ${month.name}`
                            : `counted as money ${account.counting === "OUT" ? "out" : "in"} in ${month.name}`
                      }`}
                    </p>
                    <div class="card-footer">
                      <span>This month</span>
                      <strong>
                        {`${summary.count} ${summary.count === 1 ? "transaction" : "transactions"}`}
                      </strong>
                    </div>
                  </>
                ) : (
                  <>
                    <div class="card-balance">
                      {kr(
                        account.counting === "NEUTRAL"
                          ? summary.moved
                          : Math.abs(summary.counted),
                      )}{" "}
                      <span>kr</span>
                    </div>
                    <p class="credit-note">
                      {account.counting === "NEUTRAL"
                        ? `moved in ${month.name}, left out of your totals`
                        : account.counting === "FLOW"
                          ? `${summary.counted < 0 ? "net out" : "net in"} in ${month.name}`
                          : `counted as money ${account.counting === "OUT" ? "out" : "in"} in ${month.name}`}
                    </p>
                    <div class="card-footer">
                      <span>This month</span>
                      <strong>
                        {`${summary.count} ${summary.count === 1 ? "transaction" : "transactions"}`}
                      </strong>
                    </div>
                  </>
                )}

                {confirmingDelete.value === account.id ? (
                  <div
                    class="credit-actions"
                    role="group"
                    aria-label="Confirm delete"
                  >
                    <span class="cat-confirm">
                      {people.length > 0
                        ? "Its transactions count normally again, and the people it's shared with are removed."
                        : "Its transactions count normally again."}
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
                      disabled={deleting.value === account.id}
                      onClick$={() => remove(account.id)}
                    >
                      {deleting.value === account.id ? "Deleting…" : "Delete"}
                    </button>
                  </div>
                ) : (
                  <div class="credit-actions">
                    <button
                      type="button"
                      class="cal-chip"
                      onClick$={() => openEditor(account)}
                    >
                      Edit
                    </button>
                    <button
                      type="button"
                      class="cal-chip"
                      onClick$={() => {
                        confirmingDelete.value = account.id;
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
