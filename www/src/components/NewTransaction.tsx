import {
  $,
  component$,
  useOnWindow,
  useSignal,
  useStore,
  useStyles$,
} from "@builder.io/qwik";

import { Icon } from "./Icon";
import {
  type LinkableAccount,
  manualCountings,
  manualTransactionsChangedEvent,
  openManualTransactionEvent,
} from "../utils/manual";

interface LinkableAccounts {
  accounts: LinkableAccount[];
  virtualAccounts: LinkableAccount[];
}

interface Draft {
  amount: string;
  date: string;
  description: string;
  to: string;
  toNumber: string;
  toName: string;
  from: string;
  fromNumber: string;
  counting: string;
  countingTouched: boolean;
  saving: boolean;
  error: string | null;
}

const otherAccount = "other";
const noAccount = "none";

function localToday(): string {
  const now = new Date();

  return new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()))
    .toISOString()
    .slice(0, 10);
}

function parseAmount(text: string): number | null {
  const normalized = text.replace(/\s/g, "").replace(",", ".");

  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) {
    return null;
  }

  const amount = Number(normalized);

  return amount > 0 ? amount : null;
}

function optionValue(account: LinkableAccount): string {
  return `${account.kind}:${account.id}`;
}

function linkedAccount(
  linkable: LinkableAccounts | null,
  value: string,
): LinkableAccount | null {
  if (!linkable) {
    return null;
  }

  return (
    [...linkable.accounts, ...linkable.virtualAccounts].find(
      (account) => optionValue(account) === value,
    ) ?? null
  );
}

function optionLabel(account: LinkableAccount): string {
  const number = account.accountNumber?.replace(/\s+/g, "");
  const suffix = number ? ` ··${number.slice(-4)}` : "";

  return account.kind === "connected"
    ? `${account.name}${suffix} · ${account.bankName ?? "Bank"}${account.isActive ? "" : " (not synced)"}`
    : `${account.name}${suffix}`;
}

function suggestedCounting(from: string, to: string): string {
  const fromOwn = from !== noAccount;
  const toOwn = to !== otherAccount;

  if (fromOwn && toOwn) {
    return "NEUTRAL";
  }

  return toOwn ? "IN" : "OUT";
}

function countingHint(counting: string): string {
  switch (counting) {
    case "IN":
      return "Counts as money in for the month.";
    case "NEUTRAL":
      return "Left out of the month's money in and out, like a transfer between your own accounts.";
    default:
      return "Counts as money out for the month.";
  }
}

function freshDraft(): Draft {
  return {
    amount: "",
    date: localToday(),
    description: "",
    to: otherAccount,
    toNumber: "",
    toName: "",
    from: noAccount,
    fromNumber: "",
    counting: "OUT",
    countingTouched: false,
    saving: false,
    error: null,
  };
}

async function readMessage(response: Response): Promise<string | null> {
  try {
    const payload = (await response.json()) as { message?: unknown } | null;

    return typeof payload?.message === "string" ? payload.message : null;
  } catch {
    return null;
  }
}

export const NewTransaction = component$(() => {
  useStyles$(`
    .mt-dialog {
      width: min(600px, calc(100vw - 24px));
      max-width: none;
      max-height: calc(100dvh - 24px);
      margin: auto;
      padding: 0;
      border: 1px solid #2b3240;
      border-radius: 20px;
      background: #161b25;
      color: #f1f2f5;
      box-shadow: 0 30px 90px #000000a6;
      font-family: 'DM Sans', ui-sans-serif, system-ui, sans-serif;
      font-size: 15px;
      line-height: 1.5;
      overflow-y: auto;
      overscroll-behavior: contain;
    }
    .mt-dialog, .mt-dialog * { box-sizing: border-box; }
    .mt-dialog[open] { animation: mt-enter 200ms ease-out; }
    .mt-dialog::backdrop {
      background: #05080db3;
      backdrop-filter: blur(3px);
      -webkit-backdrop-filter: blur(3px);
    }
    html:has(.mt-dialog[open]) { overflow: hidden; }
    .mt-form { padding: 24px 26px 22px; }
    .mt-head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 22px;
    }
    .mt-head h2 {
      margin: 0;
      font-family: 'Manrope', sans-serif;
      font-size: 1.25rem;
      font-weight: 750;
      letter-spacing: -0.4px;
    }
    .mt-head p { margin: 4px 0 0; color: #939bad; font-size: 0.8125rem; }
    .mt-close {
      display: grid;
      place-items: center;
      width: 36px;
      height: 36px;
      flex-shrink: 0;
      padding: 0;
      border: 1px solid #2b3240;
      border-radius: 11px;
      background: transparent;
      color: #b6becc;
    }
    .mt-close:hover { background: #1d2330; color: #f1f2f5; }
    .mt-close svg { width: 18px; height: 18px; }
    .mt-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 18px 20px;
    }
    .mt-wide { grid-column: 1 / -1; }
    .mt-field, .mt-side {
      display: flex;
      flex-direction: column;
      gap: 8px;
      min-width: 0;
      margin: 0;
      padding: 0;
      border: 0;
    }
    .mt-side {
      gap: 12px;
      padding: 16px;
      border: 1px solid #262d3a;
      border-radius: 14px;
      background: #131821;
    }
    .mt-side legend {
      float: left;
      width: 100%;
      padding: 0;
    }
    .mt-side legend + * { clear: both; }
    .mt-side-title { display: block; font-weight: 600; font-size: 0.875rem; }
    .mt-side-caption { display: block; color: #939bad; font-size: 0.75rem; }
    .mt-row {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 12px;
    }
    .mt-label { padding: 0; font-size: 0.8125rem; font-weight: 500; color: #ccd1da; }
    .mt-optional { color: #939bad; font-weight: 400; }
    .mt-input {
      width: 100%;
      min-height: 42px;
      padding: 10px 13px;
      border: 1px solid #363e4b;
      border-radius: 9px;
      background: #1c222b;
      color: #f1f2f5;
      font: inherit;
      font-size: 0.875rem;
      color-scheme: dark;
    }
    .mt-input::placeholder { color: #7d8698; }
    .mt-input:focus-visible { outline: 2px solid #c1d8b6; outline-offset: 2px; }
    .mt-input[readonly] {
      border-style: dashed;
      background: #171c25;
      color: #b6becc;
      cursor: default;
    }
    .mt-amount { font-size: 1.125rem; font-weight: 600; font-variant-numeric: tabular-nums; }
    .mt-hint { margin: 0; font-size: 0.75rem; line-height: 1.5; color: #939bad; }
    .mt-choices { display: flex; flex-wrap: wrap; gap: 8px; }
    .mt-choice {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 8px 14px;
      border: 1px solid #323a49;
      border-radius: 999px;
      font-size: 0.875rem;
      color: #ccd1da;
      cursor: pointer;
    }
    .mt-choice input { margin: 0; accent-color: #c1d8b6; }
    .mt-choice:focus-within { outline: 2px solid #c1d8b6; outline-offset: 2px; }
    .mt-choice-selected { border-color: #c1d8b6; color: #f1f2f5; }
    .mt-error {
      margin: 18px 0 0;
      padding: 10px 13px;
      border: 1px solid #6b3434;
      border-radius: 9px;
      background: #2c1d20;
      font-size: 0.8125rem;
      color: #f3c1bd;
    }
    .mt-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 22px; }
    .mt-secondary, .mt-primary {
      padding: 9px 18px;
      border-radius: 999px;
      font-size: 0.875rem;
      font-weight: 600;
    }
    .mt-secondary { border: 1px solid #3a4352; background: transparent; color: #ccd1da; }
    .mt-secondary:hover { border-color: #4a5466; background: #232a37; color: #f1f2f5; }
    .mt-primary { border: 1px solid #c1d8b6; background: #c1d8b6; color: #11151d; }
    .mt-primary:hover:not(:disabled) { border-color: #d3e6c9; background: #d3e6c9; }
    .mt-primary:disabled { opacity: 0.6; }
    .mt-dialog :focus-visible { outline: 2px solid #c1d8b6; outline-offset: 2px; }
    @keyframes mt-enter { from { opacity: 0; transform: translateY(12px); } to { opacity: 1; transform: none; } }
    @media (max-width: 560px) {
      .mt-dialog { width: calc(100vw - 16px); max-height: calc(100dvh - 16px); border-radius: 18px; }
      .mt-form { padding: 20px 16px 18px; }
      .mt-grid, .mt-row { grid-template-columns: minmax(0, 1fr); }
      .mt-actions > * { flex: 1; }
    }
    @media (prefers-reduced-motion: reduce) {
      .mt-dialog[open] { animation: none; }
    }
  `);

  const dialog = useSignal<HTMLDialogElement>();
  const linkable = useSignal<LinkableAccounts | null>(null);
  const linkableError = useSignal<string | null>(null);
  const draft = useStore<Draft>(freshDraft());

  const open = $(async () => {
    Object.assign(draft, freshDraft());

    if (dialog.value && !dialog.value.open) {
      dialog.value.showModal();
    }

    linkableError.value = null;

    try {
      const response = await fetch("/api/manual-transactions/accounts", {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      if (!response.ok) {
        throw new Error(
          (await readMessage(response)) ?? "Could not load your accounts",
        );
      }

      const payload = (await response.json()) as LinkableAccounts;

      linkable.value = {
        accounts: payload.accounts ?? [],
        virtualAccounts: payload.virtualAccounts ?? [],
      };
    } catch (err) {
      linkableError.value =
        err instanceof Error ? err.message : "Could not load your accounts";
    }
  });

  useOnWindow(openManualTransactionEvent, open);

  const chooseTo = $((value: string) => {
    const account = linkedAccount(linkable.value, value);

    draft.to = value;
    draft.toNumber = account?.accountNumber ?? "";
    draft.toName = "";
    draft.error = null;

    if (!draft.countingTouched) {
      draft.counting = suggestedCounting(draft.from, value);
    }
  });

  const chooseFrom = $((value: string) => {
    const account = linkedAccount(linkable.value, value);

    draft.from = value;
    draft.fromNumber = account?.accountNumber ?? "";
    draft.error = null;

    if (!draft.countingTouched) {
      draft.counting = suggestedCounting(value, draft.to);
    }
  });

  const save = $(async () => {
    if (draft.saving) {
      return;
    }

    const amount = parseAmount(draft.amount);

    if (amount === null) {
      draft.error = "Enter the amount, for example 1 250 or 99,50.";
      return;
    }

    const to = linkedAccount(linkable.value, draft.to);
    const from = linkedAccount(linkable.value, draft.from);

    if (!to && !draft.toNumber.trim()) {
      draft.error =
        "Say where the money ended up: pick an account or enter its account number.";
      return;
    }

    draft.saving = true;
    draft.error = null;

    try {
      const response = await fetch("/api/manual-transactions", {
        method: "POST",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
        body: JSON.stringify({
          date: draft.date,
          amount,
          description: draft.description.trim() || null,
          counting: draft.counting,
          fromAccountId: from?.kind === "connected" ? from.id : null,
          fromVirtualAccountId: from?.kind === "virtual" ? from.id : null,
          fromAccountNumber: draft.fromNumber.trim() || null,
          toAccountId: to?.kind === "connected" ? to.id : null,
          toVirtualAccountId: to?.kind === "virtual" ? to.id : null,
          toAccountNumber: draft.toNumber.trim() || null,
          toName: to ? null : draft.toName.trim() || null,
        }),
      });

      if (!response.ok) {
        draft.error =
          (await readMessage(response)) ??
          `Could not add the transaction (${response.status})`;
        return;
      }

      const date = draft.date;

      dialog.value?.close();
      window.dispatchEvent(
        new CustomEvent(manualTransactionsChangedEvent, { detail: { date } }),
      );
    } catch {
      draft.error = "Could not reach Zenith. Try again in a moment.";
    } finally {
      draft.saving = false;
    }
  });

  const toAccount = linkedAccount(linkable.value, draft.to);
  const fromAccount = linkedAccount(linkable.value, draft.from);
  const accounts = linkable.value?.accounts ?? [];
  const virtualAccounts = linkable.value?.virtualAccounts ?? [];

  return (
    <dialog
      ref={dialog}
      class="mt-dialog"
      aria-labelledby="mt-title"
      onClick$={(event, element) => {
        if (event.target !== element) return;
        const rect = element.getBoundingClientRect();
        if (
          event.clientX < rect.left ||
          event.clientX > rect.right ||
          event.clientY < rect.top ||
          event.clientY > rect.bottom
        ) {
          element.close();
        }
      }}
    >
      <form class="mt-form" preventdefault:submit onSubmit$={save}>
        <div class="mt-head">
          <div>
            <h2 id="mt-title">New transaction</h2>
            <p>
              It counts right away. Once your bank shows the same payment,
              Zenith counts the bank's version instead.
            </p>
          </div>
          <button
            type="button"
            class="mt-close"
            aria-label="Close"
            onClick$={() => {
              dialog.value?.close();
            }}
          >
            <Icon name="close" />
          </button>
        </div>

        <div class="mt-grid">
          <div class="mt-field">
            <label for="mt-amount" class="mt-label">
              Amount sent
            </label>
            <input
              id="mt-amount"
              class="mt-input mt-amount"
              type="text"
              inputMode="decimal"
              autoComplete="off"
              autoFocus
              required
              placeholder="0,00"
              value={draft.amount}
              onInput$={(_, element) => {
                draft.amount = element.value;
              }}
            />
          </div>

          <div class="mt-field">
            <label for="mt-date" class="mt-label">
              Date
            </label>
            <input
              id="mt-date"
              class="mt-input"
              type="date"
              required
              value={draft.date}
              onInput$={(_, element) => {
                draft.date = element.value;
              }}
            />
          </div>

          <fieldset class="mt-side mt-wide">
            <legend>
              <span class="mt-side-title">To</span>
              <span class="mt-side-caption">Where the money ended up</span>
            </legend>

            <select
              id="mt-to"
              class="mt-input"
              aria-label="Account the money went to"
              onChange$={(_, element) => chooseTo(element.value)}
            >
              <option value={otherAccount} selected={draft.to === otherAccount}>
                Another account
              </option>
              {accounts.length > 0 && (
                <optgroup label="Your accounts">
                  {accounts.map((account) => (
                    <option
                      key={optionValue(account)}
                      value={optionValue(account)}
                      selected={draft.to === optionValue(account)}
                      disabled={draft.from === optionValue(account)}
                    >
                      {optionLabel(account)}
                    </option>
                  ))}
                </optgroup>
              )}
              {virtualAccounts.length > 0 && (
                <optgroup label="Virtual accounts">
                  {virtualAccounts.map((account) => (
                    <option
                      key={optionValue(account)}
                      value={optionValue(account)}
                      selected={draft.to === optionValue(account)}
                      disabled={draft.from === optionValue(account)}
                    >
                      {optionLabel(account)}
                    </option>
                  ))}
                </optgroup>
              )}
            </select>

            <div class={toAccount ? "mt-field" : "mt-row"}>
              <div class="mt-field">
                <label for="mt-to-number" class="mt-label">
                  Account number
                </label>
                <input
                  id="mt-to-number"
                  class="mt-input"
                  type="text"
                  autoComplete="off"
                  required={!toAccount}
                  readOnly={!!toAccount}
                  placeholder={
                    toAccount ? "No account number" : "Number or IBAN"
                  }
                  aria-describedby="mt-to-hint"
                  value={draft.toNumber}
                  onInput$={(_, element) => {
                    if (!toAccount) {
                      draft.toNumber = element.value;
                    }
                  }}
                />
              </div>

              {!toAccount && (
                <div class="mt-field">
                  <label for="mt-to-name" class="mt-label">
                    Name <span class="mt-optional">(optional)</span>
                  </label>
                  <input
                    id="mt-to-name"
                    class="mt-input"
                    type="text"
                    autoComplete="off"
                    maxLength={60}
                    placeholder="For example Landlord"
                    value={draft.toName}
                    onInput$={(_, element) => {
                      draft.toName = element.value;
                    }}
                  />
                </div>
              )}
            </div>
            <p id="mt-to-hint" class="mt-hint">
              {toAccount
                ? "Filled in from the account you picked, so it can't be changed here."
                : "Pick one of your accounts above, or enter the number the money went to."}
            </p>
          </fieldset>

          <fieldset class="mt-side mt-wide">
            <legend>
              <span class="mt-side-title">From</span>
              <span class="mt-side-caption">
                Where the money came from (optional)
              </span>
            </legend>

            <select
              id="mt-from"
              class="mt-input"
              aria-label="Account the money came from"
              onChange$={(_, element) => chooseFrom(element.value)}
            >
              <option value={noAccount} selected={draft.from === noAccount}>
                Not one of my accounts
              </option>
              {accounts.length > 0 && (
                <optgroup label="Your accounts">
                  {accounts.map((account) => (
                    <option
                      key={optionValue(account)}
                      value={optionValue(account)}
                      selected={draft.from === optionValue(account)}
                      disabled={draft.to === optionValue(account)}
                    >
                      {optionLabel(account)}
                    </option>
                  ))}
                </optgroup>
              )}
              {virtualAccounts.length > 0 && (
                <optgroup label="Virtual accounts">
                  {virtualAccounts.map((account) => (
                    <option
                      key={optionValue(account)}
                      value={optionValue(account)}
                      selected={draft.from === optionValue(account)}
                      disabled={draft.to === optionValue(account)}
                    >
                      {optionLabel(account)}
                    </option>
                  ))}
                </optgroup>
              )}
            </select>

            <div class="mt-field">
              <label for="mt-from-number" class="mt-label">
                Account number{" "}
                {!fromAccount && <span class="mt-optional">(optional)</span>}
              </label>
              <input
                id="mt-from-number"
                class="mt-input"
                type="text"
                autoComplete="off"
                readOnly={!!fromAccount}
                placeholder={
                  fromAccount ? "No account number" : "Leave empty if unknown"
                }
                aria-describedby="mt-from-hint"
                value={draft.fromNumber}
                onInput$={(_, element) => {
                  if (!fromAccount) {
                    draft.fromNumber = element.value;
                  }
                }}
              />
            </div>
            <p id="mt-from-hint" class="mt-hint">
              {fromAccount
                ? "Filled in from the account you picked, so it can't be changed here."
                : "Not needed. Pick one of your accounts to link it instead."}
            </p>
          </fieldset>

          <fieldset class="mt-field mt-wide">
            <legend class="mt-label">Counts as</legend>
            <div class="mt-choices">
              {manualCountings.map((choice) => (
                <label
                  key={choice.value}
                  class={{
                    "mt-choice": true,
                    "mt-choice-selected": draft.counting === choice.value,
                  }}
                >
                  <input
                    type="radio"
                    name="mt-counting"
                    value={choice.value}
                    checked={draft.counting === choice.value}
                    onChange$={() => {
                      draft.counting = choice.value;
                      draft.countingTouched = true;
                    }}
                  />
                  {choice.label}
                </label>
              ))}
            </div>
            <p class="mt-hint">{countingHint(draft.counting)}</p>
          </fieldset>

          <div class="mt-field mt-wide">
            <label for="mt-description" class="mt-label">
              Description <span class="mt-optional">(optional)</span>
            </label>
            <input
              id="mt-description"
              class="mt-input"
              type="text"
              autoComplete="off"
              maxLength={140}
              placeholder="For example Rent for November"
              value={draft.description}
              onInput$={(_, element) => {
                draft.description = element.value;
              }}
            />
          </div>
        </div>

        {(draft.error || linkableError.value) && (
          <p class="mt-error" role="alert">
            {draft.error ?? linkableError.value}
          </p>
        )}

        <div class="mt-actions">
          <button
            type="button"
            class="mt-secondary"
            onClick$={() => {
              dialog.value?.close();
            }}
          >
            Cancel
          </button>
          <button type="submit" class="mt-primary" disabled={draft.saving}>
            {draft.saving ? "Adding…" : "Add transaction"}
          </button>
        </div>
      </form>
    </dialog>
  );
});
