import { component$, useId, useSignal, useStyles$ } from '@builder.io/qwik';

export interface MenuItem {
    label: string;
    href: string;
}

interface SideMenuProps {
    brand?: string;
    currentPath: string;
    items?: MenuItem[];
}

const defaultItems: MenuItem[] = [
    { label: 'Overview', href: '/' },
    { label: 'Accounts', href: '/accounts' },
    { label: 'Journal', href: '/journal' },
    { label: 'About', href: '/about' },
    { label: 'Contact', href: '/contact' },
];

export default component$<SideMenuProps>((props) => {
    const dialog = useSignal<HTMLDialogElement>();
    const isOpen = useSignal(false);
    const id = useId();

    useStyles$(`
    .sm-trigger, .sm-panel {
      --sm-ink: #202d29;
      --sm-muted: #68736c;
      --sm-accent: #245a43;
      font-family: ui-sans-serif, system-ui, -apple-system, sans-serif;
      color: var(--sm-ink);
      font-size: 15px;
      line-height: 1.5;
    }
    .sm-trigger, .sm-panel, .sm-panel * { box-sizing: border-box; }
    .sm-trigger {
      position: fixed;
      top: max(20px, env(safe-area-inset-top));
      left: max(20px, env(safe-area-inset-left));
      z-index: 40;
      display: inline-flex;
      align-items: center;
      gap: 10px;
      min-height: 46px;
      padding: 10px 16px;
      border: 1px solid #dedfd7;
      border-radius: 14px;
      background: #fafaf5;
      box-shadow: 0 4px 18px #172a2110;
      font-weight: 600;
      cursor: pointer;
    }
    .sm-trigger:hover { background: #efefe7; }
    .sm-panel {
      position: fixed;
      inset: 0 auto 0 0;
      width: min(360px, calc(100vw - 24px));
      height: 100vh;
      height: 100dvh;
      max-width: none;
      max-height: none;
      margin: 0;
      padding: 0;
      border: 0;
      border-right: 1px solid #dedfd7;
      border-radius: 0 24px 24px 0;
      background: #fafaf5;
      box-shadow: 18px 0 80px #13261f20;
      overflow-y: auto;
      overscroll-behavior: contain;
    }
    .sm-panel[open] { animation: sm-enter 220ms ease-out; }
    .sm-panel::backdrop {
      background: #14221b70;
      animation: sm-fade 220ms ease-out;
    }
    html:has(.sm-panel[open]) { overflow: hidden; }
    .sm-inner {
      display: flex;
      flex-direction: column;
      min-height: 100%;
      padding: max(20px, env(safe-area-inset-top)) 24px
        max(24px, env(safe-area-inset-bottom))
        max(24px, env(safe-area-inset-left));
    }
    .sm-close-form { margin: 0 0 36px; }
    .sm-close {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      min-height: 46px;
      padding: 10px 12px;
      border: 1px solid #dedfd7;
      border-radius: 12px;
      background: transparent;
      color: var(--sm-ink);
      font: inherit;
      cursor: pointer;
    }
    .sm-close:hover { background: #eeeee5; }
    .sm-brand { display: flex; align-items: center; gap: 12px; }
    .sm-mark {
      display: grid;
      place-items: center;
      width: 40px;
      height: 40px;
      flex-shrink: 0;
      border-radius: 13px;
      background: var(--sm-accent);
      color: #e0efbe;
      font-size: 26px;
    }
    .sm-title { margin: 0; font-size: 23px; letter-spacing: -.7px; }
    .sm-caption { margin: 5px 0 30px; color: var(--sm-muted); font-size: 13px; }

    .sm-list { display: grid; gap: 6px; padding: 0; margin: 12px 0 0 0; list-style: none; }
    .sm-link {
      display: flex;
      align-items: center;
      gap: 14px;
      min-height: 52px;
      padding: 12px 14px;
      border: 1px solid transparent;
      border-radius: 12px;
      color: var(--sm-ink);
      text-decoration: none;
      transition: background 150ms ease;
    }
    .sm-link:hover { background: #eeeee5; }
    .sm-link[aria-current="page"] {
      background: #e7eddf;
      border-color: #d6e0cd;
      color: #214c36;
      font-weight: 600;
    }
    .sm-number { opacity: .65; font-size: 11px; font-variant-numeric: tabular-nums; }
    .sm-arrow { margin-left: auto; }
    .sm-footer {
      margin-top: auto;
      padding-top: 48px;
      color: var(--sm-muted);
      font-size: 12px;
    }
    .sm-footer p { margin: 0; padding-top: 20px; border-top: 1px solid #dedfd7; }
    .sm-trigger:focus-visible, .sm-panel :focus-visible {
      outline: 3px solid #4c7950;
      outline-offset: 4px;
    }
    @keyframes sm-enter { from { transform: translateX(-100%); } to { transform: translateX(0); } }
    @keyframes sm-fade { from { opacity: 0; } to { opacity: 1; } }
    @media (max-width: 480px) {
      .sm-inner { padding-right: 20px; }
      .sm-trigger { left: max(12px, env(safe-area-inset-left)); top: max(12px, env(safe-area-inset-top)); }
    }
    @media (prefers-reduced-motion: reduce) {
      .sm-panel[open], .sm-panel::backdrop { animation: none; }
      .sm-link { transition: none; }
    }
  `);

    const currentPath = props.currentPath.replace(/\/+$/, '') || '/';

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
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" aria-hidden="true">
                    <path d="M4 7h16M4 12h11M4 17h16" />
                </svg>
                Menu
            </button>

            <dialog
                ref={dialog}
                id={id}
                class="sm-panel"
                aria-labelledby={`${id}-title`}
                onClose$={() => { isOpen.value = false; }}
                onClick$={(event, element) => {
                    if (event.target !== element) return;
                    const rect = element.getBoundingClientRect();
                    if (event.clientX < rect.left || event.clientX > rect.right ||
                        event.clientY < rect.top || event.clientY > rect.bottom) {
                        element.close();
                    }
                }}
            >
                <div class="sm-inner">
                    <form method="dialog" class="sm-close-form">
                        <button class="sm-close" type="submit" autofocus aria-label="Close navigation menu">
                            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" aria-hidden="true">
                                <path d="m6 6 12 12M6 18 18 6" />
                            </svg>
                            Close
                        </button>
                    </form>

                    <div class="sm-brand">
                        <span class="sm-mark" aria-hidden="true">✳</span>
                        <h2 id={`${id}-title`} class="sm-title">{props.brand ?? 'Studio'}</h2>
                    </div>

                    <nav aria-label="Main navigation">
                        <ul class="sm-list">
                            {(props.items ?? defaultItems).map((item, index) => (
                                <li key={item.href}>
                                    <a
                                        class="sm-link"
                                        href={item.href}
                                        aria-current={(item.href.replace(/\/+$/, '') || '/') === currentPath ? 'page' : undefined}
                                        onClick$={() => { dialog.value?.close(); }}
                                    >
                                        <span class="sm-number" aria-hidden="true">{String(index + 1).padStart(2, '0')}</span>
                                        {item.label}
                                        <span class="sm-arrow" aria-hidden="true">↗</span>
                                    </a>
                                </li>
                            ))}
                        </ul>
                    </nav>
                </div>
            </dialog>
        </>
    );
});