import {
  $,
  component$,
  useSignal,
  useStore,
  useVisibleTask$,
} from "@builder.io/qwik";

import { Icon } from "./Icon";
import { inboxChangedEvent } from "../utils/sharing";

interface Person {
  id: number;
  name: string;
}

interface Partnership {
  id: number;
  status: string;
  partner: Person;
  sentByMe: boolean;
}

interface SplitRule {
  id: number;
  myShare: number;
  partnerShare: number;
  validFrom: string;
  status: string;
  proposedByMe: boolean;
}

interface ShareableAccount {
  accountId: number;
  name: string;
  bankName: string;
  iban?: string | null;
  sharingId?: number | null;
  status?: string | null;
  partnerHasIt: boolean;
}

interface SharedAccount {
  sharingId: number;
  accountId: number;
  name: string;
  bankName: string;
  iban?: string | null;
}

interface InboxItem {
  kind: string;
  id: number;
  text: string;
}

interface NotificationItem {
  id: number;
  text: string;
  createdAt: string;
  read: boolean;
}

interface SharingState {
  me: Person;
  partnership?: Partnership | null;
  splitRules: SplitRule[];
  myAccounts: ShareableAccount[];
  partnerAccounts: SharedAccount[];
  inbox: InboxItem[];
  notifications: NotificationItem[];
  unread: number;
}

interface Forms {
  email: string;
  myShare: string;
  splitShare: string;
  splitFrom: string;
  busy: boolean;
  error: string | null;
  notice: string | null;
}

function lastFour(iban?: string | null): string {
  const digits = (iban ?? "").replace(/\s+/g, "");

  return digits ? ` ··${digits.slice(-4)}` : "";
}

function percent(value: number): string {
  return `${new Intl.NumberFormat("sv-SE", { maximumFractionDigits: 2 }).format(value)} %`;
}

function today(): string {
  const now = new Date();

  return new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()))
    .toISOString()
    .slice(0, 10);
}

async function message(response: Response): Promise<string> {
  const payload = (await response.json().catch(() => null)) as {
    message?: string;
  } | null;

  return payload?.message ?? `Something went wrong (${response.status})`;
}

export const SharedEconomy = component$(() => {
  const state = useSignal<SharingState | null>(null);
  const loadError = useSignal<string | null>(null);
  const forms = useStore<Forms>({
    email: "",
    myShare: "50",
    splitShare: "50",
    splitFrom: today(),
    busy: false,
    error: null,
    notice: null,
  });

  const load = $(async () => {
    try {
      const response = await fetch("/api/sharing", {
        headers: { Accept: "application/json" },
        credentials: "same-origin",
        cache: "no-store",
      });

      if (!response.ok) {
        throw new Error(await message(response));
      }

      state.value = (await response.json()) as SharingState;
      loadError.value = null;
      window.dispatchEvent(new CustomEvent(inboxChangedEvent));
    } catch (err) {
      loadError.value =
        err instanceof Error ? err.message : "Could not load sharing.";
    }
  });

  const act = $(async (path: string, body: unknown, done: string) => {
    if (forms.busy) {
      return;
    }

    forms.busy = true;
    forms.error = null;
    forms.notice = null;

    try {
      const response = await fetch(`/api/sharing/${path}`, {
        method: "POST",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        credentials: "same-origin",
        body: JSON.stringify(body ?? {}),
      });

      if (!response.ok) {
        throw new Error(await message(response));
      }

      forms.notice = done;
      await load();
    } catch (err) {
      forms.error =
        err instanceof Error ? err.message : "Something went wrong.";
    } finally {
      forms.busy = false;
    }
  });

  useVisibleTask$(
    async () => {
      await load();
    },
    { strategy: "document-ready" },
  );

  const current = state.value;
  const partnership = current?.partnership ?? null;
  const partnerName = partnership?.partner.name ?? "your partner";
  const accepted = partnership?.status === "ACCEPTED";
  const activeSplit = (current?.splitRules ?? [])
    .filter((rule) => rule.status === "ACCEPTED" && rule.validFrom <= today())
    .at(-1);
  const pendingSplit = (current?.splitRules ?? []).find(
    (rule) => rule.status === "PENDING",
  );

  return (
    <main id="main" class="page-shell cal-shell economy-shell">
      <section class="page-heading" aria-labelledby="page-title">
        <div>
          <h1 id="page-title">
            Shared economy<span>.</span>
          </h1>
          <p class="page-subtitle">
            Share accounts with your partner and split verified shared costs.
          </p>
        </div>
      </section>

      {loadError.value && (
        <p class="cat-error" role="alert">
          {loadError.value}
        </p>
      )}
      {forms.error && (
        <p class="cat-error" role="alert">
          {forms.error}
        </p>
      )}
      {forms.notice && (
        <p class="cal-tx-notice economy-notice">
          <Icon name="check" />
          {forms.notice}
        </p>
      )}

      {current && (current.inbox.length > 0 || current.unread > 0) && (
        <section
          id="inbox"
          class="overview-panel economy-panel"
          aria-labelledby="inbox-title"
        >
          <div class="economy-panel-head">
            <h2 id="inbox-title">
              Inbox{" "}
              <span class="economy-count">
                {current.inbox.length + current.unread}
              </span>
            </h2>
            {current.unread > 0 && (
              <button
                type="button"
                class="cal-chip"
                disabled={forms.busy}
                onClick$={() =>
                  act("notifications/read", {}, "Marked as read.")
                }
              >
                Mark as read
              </button>
            )}
          </div>
          <ul class="economy-list">
            {current.inbox.map((item) => (
              <li key={`${item.kind}-${item.id}`} class="economy-request">
                <span>{item.text}</span>
                <span class="economy-actions">
                  <button
                    type="button"
                    class="cat-primary"
                    disabled={forms.busy}
                    onClick$={() =>
                      act(
                        `${item.kind === "partnership" ? "partner" : item.kind === "account" ? "shares" : "split"}/${item.id}/respond`,
                        { accept: true },
                        "Accepted.",
                      )
                    }
                  >
                    Accept
                  </button>
                  <button
                    type="button"
                    class="cal-chip"
                    disabled={forms.busy}
                    onClick$={() =>
                      act(
                        `${item.kind === "partnership" ? "partner" : item.kind === "account" ? "shares" : "split"}/${item.id}/respond`,
                        { accept: false },
                        "Declined.",
                      )
                    }
                  >
                    Decline
                  </button>
                </span>
              </li>
            ))}
            {current.notifications
              .filter((note) => !note.read)
              .map((note) => (
                <li key={note.id} class="economy-note">
                  <Icon name="info" />
                  <span>{note.text}</span>
                </li>
              ))}
          </ul>
        </section>
      )}

      {current && (
        <section
          class="overview-panel economy-panel"
          aria-labelledby="partner-title"
        >
          <div class="economy-panel-head">
            <h2 id="partner-title">Partner</h2>
          </div>

          {!partnership && (
            <form
              class="economy-form"
              preventdefault:submit
              onSubmit$={() =>
                act(
                  "partner",
                  {
                    email: forms.email,
                    myShare: Number(forms.myShare.replace(",", ".")),
                  },
                  "Invitation sent. Nothing is shared until they accept.",
                )
              }
            >
              <p class="cat-hint">
                Invite the person you share costs with. They need their own
                Zenith account, and nothing is shared until they accept.
              </p>
              <div class="cat-field">
                <label for="partner-email" class="cat-label">
                  Their email in Zenith
                </label>
                <input
                  id="partner-email"
                  class="cat-input"
                  type="email"
                  required
                  autoComplete="off"
                  value={forms.email}
                  onInput$={(_, element) => {
                    forms.email = element.value;
                  }}
                />
              </div>
              <div class="cat-field">
                <label for="partner-share" class="cat-label">
                  Your share of shared costs, in %
                </label>
                <input
                  id="partner-share"
                  class="cat-input"
                  type="text"
                  inputMode="decimal"
                  required
                  value={forms.myShare}
                  onInput$={(_, element) => {
                    forms.myShare = element.value;
                  }}
                />
                <span class="cat-hint">
                  For example 50 for 50/50 or 60 for 60/40. You can change it
                  later if you both agree.
                </span>
              </div>
              <button type="submit" class="cat-primary" disabled={forms.busy}>
                Send invitation
              </button>
            </form>
          )}

          {partnership?.status === "PENDING" && partnership.sentByMe && (
            <div class="economy-row">
              <span>{`Waiting for ${partnerName} to answer your invitation.`}</span>
              <button
                type="button"
                class="cal-chip"
                disabled={forms.busy}
                onClick$={() =>
                  act(
                    `partner/${partnership.id}/end`,
                    {},
                    "Invitation withdrawn.",
                  )
                }
              >
                Withdraw
              </button>
            </div>
          )}

          {partnership?.status === "PENDING" && !partnership.sentByMe && (
            <p class="cat-hint">{`${partnerName} invited you. Answer in the inbox above.`}</p>
          )}

          {accepted && (
            <>
              <div class="economy-row">
                <span>
                  <strong>{partnerName}</strong>
                  {activeSplit
                    ? ` · shared costs split ${percent(activeSplit.myShare)} you, ${percent(activeSplit.partnerShare)} ${partnerName}`
                    : ""}
                </span>
                <button
                  type="button"
                  class="cal-chip cal-manual-remove"
                  disabled={forms.busy}
                  onClick$={() => {
                    if (
                      window.confirm(
                        `Stop sharing your economy with ${partnerName}? Shared accounts become private again from today. Earlier months keep their split.`,
                      )
                    ) {
                      void act(
                        `partner/${partnership.id}/end`,
                        {},
                        "You no longer share your economy.",
                      );
                    }
                  }}
                >
                  Stop sharing
                </button>
              </div>

              <ul class="economy-list">
                {current.splitRules
                  .filter((rule) => rule.status === "ACCEPTED")
                  .map((rule) => (
                    <li key={rule.id} class="economy-note">
                      <Icon name="calendar" />
                      <span>{`From ${rule.validFrom === "2000-01-01" ? "the start" : rule.validFrom}: ${percent(rule.myShare)} you, ${percent(rule.partnerShare)} ${partnerName}`}</span>
                    </li>
                  ))}
                {pendingSplit && (
                  <li class="economy-note">
                    <Icon name="info" />
                    <span>{`${pendingSplit.proposedByMe ? "You suggested" : `${partnerName} suggested`} ${percent(pendingSplit.myShare)} you from ${pendingSplit.validFrom}. ${pendingSplit.proposedByMe ? "Waiting for an answer." : "Answer in the inbox."}`}</span>
                  </li>
                )}
              </ul>

              <form
                class="economy-form economy-inline"
                preventdefault:submit
                onSubmit$={() =>
                  act(
                    "split",
                    {
                      myShare: Number(forms.splitShare.replace(",", ".")),
                      validFrom: forms.splitFrom,
                    },
                    `Suggested to ${partnerName}. It applies when they accept.`,
                  )
                }
              >
                <div class="cat-field">
                  <label for="split-share" class="cat-label">
                    Suggest a new split: your %
                  </label>
                  <input
                    id="split-share"
                    class="cat-input"
                    type="text"
                    inputMode="decimal"
                    value={forms.splitShare}
                    onInput$={(_, element) => {
                      forms.splitShare = element.value;
                    }}
                  />
                </div>
                <div class="cat-field">
                  <label for="split-from" class="cat-label">
                    From
                  </label>
                  <input
                    id="split-from"
                    class="cat-input"
                    type="date"
                    value={forms.splitFrom}
                    onInput$={(_, element) => {
                      forms.splitFrom = element.value;
                    }}
                  />
                </div>
                <button type="submit" class="cal-chip" disabled={forms.busy}>
                  Suggest
                </button>
              </form>
            </>
          )}
        </section>
      )}

      {current && accepted && (
        <section
          class="overview-panel economy-panel"
          aria-labelledby="accounts-title"
        >
          <div class="economy-panel-head">
            <h2 id="accounts-title">Shared accounts</h2>
          </div>
          <p class="cat-hint">
            Everything spent from a shared account counts as a shared cost.
            Money you move into it counts as your contribution.
          </p>
          <ul class="economy-list">
            {current.myAccounts.map((account) => (
              <li key={account.accountId} class="economy-row">
                <span>
                  <strong>{`${account.name}${lastFour(account.iban)}`}</strong>
                  {` · ${account.bankName}`}
                  {account.partnerHasIt && (
                    <span class="cal-ai-badge cal-origin-user">
                      {`${partnerName} has it too`}
                    </span>
                  )}
                </span>
                {account.status === "ACCEPTED" ? (
                  <button
                    type="button"
                    class="cal-chip"
                    disabled={forms.busy}
                    onClick$={() =>
                      act(
                        `shares/${account.sharingId}/end`,
                        {},
                        "The account is private again from today.",
                      )
                    }
                  >
                    Shared · stop
                  </button>
                ) : account.status === "PENDING" ? (
                  <button
                    type="button"
                    class="cal-chip"
                    disabled={forms.busy}
                    onClick$={() =>
                      act(
                        `shares/${account.sharingId}/end`,
                        {},
                        "Request withdrawn.",
                      )
                    }
                  >
                    Waiting · withdraw
                  </button>
                ) : (
                  <button
                    type="button"
                    class="cal-chip"
                    disabled={forms.busy}
                    onClick$={() =>
                      act(
                        `accounts/${account.accountId}/share`,
                        {},
                        `Asked ${partnerName} to accept the shared account.`,
                      )
                    }
                  >
                    Share
                  </button>
                )}
              </li>
            ))}
            {current.partnerAccounts.map((account) => (
              <li key={`p-${account.sharingId}`} class="economy-row">
                <span>
                  <strong>{`${account.name}${lastFour(account.iban)}`}</strong>
                  {` · ${account.bankName} · shared by ${partnerName}`}
                </span>
                <button
                  type="button"
                  class="cal-chip"
                  disabled={forms.busy}
                  onClick$={() =>
                    act(
                      `shares/${account.sharingId}/end`,
                      {},
                      "You no longer see that account.",
                    )
                  }
                >
                  Stop
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </main>
  );
});
