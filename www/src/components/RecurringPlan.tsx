import {
  $,
  component$,
  useSignal,
  useStore,
  type PropFunction,
} from "@builder.io/qwik";

import { Icon } from "./Icon";
import {
  count,
  formatDay,
  formatKr,
  formatMonth,
  maskedAmount,
  type Plan,
  type PlanDirection,
  type PlanItem,
  type PlanMonth,
  type PlanOccurrence,
  type PlanPart,
  type PlanSuggestion,
} from "../utils/recurring";

type EditableUnit = "MONTH" | "YEAR";

interface PlanProps {
  plan: Plan;
  today: string;
  hidden: boolean;
  onChange$: PropFunction<() => Promise<void>>;
}

interface EditorPart {
  key: number;
  name: string;
  amount: string;
}

interface EditorState {
  mode: "add" | "edit";
  itemId: number | null;
  bank: boolean;
  title: string;
  direction: PlanDirection;
  name: string;
  amount: string;
  every: string;
  unit: EditableUnit;
  followBank: boolean;
  lastDate: string;
  endDate: string;
  bankAmount: number | null;
  bankCadence: string;
  parts: EditorPart[];
  nextPart: number;
  saving: boolean;
  error: string | null;
}

const planItemsUrl = "/api/recurring/plan/items";
const maxNameLength = 60;

const suggestionGroups: { kind: string; title: string }[] = [
  { kind: "INCOME", title: "Income" },
  { kind: "COST", title: "Costs" },
  { kind: "SAVING", title: "Savings" },
  { kind: "TRANSFER", title: "Card bills and transfers" },
];

function parseAmount(text: string): number | null {
  const normalized = text.replace(/\s/g, "").replace(",", ".");

  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) {
    return null;
  }

  const amount = Number(normalized);

  return amount > 0 ? amount : null;
}

function amountText(amount: number | null | undefined): string {
  if (amount == null) {
    return "";
  }

  return (
    Number.isInteger(amount) ? String(amount) : amount.toFixed(2)
  ).replace(".", ",");
}

function restText(rest: number): string {
  return rest > 0 ? "Unexplained" : "Parts over the amount";
}

function plainName(name: string): string {
  return name.replace(/\s+/g, " ").trim().slice(0, maxNameLength).trim();
}

async function send(
  method: string,
  url: string,
  body?: Record<string, unknown>,
): Promise<void> {
  const response = await fetch(url, {
    method,
    headers: {
      Accept: "application/json",
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
    },
    credentials: "same-origin",
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    const payload = (await response.json().catch(() => null)) as {
      message?: string;
    } | null;

    throw new Error(
      payload?.message ?? `Could not change your plan (${response.status})`,
    );
  }
}

function statusText(occurrence: PlanOccurrence, kind?: string | null): string {
  switch (occurrence.status) {
    case "PAID":
      return occurrence.direction === "IN"
        ? "Received, from your bank"
        : kind === "SAVING"
          ? "Saved, from your bank"
          : "Paid, from your bank";
    case "LATE":
      return "Not seen at your bank yet";
    case "PASSED":
      return "The date has passed";
    default:
      return "Coming up";
  }
}

function stateText(item: PlanItem, today: string): string {
  switch (item.state) {
    case "MISSING":
      return "Not in your history now";
    case "ENDED":
      return item.endDate ? `Ended ${formatDay(item.endDate, true)}` : "Ended";
    case "QUIET":
      return item.lastDate
        ? `Nothing since ${formatDay(item.lastDate, true)}`
        : "Nothing seen lately";
    default:
      return item.next
        ? `Next ${formatDay(item.next, item.next.slice(0, 4) !== today.slice(0, 4))}`
        : "No more dates";
  }
}

export const RecurringPlan = component$<PlanProps>((props) => {
  const selected = useSignal(0);
  const dialog = useSignal<HTMLDialogElement>();
  const busy = useSignal<string | null>(null);
  const notice = useSignal<string | null>(null);
  const failure = useSignal<string | null>(null);
  const editor = useStore<EditorState>({
    mode: "add",
    itemId: null,
    bank: false,
    title: "",
    direction: "OUT",
    name: "",
    amount: "",
    every: "1",
    unit: "MONTH",
    followBank: false,
    lastDate: "",
    endDate: "",
    bankAmount: null,
    bankCadence: "",
    parts: [],
    nextPart: 1,
    saving: false,
    error: null,
  });

  const closeEditor = $(() => {
    dialog.value?.close();
  });

  const openAdd = $((direction: PlanDirection) => {
    editor.mode = "add";
    editor.itemId = null;
    editor.bank = false;
    editor.title = direction === "IN" ? "Add money in" : "Add money out";
    editor.direction = direction;
    editor.name = "";
    editor.amount = "";
    editor.every = "1";
    editor.unit = "MONTH";
    editor.followBank = false;
    editor.lastDate = props.today;
    editor.endDate = "";
    editor.bankAmount = null;
    editor.bankCadence = "";
    editor.parts = [];
    editor.nextPart = 1;
    editor.saving = false;
    editor.error = null;

    if (dialog.value && !dialog.value.open) {
      dialog.value.showModal();
    }
  });

  const openEdit = $((item: PlanItem) => {
    const bank = item.source === "BANK";

    editor.mode = "edit";
    editor.itemId = item.id;
    editor.bank = bank;
    editor.title = `Change ${item.name}`;
    editor.direction = item.direction;
    editor.name = item.customName ?? "";
    editor.amount = amountText(item.customAmount);
    editor.every = String(
      item.unit === "WEEK" || item.every < 1 ? 1 : item.every,
    );
    editor.unit = item.unit === "YEAR" ? "YEAR" : "MONTH";
    editor.followBank = bank && !item.customCadence;
    editor.lastDate = item.lastDate ?? "";
    editor.endDate = item.endDate ?? "";
    editor.bankAmount = item.bankAmount ?? null;
    editor.bankCadence = item.bankCadenceLabel ?? "";
    editor.parts = item.parts.map((part, position) => ({
      key: position + 1,
      name: part.name,
      amount: amountText(part.amount),
    }));
    editor.nextPart = item.parts.length + 1;
    editor.saving = false;
    editor.error = null;

    if (dialog.value && !dialog.value.open) {
      dialog.value.showModal();
    }
  });

  const addPart = $(() => {
    editor.parts = [
      ...editor.parts,
      { key: editor.nextPart, name: "", amount: "" },
    ];
    editor.nextPart += 1;
  });

  const removePart = $((key: number) => {
    editor.parts = editor.parts.filter((part) => part.key !== key);
  });

  const save = $(async () => {
    const typed = editor.amount.trim();
    const amount = typed === "" ? null : parseAmount(typed);
    const ownCadence = !editor.bank || !editor.followBank;
    const every = Number(editor.every);

    if (typed !== "" && amount === null) {
      editor.error = "Enter the amount in kronor, like 1 250 or 99,50.";
      return;
    }

    if (!editor.bank && amount === null) {
      editor.error = "Enter an amount.";
      return;
    }

    if (ownCadence && (!Number.isInteger(every) || every < 1 || every > 120)) {
      editor.error = "It can repeat every 1 to 120 months or years.";
      return;
    }

    if (!editor.bank && !editor.lastDate) {
      editor.error = "Enter when it last happened.";
      return;
    }

    if (!editor.bank && editor.endDate && editor.endDate < editor.lastDate) {
      editor.error = "It cannot end before it last happened.";
      return;
    }

    const splitting = editor.direction === "OUT";
    const parts = splitting
      ? editor.parts.map((part) => ({
          name: part.name.trim(),
          amount: parseAmount(part.amount.trim()),
        }))
      : [];

    if (parts.some((part) => part.name === "" || part.amount === null)) {
      editor.error =
        "Give every part a name and an amount, like 1 250 or 99,50.";
      return;
    }

    const name = editor.name.trim();
    const adding = editor.mode === "add";

    editor.saving = true;
    editor.error = null;
    notice.value = null;
    failure.value = null;

    try {
      await send(
        adding ? "POST" : "PUT",
        adding ? planItemsUrl : `${planItemsUrl}/${editor.itemId}`,
        {
          direction: editor.bank ? null : editor.direction,
          name: name === "" ? null : name,
          amount,
          every: ownCadence ? every : null,
          unit: ownCadence ? editor.unit : null,
          lastDate: editor.bank ? null : editor.lastDate,
          endDate: editor.endDate === "" ? null : editor.endDate,
          parts,
        },
      );

      dialog.value?.close();
      notice.value = adding
        ? `${name === "" ? "The row" : name} is in your plan.`
        : "Your change is saved.";
      await props.onChange$();
    } catch (err) {
      editor.error =
        err instanceof Error ? err.message : "Could not save the row.";
    } finally {
      editor.saving = false;
    }
  });

  const addSuggestion = $(async (suggestion: PlanSuggestion) => {
    if (busy.value !== null) {
      return;
    }

    busy.value = `add:${suggestion.key}`;
    notice.value = null;
    failure.value = null;

    try {
      await send("POST", planItemsUrl, {
        seriesKey: suggestion.key,
        name: plainName(suggestion.name) || null,
        direction: suggestion.direction,
      });

      notice.value = `${suggestion.name} is in your plan. It follows the amount and dates from your bank.`;
      await props.onChange$();
    } catch (err) {
      failure.value =
        err instanceof Error
          ? err.message
          : `Could not add ${suggestion.name}.`;
    } finally {
      busy.value = null;
    }
  });

  const remove = $(async (item: PlanItem) => {
    if (
      busy.value !== null ||
      !window.confirm(`Remove ${item.name} from your plan?`)
    ) {
      return;
    }

    busy.value = `remove:${item.id}`;
    notice.value = null;
    failure.value = null;

    try {
      await send("DELETE", `${planItemsUrl}/${item.id}`);

      notice.value = `${item.name} is no longer in your plan.`;
      await props.onChange$();
    } catch (err) {
      failure.value =
        err instanceof Error ? err.message : `Could not remove ${item.name}.`;
    } finally {
      busy.value = null;
    }
  });

  const kr = (amount: number) =>
    props.hidden ? maskedAmount : formatKr(amount);
  const cents = (amount: number) => Math.round(amount * 100) / 100;
  const plan = props.plan;
  const months = plan.months;
  const itemsById = new Map(plan.items.map((item) => [item.id, item]));
  const first = Math.max(0, Math.min(selected.value, months.length - 2));
  const shown = months.slice(first, first + 2);
  const currentIndex = months.findIndex((month) => month.current);
  const settled = (occurrence: PlanOccurrence) =>
    occurrence.status === "PAID" || occurrence.status === "PASSED";
  const sumOf = (list: PlanOccurrence[]) =>
    cents(list.reduce((sum, occurrence) => sum + occurrence.amount, 0));
  const groups: {
    direction: PlanDirection;
    title: string;
    items: PlanItem[];
  }[] = [
    {
      direction: "IN",
      title: "Money in",
      items: plan.items.filter((item) => item.direction === "IN"),
    },
    {
      direction: "OUT",
      title: "Money out",
      items: plan.items.filter((item) => item.direction === "OUT"),
    },
  ];
  const splitTotal = cents(
    editor.parts.reduce(
      (sum, part) => sum + (parseAmount(part.amount.trim()) ?? 0),
      0,
    ),
  );
  const splitBase =
    parseAmount(editor.amount.trim()) ??
    (editor.bank ? editor.bankAmount : null);
  const splitRest = splitBase == null ? 0 : cents(splitBase - splitTotal);
  const splitSummary =
    splitBase == null
      ? `The parts add up to ${formatKr(splitTotal)} kr.`
      : Math.abs(splitRest) < 0.005
        ? `The parts explain all ${formatKr(splitBase)} kr.`
        : splitRest > 0
          ? `The parts explain ${formatKr(splitTotal)} of ${formatKr(splitBase)} kr. ${formatKr(splitRest)} kr is unexplained.`
          : `The parts add up to ${formatKr(-splitRest)} kr more than ${formatKr(splitBase)} kr.`;

  const renderParts = (parts: PlanPart[], rest: number, label: string) => (
    <ul class="plan-parts" aria-label={label}>
      {parts.map((part) => (
        <li key={part.id}>
          <span>{part.name}</span>
          <span>{`${kr(part.amount)} kr`}</span>
        </li>
      ))}
      {Math.abs(rest) >= 0.005 && (
        <li class="plan-part-rest">
          <span>{restText(rest)}</span>
          <span>{`${kr(Math.abs(rest))} kr`}</span>
        </li>
      )}
    </ul>
  );

  const renderOccurrence = (
    occurrence: PlanOccurrence,
    position: number,
    current: boolean,
  ) => {
    const item = itemsById.get(occurrence.itemId);
    const parts = occurrence.direction === "OUT" ? (item?.parts ?? []) : [];

    return (
      <li
        key={`${occurrence.itemId}-${occurrence.date}-${position}`}
        class={{
          "plan-occurrence": true,
          "plan-settled": settled(occurrence),
          "plan-late": occurrence.status === "LATE",
        }}
      >
        <span class="plan-date">{formatDay(occurrence.date)}</span>
        <span class="plan-name">
          <strong title={occurrence.name}>{occurrence.name}</strong>
          {current && <small>{statusText(occurrence, item?.kind)}</small>}
        </span>
        <span
          class={{
            "plan-amount": true,
            "plan-amount-in": occurrence.direction === "IN",
          }}
        >
          {`${occurrence.direction === "IN" ? "+" : "−"} ${kr(occurrence.amount)} kr`}
        </span>
        {parts.length > 0 &&
          renderParts(
            parts,
            cents(occurrence.amount - (item?.partsTotal ?? 0)),
            `What ${occurrence.name} covers`,
          )}
      </li>
    );
  };

  const renderMonth = (month: PlanMonth, position: number) => {
    const title = formatMonth(month.month, "long");
    const incoming = month.occurrences.filter(
      (occurrence) => occurrence.direction === "IN",
    );
    const outgoing = month.occurrences.filter(
      (occurrence) => occurrence.direction === "OUT",
    );
    const badge = month.current
      ? "This month"
      : currentIndex >= 0 && position === currentIndex + 1
        ? "Next month"
        : null;

    return (
      <section key={month.month} class="plan-column" aria-label={title}>
        <div class="plan-column-head">
          <h3>{title}</h3>
          {badge && <span class="soft-badge">{badge}</span>}
        </div>

        <dl class="plan-totals">
          <div>
            <dt>Money in</dt>
            <dd>{`${kr(month.in)} kr`}</dd>
          </div>
          <div>
            <dt>Money out</dt>
            <dd>{`${kr(month.out)} kr`}</dd>
          </div>
          <div class="plan-totals-left">
            <dt>Left</dt>
            <dd class={{ "recurring-negative": month.left < 0 }}>
              {`${kr(month.left)} kr`}
            </dd>
          </div>
        </dl>

        {month.current && (
          <p class="recurring-hint">
            {`${kr(sumOf(incoming.filter(settled)))} kr received so far · ${kr(
              sumOf(outgoing.filter((occurrence) => !settled(occurrence))),
            )} kr still to pay`}
          </p>
        )}

        {month.occurrences.length === 0 && (
          <p class="recurring-hint">Nothing in your plan this month.</p>
        )}

        {incoming.length > 0 && (
          <>
            <h4 class="plan-column-title">Money in</h4>
            <ul class="plan-list" aria-label={`Money in, ${title}`}>
              {incoming.map((occurrence, index) =>
                renderOccurrence(occurrence, index, month.current),
              )}
            </ul>
          </>
        )}

        {outgoing.length > 0 && (
          <>
            <h4 class="plan-column-title">Money out</h4>
            <ul class="plan-list" aria-label={`Money out, ${title}`}>
              {outgoing.map((occurrence, index) =>
                renderOccurrence(occurrence, index, month.current),
              )}
            </ul>
          </>
        )}
      </section>
    );
  };

  const renderItem = (item: PlanItem) => {
    const icon =
      item.source === "BANK"
        ? (item.categoryIcon ?? "refresh")
        : item.direction === "IN"
          ? "income"
          : "receipt";
    const spread =
      item.amount != null &&
      item.state !== "ENDED" &&
      item.state !== "MISSING" &&
      Math.abs(item.monthly - item.amount) >= 0.01;

    return (
      <li
        key={item.id}
        class={{
          "recurring-row": true,
          "plan-row": true,
          "plan-row-off": item.state === "ENDED" || item.state === "MISSING",
        }}
      >
        <span
          class="recurring-icon"
          style={{
            "--recurring-color":
              item.categoryColor ??
              (item.direction === "IN" ? "#8fd19e" : "#8792a5"),
          }}
        >
          <Icon key={icon} name={icon} />
        </span>

        <span class="recurring-name">
          <strong title={item.name}>{item.name}</strong>
          <small>
            {[
              item.cadenceLabel || null,
              item.source === "BANK"
                ? item.customAmount != null || item.customCadence
                  ? "From your bank, changed by you"
                  : "From your bank"
                : "Your own",
              item.endDate && item.state !== "ENDED"
                ? `Until ${formatDay(item.endDate, true)}`
                : null,
            ]
              .filter(Boolean)
              .join(" · ")}
          </small>
        </span>

        <span class="recurring-amount">
          <strong>
            {item.amount == null ? "No amount" : `${kr(item.amount)} kr`}
          </strong>
          {spread && <small>{`${kr(item.monthly)} kr a month`}</small>}
        </span>

        <span
          class={`recurring-status plan-state plan-state-${item.state.toLowerCase()}`}
        >
          {stateText(item, props.today)}
        </span>

        <span class="plan-row-actions">
          <button
            type="button"
            class="cal-chip"
            disabled={busy.value !== null}
            title={`Change ${item.name}`}
            onClick$={() => openEdit(item)}
          >
            Change
          </button>
          <button
            type="button"
            class="cal-chip plan-remove"
            disabled={busy.value !== null}
            title={`Remove ${item.name} from your plan`}
            onClick$={() => remove(item)}
          >
            {busy.value === `remove:${item.id}` ? "Removing…" : "Remove"}
          </button>
        </span>

        {item.parts.length > 0 &&
          renderParts(
            item.parts,
            item.unexplained ?? 0,
            `What ${item.name} covers`,
          )}
      </li>
    );
  };

  const renderSuggestion = (suggestion: PlanSuggestion) => {
    const icon = suggestion.categoryIcon ?? "refresh";

    return (
      <li key={suggestion.key} class="recurring-row plan-suggestion">
        <span
          class="recurring-icon"
          style={{
            "--recurring-color": suggestion.categoryColor ?? "#8792a5",
          }}
        >
          <Icon key={icon} name={icon} />
        </span>

        <span class="recurring-name">
          <strong title={suggestion.name}>{suggestion.name}</strong>
          <small>
            {[
              suggestion.cadenceLabel,
              `Last ${formatDay(suggestion.last, true)}`,
              suggestion.accounts.join(", "),
            ]
              .filter(Boolean)
              .join(" · ")}
          </small>
        </span>

        <span class="recurring-amount">
          <strong>{`${kr(suggestion.amount)} kr`}</strong>
          {Math.abs(suggestion.monthly - suggestion.amount) >= 0.01 && (
            <small>{`${kr(suggestion.monthly)} kr a month`}</small>
          )}
        </span>

        <button
          type="button"
          class="cal-chip plan-add"
          disabled={busy.value !== null}
          title={`Add ${suggestion.name} to your plan`}
          onClick$={() => addSuggestion(suggestion)}
        >
          {busy.value === `add:${suggestion.key}` ? (
            "Adding…"
          ) : (
            <>
              <Icon name="plus" />
              Add
            </>
          )}
        </button>
      </li>
    );
  };

  return (
    <>
      <section
        class="overview-panel recurring-panel plan-panel"
        aria-labelledby="plan-title"
      >
        <div class="recurring-head plan-head">
          <div>
            <h2 id="plan-title">Monthly plan</h2>
            <p class="recurring-hint">
              What comes in, goes out and is left, from the rows in your plan.
            </p>
          </div>

          {shown.length > 0 && (
            <div class="plan-switcher">
              <button
                type="button"
                class="cal-chip plan-step"
                aria-label="Earlier months"
                disabled={first === 0}
                onClick$={() => {
                  selected.value = Math.max(0, first - 1);
                }}
              >
                <Icon name="chevronLeft" />
              </button>
              <span class="plan-month" aria-live="polite">
                {shown
                  .map((month) => formatMonth(month.month, "short"))
                  .join(" – ")}
              </span>
              <button
                type="button"
                class="cal-chip plan-step"
                aria-label="Later months"
                disabled={first >= months.length - 2}
                onClick$={() => {
                  selected.value = Math.min(months.length - 2, first + 1);
                }}
              >
                <Icon name="chevronRight" />
              </button>
            </div>
          )}
        </div>

        {shown.length > 0 && (
          <>
            <div class="plan-pair">
              {shown.map((month, offset) => renderMonth(month, first + offset))}
            </div>

            <div class="plan-year">
              <div class="recurring-table-wrap">
                <table class="recurring-table plan-table">
                  <thead>
                    <tr>
                      <th scope="col">Month</th>
                      <th scope="col">In</th>
                      <th scope="col">Out</th>
                      <th scope="col">Left</th>
                    </tr>
                  </thead>
                  <tbody>
                    {months.map((entry, position) => (
                      <tr
                        key={entry.month}
                        class={{
                          "recurring-current":
                            position === first || position === first + 1,
                        }}
                      >
                        <th scope="row">
                          <button
                            type="button"
                            class="plan-month-button"
                            aria-pressed={
                              position === first || position === first + 1
                            }
                            onClick$={() => {
                              selected.value = position;
                            }}
                          >
                            {formatMonth(entry.month, "short")}
                          </button>
                          {entry.current && <small>now</small>}
                        </th>
                        <td>{kr(entry.in)}</td>
                        <td>{kr(entry.out)}</td>
                        <td class={{ "recurring-negative": entry.left < 0 }}>
                          {kr(entry.left)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr>
                      <th scope="row">Average month</th>
                      <td>{kr(plan.average.in)}</td>
                      <td>{kr(plan.average.out)}</td>
                      <td
                        class={{ "recurring-negative": plan.average.left < 0 }}
                      >
                        {kr(plan.average.left)}
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </div>
              <p class="recurring-hint">
                Pick a month to show it next to the month after. The average
                spreads rows that come every few months or years over every
                month.
              </p>
            </div>
          </>
        )}
      </section>

      <section
        class="overview-panel recurring-panel"
        aria-labelledby="plan-rows"
      >
        <div class="recurring-head plan-rows-head">
          <h2 id="plan-rows">Rows in your plan</h2>
          <div class="plan-actions">
            <button
              type="button"
              class="cal-chip"
              onClick$={() => openAdd("IN")}
            >
              <Icon name="plus" />
              Money in
            </button>
            <button
              type="button"
              class="cal-chip"
              onClick$={() => openAdd("OUT")}
            >
              <Icon name="plus" />
              Money out
            </button>
          </div>
        </div>

        {notice.value && (
          <p class="plan-notice" role="status">
            <Icon name="check" />
            {notice.value}
          </p>
        )}

        {failure.value && (
          <p class="plan-error" role="alert">
            {failure.value}
          </p>
        )}

        {plan.items.length === 0 && (
          <p class="recurring-hint">
            Your plan is empty. Add your income and the costs you know about, or
            add payments Zenith found in your history.
          </p>
        )}

        {groups.map((group) =>
          group.items.length === 0 ? null : (
            <div key={group.direction} class="plan-group">
              <h3 class="plan-group-title">
                {group.title}
                <span>{`${kr(
                  group.items.reduce((sum, item) => sum + item.monthly, 0),
                )} kr a month`}</span>
              </h3>
              <ul class="recurring-list">{group.items.map(renderItem)}</ul>
            </div>
          ),
        )}
      </section>

      {plan.suggestions.length > 0 && (
        <section
          class="overview-panel recurring-panel"
          aria-labelledby="plan-suggestions"
        >
          <div class="recurring-head">
            <h2 id="plan-suggestions">Add from your history</h2>
            <span class="soft-badge">
              {count(plan.suggestions.length, "payment", "payments")}
            </span>
          </div>
          <p class="recurring-hint">
            Payments Zenith found that are not in your plan. An added payment
            follows your bank: the plan uses its usual amount and the date it
            last came, and you can change both.
          </p>

          {suggestionGroups.map((group) => {
            const list = plan.suggestions.filter(
              (suggestion) => suggestion.kind === group.kind,
            );

            return list.length === 0 ? null : (
              <div key={group.kind} class="plan-group">
                <h3 class="plan-group-title">{group.title}</h3>
                <ul class="recurring-list">{list.map(renderSuggestion)}</ul>
              </div>
            );
          })}
        </section>
      )}

      <dialog
        ref={dialog}
        class="plan-dialog"
        aria-labelledby="plan-dialog-title"
        onClose$={() => {
          editor.error = null;
        }}
      >
        <form class="plan-form" preventdefault:submit onSubmit$={save}>
          <div class="plan-dialog-head">
            <div>
              <h2 id="plan-dialog-title">{editor.title}</h2>
              <p>
                {editor.bank
                  ? "This row follows a payment from your bank. Leave a field empty to keep following it."
                  : "The plan counts this again from the date it last happened."}
              </p>
            </div>
            <button
              type="button"
              class="plan-close"
              aria-label="Close"
              onClick$={closeEditor}
            >
              <Icon name="close" />
            </button>
          </div>

          {!editor.bank && (
            <fieldset class="plan-section">
              <legend class="plan-label">Direction</legend>
              <div class="plan-choices">
                {(["IN", "OUT"] as const).map((direction) => (
                  <label
                    key={direction}
                    class={{
                      "plan-choice": true,
                      "plan-choice-selected": editor.direction === direction,
                    }}
                  >
                    <input
                      type="radio"
                      name="plan-direction"
                      value={direction}
                      checked={editor.direction === direction}
                      onChange$={() => {
                        editor.direction = direction;
                      }}
                    />
                    {direction === "IN" ? "Money in" : "Money out"}
                  </label>
                ))}
              </div>
            </fieldset>
          )}

          <label class="plan-field">
            <span class="plan-label">
              Name <small>optional</small>
            </span>
            <input
              class="plan-input"
              type="text"
              maxLength={maxNameLength}
              autoComplete="off"
              value={editor.name}
              placeholder={
                editor.bank
                  ? "The name from your bank"
                  : editor.direction === "IN"
                    ? "Salary, side job…"
                    : "Rent, insurance…"
              }
              onInput$={(_, element) => {
                editor.name = element.value;
              }}
            />
          </label>

          <label class="plan-field">
            <span class="plan-label">
              Amount {editor.bank && <small>optional</small>}
            </span>
            <span class="plan-money">
              <input
                class="plan-input"
                type="text"
                inputMode="decimal"
                autoComplete="off"
                required={!editor.bank}
                value={editor.amount}
                placeholder={
                  editor.bank && editor.bankAmount != null
                    ? formatKr(editor.bankAmount)
                    : "0,00"
                }
                onInput$={(_, element) => {
                  editor.amount = element.value;
                }}
              />
              <span>kr</span>
            </span>
            {editor.bank && (
              <span class="plan-hint">
                {editor.bankAmount != null
                  ? `Empty uses your bank's usual amount, ${formatKr(editor.bankAmount)} kr.`
                  : "Empty uses your bank's usual amount."}
              </span>
            )}
          </label>

          {editor.direction === "OUT" && (
            <fieldset class="plan-section plan-split">
              <legend class="plan-label">
                Split into parts <small>optional</small>
              </legend>
              <span class="plan-hint">
                Name what the payment covers. It still counts once, with its own
                amount.
              </span>
              {editor.parts.map((part) => (
                <div key={part.key} class="plan-part-edit">
                  <input
                    class="plan-input plan-part-name"
                    type="text"
                    maxLength={maxNameLength}
                    autoComplete="off"
                    aria-label="Part name"
                    placeholder="Rent, food…"
                    value={part.name}
                    onInput$={(_, element) => {
                      part.name = element.value;
                    }}
                  />
                  <span class="plan-money plan-part-money">
                    <input
                      class="plan-input"
                      type="text"
                      inputMode="decimal"
                      autoComplete="off"
                      aria-label={`Amount for ${part.name.trim() || "this part"}`}
                      placeholder="0,00"
                      value={part.amount}
                      onInput$={(_, element) => {
                        part.amount = element.value;
                      }}
                    />
                    <span>kr</span>
                  </span>
                  <button
                    type="button"
                    class="plan-part-remove"
                    aria-label={`Remove ${part.name.trim() || "this part"}`}
                    onClick$={() => removePart(part.key)}
                  >
                    <Icon name="close" />
                  </button>
                </div>
              ))}
              <button
                type="button"
                class="cal-chip plan-part-add"
                onClick$={addPart}
              >
                <Icon name="plus" />
                Add part
              </button>
              {editor.parts.length > 0 && (
                <p class="plan-summary">{splitSummary}</p>
              )}
            </fieldset>
          )}

          <fieldset class="plan-section">
            <legend class="plan-label">How often</legend>
            {editor.bank && (
              <div class="plan-choices">
                <label
                  class={{
                    "plan-choice": true,
                    "plan-choice-selected": editor.followBank,
                  }}
                >
                  <input
                    type="radio"
                    name="plan-cadence"
                    checked={editor.followBank}
                    onChange$={() => {
                      editor.followBank = true;
                    }}
                  />
                  {editor.bankCadence
                    ? `Like your bank: ${editor.bankCadence.toLowerCase()}`
                    : "Like your bank"}
                </label>
                <label
                  class={{
                    "plan-choice": true,
                    "plan-choice-selected": !editor.followBank,
                  }}
                >
                  <input
                    type="radio"
                    name="plan-cadence"
                    checked={!editor.followBank}
                    onChange$={() => {
                      editor.followBank = false;
                    }}
                  />
                  My own
                </label>
              </div>
            )}
            {(!editor.bank || !editor.followBank) && (
              <div class="plan-every">
                <span>Every</span>
                <input
                  class="plan-input plan-number"
                  type="number"
                  min={1}
                  max={120}
                  step={1}
                  required
                  aria-label="How many months or years"
                  value={editor.every}
                  onInput$={(_, element) => {
                    editor.every = element.value;
                  }}
                />
                <select
                  class="plan-input"
                  aria-label="Months or years"
                  onChange$={(_, element) => {
                    editor.unit = element.value === "YEAR" ? "YEAR" : "MONTH";
                  }}
                >
                  <option value="MONTH" selected={editor.unit === "MONTH"}>
                    {editor.every === "1" ? "month" : "months"}
                  </option>
                  <option value="YEAR" selected={editor.unit === "YEAR"}>
                    {editor.every === "1" ? "year" : "years"}
                  </option>
                </select>
              </div>
            )}
          </fieldset>

          {editor.bank ? (
            <p class="plan-summary">
              {editor.lastDate
                ? `Last payment ${formatDay(editor.lastDate, true)}, from your bank. The plan counts on from there.`
                : "Zenith cannot find this payment in your history right now, so it is not counted."}
            </p>
          ) : (
            <label class="plan-field">
              <span class="plan-label">Last happened</span>
              <input
                class="plan-input"
                type="date"
                required
                min="2000-01-01"
                value={editor.lastDate}
                onInput$={(_, element) => {
                  editor.lastDate = element.value;
                }}
              />
              <span class="plan-hint">
                Pick a later date if it has not started yet.
              </span>
            </label>
          )}

          <label class="plan-field">
            <span class="plan-label">
              Ends <small>optional</small>
            </span>
            <input
              class="plan-input"
              type="date"
              min={editor.bank ? "2000-01-01" : editor.lastDate || "2000-01-01"}
              value={editor.endDate}
              onInput$={(_, element) => {
                editor.endDate = element.value;
              }}
            />
            <span class="plan-hint">Leave it empty if it keeps going.</span>
          </label>

          {editor.error && (
            <p class="plan-error" role="alert">
              {editor.error}
            </p>
          )}

          <div class="plan-dialog-actions">
            <button type="button" class="plan-secondary" onClick$={closeEditor}>
              Cancel
            </button>
            <button type="submit" class="plan-primary" disabled={editor.saving}>
              {editor.saving
                ? "Saving…"
                : editor.mode === "add"
                  ? "Add to plan"
                  : "Save"}
            </button>
          </div>
        </form>
      </dialog>
    </>
  );
});
