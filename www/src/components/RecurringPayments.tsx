import { $, component$, useSignal, useVisibleTask$ } from "@builder.io/qwik";

import { Icon } from "./Icon";
import { RecurringPlan } from "./RecurringPlan";
import {
  count,
  formatDay,
  formatKr,
  formatMonth,
  maskedAmount,
  type Plan,
} from "../utils/recurring";

type Kind = "INCOME" | "COST" | "SAVING" | "TRANSFER";
type Status = "PAID" | "DUE" | "LATE" | "LATER" | "ENDED";

interface RecurringSeries {
  key: string;
  latestTransactionId?: number | null;
  name: string;
  kind: Kind;
  direction: string;
  type: string;
  subtype: string;
  category?: string | null;
  categoryIcon?: string | null;
  categoryColor?: string | null;
  accounts: string[];
  frequency: string;
  frequencyLabel: string;
  manual: boolean;
  occurrences: number;
  first: string;
  last: string;
  lastAmount: number;
  next: string;
  typicalAmount: number;
  typicalCounted: number;
  minAmount: number;
  maxAmount: number;
  monthly: number;
  status: Status;
  thisMonth: number;
  currency: string;
}

interface MonthTotals {
  month: string;
  current: boolean;
  moneyIn: number;
  moneyOut: number;
  recurringIncome: number;
  recurringCosts: number;
  saved: number;
  otherSpending: number;
  leftAfterRecurring: number;
  left: number;
  dueIncome: number;
  dueCosts: number;
  dueSavings: number;
}

interface Summary {
  income: number;
  incomeSource: string;
  expectedSalary?: number | null;
  recurringIncome: number;
  costs: number;
  savings: number;
  leftAfterCosts: number;
  leftAfterSavings: number;
}

interface Overview {
  today: string;
  currency: string;
  typical: Summary;
  months: MonthTotals[];
  series: RecurringSeries[];
  plan?: Plan | null;
}

const sections: { kind: Kind; title: string; note?: string }[] = [
  { kind: "COST", title: "Recurring costs" },
  { kind: "INCOME", title: "Recurring income" },
  { kind: "SAVING", title: "Saved every month" },
  {
    kind: "TRANSFER",
    title: "Card bills and transfers",
    note: "These move money between accounts or pay a card bill, so they are not counted as costs. Card purchases count on the day you make them.",
  },
];

function typicalOf(series: RecurringSeries): number {
  return Math.abs(
    series.kind === "INCOME" || series.kind === "COST"
      ? series.typicalCounted
      : series.typicalAmount,
  );
}

function statusText(series: RecurringSeries): string {
  switch (series.status) {
    case "PAID":
      return `${
        series.kind === "INCOME"
          ? "Received"
          : series.kind === "SAVING"
            ? "Saved"
            : "Paid"
      } ${formatDay(series.last)}`;
    case "DUE":
      return `Due ${formatDay(series.next)}`;
    case "LATE":
      return `Late, expected ${formatDay(series.next)}`;
    case "LATER":
      return `Next ${formatDay(series.next)}`;
    default:
      return `Last ${formatDay(series.last, true)}`;
  }
}

export const RecurringPayments = component$(() => {
  const overview = useSignal<Overview | null>(null);
  const loading = useSignal(true);
  const error = useSignal<string | null>(null);
  const hidden = useSignal(false);
  const notice = useSignal<string | null>(null);
  const removing = useSignal<string | null>(null);

  const load = $(async () => {
    error.value = null;

    try {
      const response = await fetch("/api/recurring", {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
      });

      const payload = (await response.json().catch(() => null)) as
        (Overview & { message?: string }) | null;

      if (!response.ok || !payload || !Array.isArray(payload.series)) {
        throw new Error(
          payload?.message ??
            `Could not load your recurring payments (${response.status})`,
        );
      }

      overview.value = payload;
    } catch (err) {
      error.value =
        err instanceof Error
          ? err.message
          : "Could not load your recurring payments.";
    } finally {
      loading.value = false;
    }
  });

  const notRecurring = $(async (series: RecurringSeries) => {
    if (series.latestTransactionId == null || removing.value !== null) {
      return;
    }

    removing.value = series.key;
    notice.value = null;
    error.value = null;

    try {
      const response = await fetch(
        `/api/transactions/${series.latestTransactionId}/recurring`,
        {
          method: "PUT",
          headers: {
            Accept: "application/json",
            "Content-Type": "application/json",
          },
          credentials: "same-origin",
          body: JSON.stringify({ recurring: false }),
        },
      );

      if (!response.ok) {
        const payload = (await response.json().catch(() => null)) as {
          message?: string;
        } | null;

        throw new Error(
          payload?.message ??
            `Could not change ${series.name} (${response.status})`,
        );
      }

      notice.value = `${series.name} is no longer counted as recurring. You can mark it again from the calendar.`;
      await load();
    } catch (err) {
      error.value =
        err instanceof Error ? err.message : `Could not change ${series.name}.`;
    } finally {
      removing.value = null;
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
  const data = overview.value;
  const active =
    data?.series.filter((series) => series.status !== "ENDED") ?? [];
  const ended =
    data?.series.filter((series) => series.status === "ENDED") ?? [];
  const current = data?.months.find((month) => month.current);
  const costCount = active.filter((series) => series.kind === "COST").length;
  const savingCount = active.filter(
    (series) => series.kind === "SAVING",
  ).length;
  const dueCostCount = active.filter(
    (series) =>
      series.kind === "COST" &&
      (series.status === "DUE" || series.status === "LATE"),
  ).length;
  const typical = data?.typical;
  const barBase = typical && typical.income > 0 ? typical.income : 0;
  const share = (amount: number) =>
    barBase > 0
      ? `${Math.max(0, Math.min(100, (amount / barBase) * 100))}%`
      : "0%";
  const forecast = current
    ? current.moneyIn +
      current.dueIncome -
      current.recurringCosts -
      current.dueCosts
    : 0;

  const renderRow = (series: RecurringSeries, actions: boolean) => {
    const typicalAmount = typicalOf(series);
    const variable = series.maxAmount - series.minAmount >= 0.01;
    const details = [
      series.frequency === "MONTHLY"
        ? null
        : `${kr(series.monthly)} kr a month`,
      variable
        ? `Varies ${kr(series.minAmount)}–${kr(series.maxAmount)} kr`
        : null,
    ].filter(Boolean);

    return (
      <li key={series.key} class="recurring-row">
        <span
          class="recurring-icon"
          style={{ "--recurring-color": series.categoryColor ?? "#8792a5" }}
        >
          <Icon
            key={series.categoryIcon ?? "refresh"}
            name={series.categoryIcon ?? "refresh"}
          />
        </span>

        <span class="recurring-name">
          <strong title={series.name}>{series.name}</strong>
          <small>
            {[
              series.category,
              series.accounts.join(", "),
              `${series.frequencyLabel}${series.manual ? " (marked by you)" : ""}`,
            ]
              .filter(Boolean)
              .join(" · ")}
          </small>
        </span>

        <span class="recurring-amount">
          <strong>{`${kr(typicalAmount)} kr`}</strong>
          {details.length > 0 && <small>{details.join(" · ")}</small>}
        </span>

        <span
          class={`recurring-status recurring-status-${series.status.toLowerCase()}`}
        >
          {statusText(series)}
        </span>

        {actions && series.latestTransactionId != null && (
          <button
            type="button"
            class="cal-chip recurring-remove"
            disabled={removing.value !== null}
            title={`Stop counting ${series.name} as recurring`}
            onClick$={() => notRecurring(series)}
          >
            {removing.value === series.key ? "Removing…" : "Not recurring"}
          </button>
        )}
      </li>
    );
  };

  return (
    <main id="main" class="page-shell cal-shell recurring-shell">
      <section class="page-heading" aria-labelledby="page-title">
        <div>
          <h1 id="page-title">
            Recurring<span>.</span>
          </h1>
          <p class="page-subtitle">
            Plan each month with your own income and costs, next to the payments
            that come back in your history.
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
        <div class="cal-alert recurring-notice" role="status">
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
            {!data && (
              <button
                type="button"
                class="cal-chip recurring-retry"
                onClick$={load}
              >
                Try again
              </button>
            )}
          </div>
        </div>
      )}

      {loading.value && !data && (
        <section class="overview-panel recurring-panel">
          <p class="recurring-hint">Looking through your history…</p>
        </section>
      )}

      {data && typical && (
        <>
          {data.plan && (
            <RecurringPlan
              plan={data.plan}
              today={data.today}
              hidden={hidden.value}
              onChange$={load}
            />
          )}

          <div class="recurring-top">
            <section
              class="overview-panel recurring-panel"
              aria-labelledby="recurring-typical"
            >
              <div class="recurring-head">
                <h2 id="recurring-typical">A typical month</h2>
                <span class="soft-badge">
                  {count(costCount, "payment", "payments")}
                </span>
              </div>

              <dl class="recurring-flow">
                <div class="recurring-line">
                  <dt>
                    Income
                    <small>
                      {typical.incomeSource === "EXPECTED_SALARY"
                        ? typical.recurringIncome > 0
                          ? `Your expected salary. Recurring income has been about ${kr(typical.recurringIncome)} kr.`
                          : "Your expected salary"
                        : typical.income > 0
                          ? "Typical recurring income"
                          : "No recurring income found yet"}
                    </small>
                  </dt>
                  <dd>{`${kr(typical.income)} kr`}</dd>
                </div>

                <div class="recurring-line">
                  <dt>
                    Recurring costs
                    <small>{count(costCount, "payment", "payments")}</small>
                  </dt>
                  <dd>{`− ${kr(typical.costs)} kr`}</dd>
                </div>

                <div class="recurring-line recurring-total">
                  <dt>Left after recurring costs</dt>
                  <dd
                    class={{
                      "recurring-negative": typical.leftAfterCosts < 0,
                    }}
                  >{`${kr(typical.leftAfterCosts)} kr`}</dd>
                </div>

                <div class="recurring-line">
                  <dt>
                    Saved every month
                    <small>{count(savingCount, "transfer", "transfers")}</small>
                  </dt>
                  <dd>{`− ${kr(typical.savings)} kr`}</dd>
                </div>

                <div class="recurring-line recurring-total recurring-final">
                  <dt>Left after saving</dt>
                  <dd
                    class={{
                      "recurring-negative": typical.leftAfterSavings < 0,
                    }}
                  >{`${kr(typical.leftAfterSavings)} kr`}</dd>
                </div>
              </dl>

              {barBase > 0 && (
                <>
                  <div class="recurring-bar" aria-hidden="true">
                    <span
                      class="recurring-bar-costs"
                      style={{ width: share(typical.costs) }}
                    />
                    <span
                      class="recurring-bar-savings"
                      style={{ width: share(typical.savings) }}
                    />
                    <span
                      class="recurring-bar-left"
                      style={{ width: share(typical.leftAfterSavings) }}
                    />
                  </div>
                  <p class="recurring-legend">
                    <span class="recurring-key recurring-key-costs">Costs</span>
                    <span class="recurring-key recurring-key-savings">
                      Saved
                    </span>
                    <span class="recurring-key recurring-key-left">
                      Left for everything else
                    </span>
                  </p>
                </>
              )}

              {typical.incomeSource !== "EXPECTED_SALARY" && (
                <p class="recurring-hint">
                  Set your expected salary in the{" "}
                  <a href="/calendar">calendar</a> to plan with it instead.
                </p>
              )}
            </section>

            {current && (
              <section
                class="overview-panel recurring-panel"
                aria-labelledby="recurring-current"
              >
                <div class="recurring-head">
                  <h2 id="recurring-current">
                    {formatMonth(current.month, "long")}
                  </h2>
                  <span class="soft-badge">So far</span>
                </div>

                <dl class="recurring-figures">
                  <div>
                    <dt>Recurring costs paid</dt>
                    <dd>{`${kr(current.recurringCosts)} kr`}</dd>
                    <small>
                      {dueCostCount > 0
                        ? `${kr(current.dueCosts)} kr still to pay (${count(dueCostCount, "payment", "payments")})`
                        : "Nothing more due this month"}
                    </small>
                  </div>
                  <div>
                    <dt>Money in</dt>
                    <dd>{`${kr(current.moneyIn)} kr`}</dd>
                    <small>
                      {current.dueIncome > 0
                        ? `${kr(current.dueIncome)} kr more expected`
                        : `${kr(current.recurringIncome)} kr of it recurring`}
                    </small>
                  </div>
                  <div>
                    <dt>Left after recurring costs</dt>
                    <dd
                      class={{
                        "recurring-negative": current.leftAfterRecurring < 0,
                      }}
                    >{`${kr(current.leftAfterRecurring)} kr`}</dd>
                    <small>
                      {Math.abs(forecast - current.leftAfterRecurring) >= 0.01
                        ? `${kr(forecast)} kr once the rest is paid`
                        : "Everything due is paid"}
                    </small>
                  </div>
                  <div>
                    <dt>Left after all spending</dt>
                    <dd class={{ "recurring-negative": current.left < 0 }}>
                      {`${kr(current.left)} kr`}
                    </dd>
                    <small>The calendar's net for the month</small>
                  </div>
                </dl>
              </section>
            )}
          </div>

          <section
            class="overview-panel recurring-panel"
            aria-labelledby="recurring-months"
          >
            <div class="recurring-head">
              <h2 id="recurring-months">Each month</h2>
              <span class="soft-badge">Last 12 months</span>
            </div>

            <div class="recurring-table-wrap">
              <table class="recurring-table">
                <thead>
                  <tr>
                    <th scope="col">Month</th>
                    <th scope="col">Money in</th>
                    <th scope="col">Recurring costs</th>
                    <th scope="col">Left after recurring</th>
                    <th scope="col" class="recurring-optional">
                      Other spending
                    </th>
                    <th scope="col">Left</th>
                  </tr>
                </thead>
                <tbody>
                  {[...data.months].reverse().map((month) => (
                    <tr
                      key={month.month}
                      class={{ "recurring-current": month.current }}
                    >
                      <th scope="row">
                        {formatMonth(month.month, "short")}
                        {month.current && <small>so far</small>}
                      </th>
                      <td>{kr(month.moneyIn)}</td>
                      <td>{kr(month.recurringCosts)}</td>
                      <td
                        class={{
                          "recurring-negative": month.leftAfterRecurring < 0,
                        }}
                      >
                        {kr(month.leftAfterRecurring)}
                      </td>
                      <td class="recurring-optional">
                        {kr(month.otherSpending)}
                      </td>
                      <td class={{ "recurring-negative": month.left < 0 }}>
                        {kr(month.left)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <p class="recurring-hint">
              Left after recurring is money in minus recurring costs. Left also
              takes off everything else you spent, the same as the calendar.
            </p>
          </section>

          {sections.map((section) => {
            const items = active.filter(
              (series) => series.kind === section.kind,
            );

            return items.length === 0 ? null : (
              <section
                key={section.kind}
                class="overview-panel recurring-panel"
                aria-labelledby={`recurring-${section.kind.toLowerCase()}`}
              >
                <div class="recurring-head">
                  <h2 id={`recurring-${section.kind.toLowerCase()}`}>
                    {section.title}
                  </h2>
                  {section.kind !== "TRANSFER" && (
                    <span class="soft-badge">{`${kr(
                      items.reduce((sum, series) => sum + series.monthly, 0),
                    )} kr a month`}</span>
                  )}
                </div>
                {section.note && <p class="recurring-hint">{section.note}</p>}
                <ul class="recurring-list">
                  {items.map((series) => renderRow(series, true))}
                </ul>
              </section>
            );
          })}

          {active.length === 0 && (
            <section class="overview-panel recurring-panel">
              <p class="recurring-hint">
                No recurring payments yet. Zenith needs three payments with a
                steady rhythm, or you can mark a payment as recurring in the
                calendar.
              </p>
            </section>
          )}

          {ended.length > 0 && (
            <details class="overview-panel recurring-panel recurring-ended">
              <summary>
                <span>{`Ended (${ended.length})`}</span>
                <small>Not counted any more</small>
              </summary>
              <ul class="recurring-list">
                {ended.map((series) => renderRow(series, false))}
              </ul>
            </details>
          )}
        </>
      )}
    </main>
  );
});
