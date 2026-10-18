import { $, component$, useSignal, useStore, useVisibleTask$ } from '@builder.io/qwik';

import { Icon } from './Icon';

interface Category {
    id: number;
    key?: string | null;
    name: string;
    icon: string;
    color: string;
    kind: string;
    isSystem: boolean;
    keywords: string[];
}

interface AiStatus {
    enabled: boolean;
    model: string;
    provider: string;
    lastRunAt?: string | null;
    lastTagged?: number | null;
    lastError?: string | null;
}

interface Correction {
    id: number;
    description: string;
    creditDebitIndicator: string;
    ownTransfer: boolean;
    tagId: number;
    replacedTagId?: number | null;
    matches: number;
    updatedAt: string;
}

interface CategoriesResponse {
    tags: Category[];
    usage: Record<string, number>;
    ai?: AiStatus | null;
    corrections?: Correction[] | null;
}

interface Editor {
    open: boolean;
    id: number | null;
    name: string;
    icon: string;
    color: string;
    kind: string;
    keywords: string;
    saving: boolean;
    error: string | null;
}

const iconChoices = [
    'tag',
    'cart',
    'utensils',
    'coffee',
    'bus',
    'car',
    'home',
    'bolt',
    'phone',
    'bag',
    'gift',
    'film',
    'music',
    'pulse',
    'heart',
    'paw',
    'book',
    'plane',
    'trend',
    'wallet',
    'receipt',
    'income',
    'transfer',
    'shield',
];

const colorChoices = [
    { value: '#2fa84f', label: 'Green' },
    { value: '#199e70', label: 'Teal' },
    { value: '#3987e5', label: 'Blue' },
    { value: '#9085e9', label: 'Violet' },
    { value: '#d55181', label: 'Pink' },
    { value: '#e66767', label: 'Red' },
    { value: '#d95926', label: 'Orange' },
    { value: '#c98500', label: 'Amber' },
    { value: '#8fd19e', label: 'Mint' },
    { value: '#8792a5', label: 'Grey' },
];

const kindChoices = [
    {
        value: 'SPENDING',
        label: 'Spending',
        hint: 'Counts as money out, and money coming back counts as a refund.',
    },
    {
        value: 'INCOME',
        label: 'Income',
        hint: 'Counts as money in.',
    },
    {
        value: 'TRANSFER',
        label: 'Transfer',
        hint: 'Money moving between your own accounts, left out of the totals.',
    },
];

function kindLabel(kind: string): string {
    return kindChoices.find((choice) => choice.value === kind)?.label ?? kind;
}

function parseKeywords(text: string): string[] {
    return text
        .split(/[,\n]/)
        .map((keyword) => keyword.trim())
        .filter((keyword) => keyword.length > 0);
}

function usageLabel(count: number): string {
    return `${count} ${count === 1 ? 'transaction' : 'transactions'}`;
}

function directionLabel(correction: Correction): string {
    const incoming = correction.creditDebitIndicator === 'CRDT';

    if (correction.ownTransfer) {
        return incoming ? 'Transfer in' : 'Transfer out';
    }

    return incoming ? 'Money in' : 'Money out';
}

function formatTime(iso: string): string {
    return new Intl.DateTimeFormat('en-GB', {
        day: 'numeric',
        month: 'short',
        hour: '2-digit',
        minute: '2-digit',
    }).format(new Date(iso));
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
    if (typeof payload === 'string') {
        return payload;
    }

    if (payload && typeof payload === 'object' && 'message' in payload) {
        const message = (payload as { message?: unknown }).message;

        if (typeof message === 'string') {
            return message;
        }
    }

    return null;
}

function isCategoriesResponse(value: unknown): value is CategoriesResponse {
    return (
        !!value &&
        typeof value === 'object' &&
        Array.isArray((value as CategoriesResponse).tags) &&
        typeof (value as CategoriesResponse).usage === 'object'
    );
}

export const CategoryManager = component$(() => {
    const data = useSignal<CategoriesResponse | null>(null);
    const loading = useSignal(true);
    const error = useSignal<string | null>(null);
    const confirmingDelete = useSignal<number | null>(null);
    const deleting = useSignal<number | null>(null);
    const forgetting = useSignal<number | null>(null);

    const editor = useStore<Editor>({
        open: false,
        id: null,
        name: '',
        icon: 'tag',
        color: colorChoices[0].value,
        kind: 'SPENDING',
        keywords: '',
        saving: false,
        error: null,
    });

    const load = $(async () => {
        loading.value = true;
        error.value = null;

        try {
            const response = await fetch('/api/tags', {
                method: 'GET',
                headers: {
                    Accept: 'application/json',
                },
                credentials: 'same-origin',
                cache: 'no-store',
            });

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(
                    messageFrom(payload) ?? `Failed to fetch categories (${response.status})`,
                );
            }

            if (!isCategoriesResponse(payload)) {
                throw new Error('Unexpected response from the category API.');
            }

            data.value = payload;
        } catch (err) {
            error.value = err instanceof Error ? err.message : 'Could not load categories.';
        } finally {
            loading.value = false;
        }
    });

    const openEditor = $((category: Category | null) => {
        editor.open = true;
        editor.id = category?.id ?? null;
        editor.name = category?.name ?? '';
        editor.icon = category?.icon ?? 'tag';
        editor.color = category?.color ?? colorChoices[0].value;
        editor.kind = category?.kind ?? 'SPENDING';
        editor.keywords = (category?.keywords ?? []).join(', ');
        editor.error = null;
        confirmingDelete.value = null;
    });

    const save = $(async () => {
        if (editor.saving) {
            return;
        }

        editor.saving = true;
        editor.error = null;

        try {
            const response = await fetch(editor.id === null ? '/api/tags' : `/api/tags/${editor.id}`, {
                method: editor.id === null ? 'POST' : 'PUT',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/json',
                },
                credentials: 'same-origin',
                body: JSON.stringify({
                    name: editor.name,
                    icon: editor.icon,
                    color: editor.color,
                    kind: editor.kind,
                    keywords: parseKeywords(editor.keywords),
                }),
            });

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(
                    messageFrom(payload) ?? `Could not save the category (${response.status})`,
                );
            }

            editor.open = false;
            await load();
        } catch (err) {
            editor.error = err instanceof Error ? err.message : 'Could not save the category.';
        } finally {
            editor.saving = false;
        }
    });

    const remove = $(async (id: number) => {
        deleting.value = id;
        error.value = null;

        try {
            const response = await fetch(`/api/tags/${id}`, {
                method: 'DELETE',
                headers: {
                    Accept: 'application/json',
                },
                credentials: 'same-origin',
            });

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(
                    messageFrom(payload) ?? `Could not delete the category (${response.status})`,
                );
            }

            confirmingDelete.value = null;

            if (editor.id === id) {
                editor.open = false;
            }

            await load();
        } catch (err) {
            error.value = err instanceof Error ? err.message : 'Could not delete the category.';
        } finally {
            deleting.value = null;
        }
    });

    const forget = $(async (id: number) => {
        forgetting.value = id;
        error.value = null;

        try {
            const response = await fetch(`/api/tags/corrections/${id}`, {
                method: 'DELETE',
                headers: {
                    Accept: 'application/json',
                },
                credentials: 'same-origin',
            });

            const payload = await readJson(response);

            if (!response.ok) {
                throw new Error(messageFrom(payload) ?? `Could not forget the change (${response.status})`);
            }

            await load();
        } catch (err) {
            error.value = err instanceof Error ? err.message : 'Could not forget the change.';
        } finally {
            forgetting.value = null;
        }
    });

    useVisibleTask$(
        () => {
            void load();
        },
        {
            strategy: 'document-ready',
        },
    );

    const categories = data.value?.tags ?? [];
    const usage = data.value?.usage ?? {};
    const ownCategories = categories.filter((category) => !category.isSystem);
    const predefinedCategories = categories.filter((category) => category.isSystem);
    const categoriesById: Record<number, Category> = Object.fromEntries(
        categories.map((category) => [category.id, category]),
    );
    const corrections = data.value?.corrections ?? [];
    const previewKeywords = parseKeywords(editor.keywords);
    const selectedKind = kindChoices.find((choice) => choice.value === editor.kind);

    return (
        <main id="main" class="page-shell cal-shell cat-shell">
            <section class="page-heading" aria-labelledby="page-title">
                <div>
                    <h1 id="page-title">
                        Categories<span>.</span>
                    </h1>
                    <p class="page-subtitle">
                        Make your own categories and choose which words in a transaction pick them.
                    </p>
                </div>

                {!editor.open && (
                    <button
                        type="button"
                        class="cat-primary cat-new"
                        disabled={!data.value}
                        onClick$={() => openEditor(null)}
                    >
                        New category
                    </button>
                )}
            </section>

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
                <section class="overview-panel cat-editor" aria-labelledby="cat-editor-title">
                    <form preventdefault:submit onSubmit$={save}>
                        <div class="cat-editor-head">
                            <span
                                class="cal-tag-tile cat-preview-tile"
                                style={{ '--tag-color': editor.color }}
                                aria-hidden="true"
                            >
                                <Icon name={editor.icon} />
                            </span>
                            <div>
                                <h2 id="cat-editor-title">
                                    {editor.id === null ? 'New category' : 'Edit category'}
                                </h2>
                                <p>{editor.name.trim() || 'Untitled'} · {kindLabel(editor.kind)}</p>
                            </div>
                        </div>

                        <div class="cat-fields">
                            <div class="cat-field">
                                <label for="cat-name" class="cat-label">
                                    Name
                                </label>
                                <input
                                    id="cat-name"
                                    class="cat-input"
                                    type="text"
                                    maxLength={40}
                                    required
                                    placeholder="For example Coffee"
                                    value={editor.name}
                                    onInput$={(_, element) => {
                                        editor.name = element.value;
                                    }}
                                />
                            </div>

                            <fieldset class="cat-field">
                                <legend class="cat-label">Counts as</legend>
                                <div class="cat-kind-row">
                                    {kindChoices.map((choice) => (
                                        <label
                                            key={choice.value}
                                            class={{
                                                'cat-kind-option': true,
                                                'cat-option-selected': editor.kind === choice.value,
                                            }}
                                        >
                                            <input
                                                type="radio"
                                                name="cat-kind"
                                                value={choice.value}
                                                checked={editor.kind === choice.value}
                                                onChange$={() => {
                                                    editor.kind = choice.value;
                                                }}
                                            />
                                            {choice.label}
                                        </label>
                                    ))}
                                </div>
                                {selectedKind && <p class="cat-hint">{selectedKind.hint}</p>}
                            </fieldset>

                            <fieldset class="cat-field">
                                <legend class="cat-label">Icon</legend>
                                <div class="cat-icon-grid">
                                    {iconChoices.map((icon) => (
                                        <button
                                            key={icon}
                                            type="button"
                                            class={{
                                                'cat-icon-option': true,
                                                'cat-option-selected': editor.icon === icon,
                                            }}
                                            aria-label={icon}
                                            aria-pressed={editor.icon === icon}
                                            onClick$={() => {
                                                editor.icon = icon;
                                            }}
                                        >
                                            <Icon name={icon} />
                                        </button>
                                    ))}
                                </div>
                            </fieldset>

                            <fieldset class="cat-field">
                                <legend class="cat-label">Color</legend>
                                <div class="cat-color-row">
                                    {colorChoices.map((color) => (
                                        <button
                                            key={color.value}
                                            type="button"
                                            class={{
                                                'cat-color-option': true,
                                                'cat-option-selected': editor.color === color.value,
                                            }}
                                            style={{ '--swatch': color.value }}
                                            aria-label={color.label}
                                            aria-pressed={editor.color === color.value}
                                            onClick$={() => {
                                                editor.color = color.value;
                                            }}
                                        />
                                    ))}
                                </div>
                            </fieldset>

                            <div class="cat-field cat-field-wide">
                                <label for="cat-keywords" class="cat-label">
                                    Match words
                                </label>
                                <input
                                    id="cat-keywords"
                                    class="cat-input"
                                    type="text"
                                    aria-describedby="cat-keywords-hint"
                                    placeholder="For example espresso, starbucks"
                                    value={editor.keywords}
                                    onInput$={(_, element) => {
                                        editor.keywords = element.value;
                                    }}
                                />
                                <span id="cat-keywords-hint" class="cat-hint">
                                    Separate words with commas. A transaction whose text contains one of
                                    them gets this category, ahead of the built-in rules.
                                </span>
                                {previewKeywords.length > 0 && (
                                    <span class="cat-chips">
                                        {previewKeywords.map((keyword, index) => (
                                            <span key={`${keyword}-${index}`} class="cat-word">
                                                {keyword}
                                            </span>
                                        ))}
                                    </span>
                                )}
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
                            <button type="submit" class="cat-primary" disabled={editor.saving}>
                                {editor.saving
                                    ? 'Saving…'
                                    : editor.id === null
                                      ? 'Create category'
                                      : 'Save changes'}
                            </button>
                        </div>
                    </form>
                </section>
            )}

            <section class="overview-panel cat-panel" aria-labelledby="cat-own-title">
                <div class="cat-panel-head">
                    <h2 id="cat-own-title">
                        <span class="tiny-mark" />
                        Your categories
                    </h2>
                    <span class="soft-badge">{ownCategories.length}</span>
                </div>

                {loading.value && !data.value ? (
                    <p class="cal-empty">Loading categories…</p>
                ) : ownCategories.length === 0 ? (
                    <p class="cal-empty">
                        No categories of your own yet. Make one to group transactions your way, for
                        example Coffee, Pets or a trip.
                    </p>
                ) : (
                    <ul class="cat-list">
                        {ownCategories.map((category) => (
                            <li key={category.id} class="cat-row">
                                <span
                                    class="cal-tag-tile"
                                    style={{ '--tag-color': category.color }}
                                    aria-hidden="true"
                                >
                                    <Icon name={category.icon} />
                                </span>
                                <span class="cat-row-main">
                                    <span class="cat-row-name">{category.name}</span>
                                    <span class="cat-row-meta">
                                        {kindLabel(category.kind)} ·{' '}
                                        {usageLabel(usage[String(category.id)] ?? 0)}
                                        {category.keywords.length > 0
                                            ? ` · matches ${category.keywords.join(', ')}`
                                            : ' · no match words'}
                                    </span>
                                </span>

                                {confirmingDelete.value === category.id ? (
                                    <span class="cat-row-actions" role="group" aria-label="Confirm delete">
                                        <span class="cat-confirm">
                                            Its transactions go back to the built-in categories.
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
                                            disabled={deleting.value === category.id}
                                            onClick$={() => remove(category.id)}
                                        >
                                            {deleting.value === category.id ? 'Deleting…' : 'Delete'}
                                        </button>
                                    </span>
                                ) : (
                                    <span class="cat-row-actions">
                                        <button
                                            type="button"
                                            class="cal-chip"
                                            onClick$={() => openEditor(category)}
                                        >
                                            Edit
                                        </button>
                                        <button
                                            type="button"
                                            class="cal-chip"
                                            onClick$={() => {
                                                confirmingDelete.value = category.id;
                                            }}
                                        >
                                            Delete
                                        </button>
                                    </span>
                                )}
                            </li>
                        ))}
                    </ul>
                )}
            </section>

            {data.value?.ai && (
                <section class="overview-panel cat-panel cat-ai" aria-labelledby="cat-ai-title">
                    <div class="cat-panel-head">
                        <h2 id="cat-ai-title">
                            <span class="tiny-mark" />
                            Sorting with AI
                        </h2>
                        <span
                            class={{
                                'cat-ai-state': true,
                                'cat-ai-on': data.value.ai.enabled && !data.value.ai.lastError,
                                'cat-ai-problem': data.value.ai.enabled && !!data.value.ai.lastError,
                            }}
                        >
                            {!data.value.ai.enabled
                                ? 'Off'
                                : data.value.ai.lastError
                                  ? 'Needs attention'
                                  : 'On'}
                        </span>
                    </div>

                    <div class="cat-ai-body">
                        <p>
                            Your remembered changes, match words and the built-in rules go first. The
                            AI sorts what they leave in Other the next time your banks sync, and uses
                            your changes as examples.
                        </p>

                        {data.value.ai.enabled ? (
                            <p class="cat-ai-detail">
                                Using {data.value.ai.model} at {data.value.ai.provider}.
                                {data.value.ai.lastRunAt &&
                                    !data.value.ai.lastError &&
                                    ` Last run ${formatTime(data.value.ai.lastRunAt)} sorted ${usageLabel(
                                        data.value.ai.lastTagged ?? 0,
                                    )}.`}
                            </p>
                        ) : (
                            <p class="cat-ai-detail">
                                Turn it on with "AI" in core/zenith/appsettings.Development.json: set
                                Enabled to true, add your ApiKey, and restart the backend.
                            </p>
                        )}

                        {data.value.ai.enabled && data.value.ai.lastError && (
                            <p class="cat-error" role="alert">
                                Last run {data.value.ai.lastRunAt ? formatTime(data.value.ai.lastRunAt) : ''}{' '}
                                failed: {data.value.ai.lastError}
                            </p>
                        )}
                    </div>
                </section>
            )}

            <section class="overview-panel cat-panel" aria-labelledby="cat-learned-title">
                <div class="cat-panel-head">
                    <h2 id="cat-learned-title">
                        <span class="tiny-mark" />
                        Learned from your changes
                    </h2>
                    {corrections.length > 0 && (
                        <span class="cat-panel-note">
                            Forget one to let the rules and the AI decide again
                        </span>
                    )}
                </div>

                {loading.value && !data.value ? (
                    <p class="cal-empty">Loading…</p>
                ) : corrections.length === 0 ? (
                    <p class="cal-empty">
                        Nothing learned yet. Click a category icon in the calendar to change it and keep
                        Remember ticked. Similar transactions then follow, and the AI uses your changes
                        as examples.
                    </p>
                ) : (
                    <ul class="cat-list">
                        {corrections.map((correction) => {
                            const category = categoriesById[correction.tagId];
                            const replaced =
                                correction.replacedTagId != null
                                    ? categoriesById[correction.replacedTagId]
                                    : undefined;

                            return (
                                <li key={correction.id} class="cat-row">
                                    <span
                                        class="cal-tag-tile"
                                        style={{ '--tag-color': category?.color ?? '#7b8496' }}
                                        aria-hidden="true"
                                    >
                                        <Icon key={category?.icon ?? 'tag'} name={category?.icon ?? 'tag'} />
                                    </span>
                                    <span class="cat-row-main">
                                        <span class="cat-row-name">{correction.description}</span>
                                        <span class="cat-row-meta">
                                            {`${directionLabel(correction)} · ${category?.name ?? 'Unknown'}${
                                                replaced ? ` instead of ${replaced.name}` : ''
                                            } · ${usageLabel(correction.matches)}`}
                                        </span>
                                    </span>
                                    <span class="cat-row-actions">
                                        <button
                                            type="button"
                                            class="cal-chip"
                                            disabled={forgetting.value === correction.id}
                                            aria-label={`Forget ${correction.description}`}
                                            onClick$={() => forget(correction.id)}
                                        >
                                            {forgetting.value === correction.id ? 'Forgetting…' : 'Forget'}
                                        </button>
                                    </span>
                                </li>
                            );
                        })}
                    </ul>
                )}
            </section>

            <section class="overview-panel cat-panel" aria-labelledby="cat-system-title">
                <div class="cat-panel-head">
                    <h2 id="cat-system-title">
                        <span class="tiny-mark" />
                        Predefined categories
                    </h2>
                    <span class="cat-panel-note">Picked automatically from the bank's text</span>
                </div>

                <ul class="cat-list cat-list-compact">
                    {predefinedCategories.map((category) => (
                        <li key={category.id} class="cat-row">
                            <span
                                class="cal-tag-tile"
                                style={{ '--tag-color': category.color }}
                                aria-hidden="true"
                            >
                                <Icon name={category.icon} />
                            </span>
                            <span class="cat-row-main">
                                <span class="cat-row-name">{category.name}</span>
                                <span class="cat-row-meta">
                                    {kindLabel(category.kind)} ·{' '}
                                    {usageLabel(usage[String(category.id)] ?? 0)}
                                </span>
                            </span>
                        </li>
                    ))}
                </ul>
            </section>
        </main>
    );
});
