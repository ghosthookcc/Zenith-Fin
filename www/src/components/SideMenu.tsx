import {
  $,
  component$,
  useId,
  useOnWindow,
  useSignal,
  useStyles$,
  useVisibleTask$,
} from "@builder.io/qwik";

import { Icon } from "./Icon";
import { NewTransaction } from "./NewTransaction";
import { openManualTransaction } from "../utils/manual";
import { inboxChangedEvent } from "../utils/sharing";

interface MenuItem {
  label: string;
  href: string;
  icon: string;
  hint: string;
}

interface SideMenuProps {
  currentPath: string;
}

const items: MenuItem[] = [
  {
    label: "Accounts",
    href: "/accounts",
    icon: "wallet",
    hint: "Balances, banks and shared accounts",
  },
  {
    label: "Calendar",
    href: "/calendar",
    icon: "calendar",
    hint: "Every transaction, day by day",
  },
  {
    label: "Recurring",
    href: "/recurring",
    icon: "refresh",
    hint: "Bills, income and what is left each month",
  },
  {
    label: "Avanza",
    href: "/avanza",
    icon: "trend",
    hint: "Account values over time",
  },
  {
    label: "Categories",
    href: "/categories",
    icon: "tag",
    hint: "Sort and train your categories",
  },
  {
    label: "Shared economy",
    href: "/shared",
    icon: "users",
    hint: "Partner, shared accounts and inbox",
  },
];

export default component$<SideMenuProps>((props) => {
  const dialog = useSignal<HTMLDialogElement>();
  const isOpen = useSignal(false);
  const inbox = useSignal(0);
  const id = useId();

  const loadInbox = $(async () => {
    try {
      const response = await fetch("/api/sharing/inbox-count", {
        headers: { Accept: "application/json" },
        credentials: "same-origin",
        cache: "no-store",
      });

      if (response.ok) {
        inbox.value =
          ((await response.json()) as { count?: number }).count ?? 0;
      }
    } catch {
      inbox.value = 0;
    }
  });

  useVisibleTask$(
    async () => {
      await loadInbox();
    },
    { strategy: "document-idle" },
  );

  useOnWindow(inboxChangedEvent, loadInbox);

  useStyles$(`
    .sm-trigger, .sm-panel {
      font-family: 'DM Sans', ui-sans-serif, system-ui, sans-serif;
      color: #f1f2f5;
      font-size: 15px;
      line-height: 1.5;
    }
    .sm-trigger, .sm-panel, .sm-panel * { box-sizing: border-box; }
    .sm-trigger {
      position: fixed;
      top: max(18px, env(safe-area-inset-top));
      left: max(18px, env(safe-area-inset-left));
      z-index: 40;
      display: inline-flex;
      align-items: center;
      gap: 9px;
      min-height: 42px;
      padding: 8px 15px 8px 12px;
      border: 1px solid #2b3240;
      border-radius: 13px;
      background: #161b25d9;
      backdrop-filter: blur(10px);
      -webkit-backdrop-filter: blur(10px);
      box-shadow: 0 6px 22px #0000004d;
      color: #f1f2f5;
      font-weight: 600;
      cursor: pointer;
      transition: background 150ms ease, border-color 150ms ease;
    }
    .sm-trigger:hover { background: #1d2330; border-color: #3a4252; }
    .sm-badge {
      display: inline-grid;
      place-items: center;
      min-width: 20px;
      height: 20px;
      margin-left: 6px;
      padding: 0 6px;
      border-radius: 999px;
      background: #c1d8b6;
      color: #11151d;
      font-size: 11.5px;
      font-weight: 700;
      vertical-align: 1px;
    }
    .sm-panel {
      position: fixed;
      inset: 0 auto 0 0;
      width: min(340px, calc(100vw - 24px));
      height: 100vh;
      height: 100dvh;
      max-width: none;
      max-height: none;
      margin: 0;
      padding: 0;
      border: 0;
      border-right: 1px solid #262d3a;
      border-radius: 0 22px 22px 0;
      background: #141922;
      box-shadow: 24px 0 80px #0000008c;
      overflow-y: auto;
      overscroll-behavior: contain;
    }
    .sm-panel[open] { animation: sm-enter 220ms ease-out; }
    .sm-panel::backdrop {
      background: #05080db3;
      backdrop-filter: blur(3px);
      -webkit-backdrop-filter: blur(3px);
      animation: sm-fade 220ms ease-out;
    }
    html:has(.sm-panel[open]) { overflow: hidden; }
    .sm-inner {
      display: flex;
      flex-direction: column;
      min-height: 100%;
      padding: max(18px, env(safe-area-inset-top)) 20px
        max(22px, env(safe-area-inset-bottom))
        max(20px, env(safe-area-inset-left));
    }
    .sm-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      margin-bottom: 34px;
    }
    .sm-brand { display: flex; align-items: center; gap: 12px; min-width: 0; }
    .sm-mark {
      display: grid;
      place-items: center;
      width: 40px;
      height: 40px;
      flex-shrink: 0;
      border-radius: 12px;
      background: #c1d8b6;
      color: #11151d;
    }
    .sm-mark svg { width: 24px; height: 24px; }
    .sm-title {
      margin: 0;
      font-family: 'Manrope', sans-serif;
      font-size: 21px;
      font-weight: 750;
      letter-spacing: -0.6px;
    }
    .sm-caption { margin: 1px 0 0; color: #939bad; font-size: 12.5px; }
    .sm-close-form { margin: 0; }
    .sm-close {
      display: grid;
      place-items: center;
      width: 38px;
      height: 38px;
      padding: 0;
      border: 1px solid #2b3240;
      border-radius: 11px;
      background: transparent;
      color: #b6becc;
      cursor: pointer;
      transition: background 150ms ease, color 150ms ease;
    }
    .sm-close:hover { background: #1d2330; color: #f1f2f5; }
    .sm-section {
      margin: 0 0 10px;
      padding: 0 12px;
      color: #6f788a;
      font-size: 11px;
      font-weight: 600;
      letter-spacing: 1.4px;
      text-transform: uppercase;
    }
    .sm-list { display: grid; gap: 4px; padding: 0; margin: 0; list-style: none; }
    .sm-link {
      position: relative;
      display: flex;
      align-items: center;
      gap: 13px;
      min-height: 58px;
      padding: 10px 12px;
      border: 1px solid transparent;
      border-radius: 13px;
      color: #d6dbe4;
      text-decoration: none;
      transition: background 150ms ease, border-color 150ms ease;
    }
    .sm-link:hover { background: #1a202a; color: #f1f2f5; }
    .sm-icon {
      display: grid;
      place-items: center;
      width: 36px;
      height: 36px;
      flex-shrink: 0;
      border: 1px solid #2b3240;
      border-radius: 11px;
      background: #181e28;
      color: #b6becc;
    }
    .sm-icon svg { width: 18px; height: 18px; }
    .sm-text { display: flex; flex-direction: column; min-width: 0; }
    .sm-label { font-weight: 600; }
    .sm-hint {
      overflow: hidden;
      color: #8a93a5;
      font-size: 12.5px;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .sm-link[aria-current="page"] {
      background: #1d2430;
      border-color: #2f3746;
      color: #f1f2f5;
    }
    .sm-link[aria-current="page"] .sm-icon {
      border-color: #5d7356;
      background: #c1d8b61f;
      color: #c1d8b6;
    }
    .sm-link[aria-current="page"]::before {
      content: '';
      position: absolute;
      left: -20px;
      top: 14px;
      bottom: 14px;
      width: 3px;
      border-radius: 0 3px 3px 0;
      background: #c1d8b6;
    }
    .sm-action {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 9px;
      width: 100%;
      min-height: 48px;
      margin: 0 0 28px;
      padding: 10px 16px;
      border: 1px solid #c1d8b6;
      border-radius: 13px;
      background: #c1d8b6;
      color: #11151d;
      font-weight: 650;
      cursor: pointer;
      transition: background 150ms ease, border-color 150ms ease;
    }
    .sm-action:hover { background: #d3e6c9; border-color: #d3e6c9; }
    .sm-action svg { width: 18px; height: 18px; }
    .sm-trigger:focus-visible, .sm-panel :focus-visible {
      outline: 2px solid #c1d8b6;
      outline-offset: 3px;
    }
    @keyframes sm-enter { from { transform: translateX(-100%); } to { transform: translateX(0); } }
    @keyframes sm-fade { from { opacity: 0; } to { opacity: 1; } }
    @media (max-width: 480px) {
      .sm-trigger { left: max(12px, env(safe-area-inset-left)); top: max(12px, env(safe-area-inset-top)); }
    }
    @media (prefers-reduced-motion: reduce) {
      .sm-panel[open], .sm-panel::backdrop { animation: none; }
      .sm-link, .sm-trigger, .sm-close, .sm-action { transition: none; }
    }
  `);

  const currentPath = props.currentPath.replace(/\/+$/, "") || "/";

  return (
    <>
      <button
        type="button"
        class="sm-trigger"
        aria-label="Open navigation menu"
        aria-haspopup="dialog"
        aria-controls={id}
        aria-expanded={isOpen.value}
        onClick$={() => {
          if (dialog.value && !dialog.value.open) {
            dialog.value.showModal();
            isOpen.value = true;
          }
        }}
      >
        <svg
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.7"
          stroke-linecap="round"
          aria-hidden="true"
        >
          <path d="M4 7h16M4 12h11M4 17h16" />
        </svg>
        Menu
        {inbox.value > 0 && (
          <span
            class="sm-badge"
            aria-label={`${inbox.value} new in your inbox`}
          >
            {inbox.value}
          </span>
        )}
      </button>

      <dialog
        ref={dialog}
        id={id}
        class="sm-panel"
        aria-labelledby={`${id}-title`}
        onClose$={() => {
          isOpen.value = false;
        }}
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
        <div class="sm-inner">
          <div class="sm-head">
            <div class="sm-brand">
              <span class="sm-mark" aria-hidden="true">
                <svg
                  viewBox="0 0 64 64"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="7"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <path d="M12 48 32 27l20 21" />
                  <path d="M24 48l8-8.5 8 8.5" stroke-width="5" />
                  <circle
                    cx="32"
                    cy="13"
                    r="4.5"
                    fill="currentColor"
                    stroke="none"
                  />
                </svg>
              </span>
              <div>
                <h2 id={`${id}-title`} class="sm-title">
                  Zenith
                </h2>
                <p class="sm-caption">Your money, all in one place</p>
              </div>
            </div>

            <form method="dialog" class="sm-close-form">
              <button
                class="sm-close"
                type="submit"
                autofocus
                aria-label="Close navigation menu"
              >
                <Icon name="close" />
              </button>
            </form>
          </div>

          <button
            type="button"
            class="sm-action"
            onClick$={() => {
              dialog.value?.close();
              openManualTransaction();
            }}
          >
            <Icon name="plus" />
            New transaction
          </button>

          <nav aria-label="Main navigation">
            <p class="sm-section">Menu</p>
            <ul class="sm-list">
              {items.map((item) => (
                <li key={item.href}>
                  <a
                    class="sm-link"
                    href={item.href}
                    aria-current={
                      item.href === currentPath ? "page" : undefined
                    }
                    onClick$={() => {
                      dialog.value?.close();
                    }}
                  >
                    <span class="sm-icon" aria-hidden="true">
                      <Icon name={item.icon} />
                    </span>
                    <span class="sm-text">
                      <span class="sm-label">
                        {item.label}
                        {item.href === "/shared" && inbox.value > 0 && (
                          <span class="sm-badge">{inbox.value}</span>
                        )}
                      </span>
                      <span class="sm-hint">{item.hint}</span>
                    </span>
                  </a>
                </li>
              ))}
            </ul>
          </nav>
        </div>
      </dialog>

      <NewTransaction />
    </>
  );
});
