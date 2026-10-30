import { component$, useSignal, useVisibleTask$ } from "@builder.io/qwik";

import type { AvanzaAccount, AvanzaHistoryPoint } from "../utils/avanza";

interface ChartProps {
  history: AvanzaHistoryPoint[];
  accounts: AvanzaAccount[];
  hidden: boolean;
}

interface Series {
  id: string;
  name: string;
  slot: number;
  values: (number | null)[];
}

const maskedAmount = "•• •••";
const maxSeries = 8;
const height = 260;
const margin = { top: 16, bottom: 30, left: 64 };

function formatKr(amount: number): string {
  return new Intl.NumberFormat("sv-SE", { maximumFractionDigits: 0 }).format(
    amount,
  );
}

function formatDay(time: number, withYear = false): string {
  return new Intl.DateTimeFormat("en-GB", {
    day: "numeric",
    month: "short",
    ...(withYear ? { year: "numeric" } : {}),
  }).format(new Date(time));
}

function niceStep(range: number, ticks: number): number {
  const raw = range / Math.max(1, ticks);
  const power = Math.pow(10, Math.floor(Math.log10(raw)));
  const unit = raw / power;

  return (unit <= 1 ? 1 : unit <= 2 ? 2 : unit <= 5 ? 5 : 10) * power;
}

function buildSeries(
  history: AvanzaHistoryPoint[],
  accounts: AvanzaAccount[],
  times: string[],
): Series[] {
  const slotOrder = [...new Set(history.map((point) => point.accountId))].sort(
    (a, b) => a.localeCompare(b),
  );
  const latest = new Map(
    accounts.map((account) => [account.accountId, account.totalValue]),
  );
  const lastValue = (id: string) =>
    latest.get(id) ??
    [...history].reverse().find((point) => point.accountId === id)
      ?.totalValue ??
    0;
  const ranked = [...slotOrder].sort((a, b) => lastValue(b) - lastValue(a));
  const kept =
    ranked.length > maxSeries ? ranked.slice(0, maxSeries - 1) : ranked;
  const folded = ranked.filter((id) => !kept.includes(id));
  const index = new Map(times.map((time, position) => [time, position]));
  const names = new Map(
    history.map((point) => [point.accountId, point.accountName]),
  );

  const series: Series[] = kept.map((id) => ({
    id,
    name: names.get(id) ?? id,
    slot: slotOrder.indexOf(id) % maxSeries,
    values: times.map(() => null),
  }));

  if (folded.length > 0) {
    series.push({
      id: "other",
      name: "Other accounts",
      slot: maxSeries - 1,
      values: times.map(() => null),
    });
  }

  for (const point of history) {
    const target =
      series.find((item) => item.id === point.accountId) ??
      (folded.includes(point.accountId) ? series[series.length - 1] : null);
    const position = index.get(point.fetchedAt);

    if (target && position !== undefined) {
      target.values[position] =
        (target.values[position] ?? 0) + point.totalValue;
    }
  }

  return series;
}

export const AvanzaChart = component$<ChartProps>((props) => {
  const container = useSignal<HTMLElement>();
  const width = useSignal(720);
  const hover = useSignal<number | null>(null);
  const mode = useSignal<"total" | "accounts">("total");

  useVisibleTask$(({ cleanup }) => {
    if (!container.value) {
      return;
    }

    const observer = new ResizeObserver((entries) => {
      width.value = Math.max(280, Math.round(entries[0].contentRect.width));
    });

    observer.observe(container.value);
    cleanup(() => observer.disconnect());
  });

  const times = [
    ...new Set(props.history.map((point) => point.fetchedAt)),
  ].sort((a, b) => Date.parse(a) - Date.parse(b));
  const stamps = times.map((time) => Date.parse(time));
  const accountSeries = buildSeries(props.history, props.accounts, times);
  const totalSeries: Series = {
    id: "total",
    name: "Total value",
    slot: 0,
    values: times.map((_, position) =>
      accountSeries.reduce(
        (sum, item) => sum + (item.values[position] ?? 0),
        0,
      ),
    ),
  };
  const series = mode.value === "total" ? [totalSeries] : accountSeries;
  const labelled =
    mode.value === "accounts" && series.length <= 4 && width.value >= 520;
  const right = labelled ? 132 : 20;
  const plotWidth = width.value - margin.left - right;
  const plotHeight = height - margin.top - margin.bottom;
  const values = series.flatMap((item) =>
    item.values.filter((value): value is number => value !== null),
  );
  const low = values.length > 0 ? Math.min(...values) : 0;
  const high = values.length > 0 ? Math.max(...values) : 1;
  const step = niceStep(Math.max(high - low, Math.abs(high) * 0.02, 1), 4);
  const yMin = Math.floor(low / step) * step;
  const yMax = Math.max(Math.ceil(high / step) * step, yMin + step);
  const first = stamps[0] ?? 0;
  const last = stamps[stamps.length - 1] ?? 0;
  const x = (stamp: number) =>
    margin.left +
    (last === first
      ? plotWidth / 2
      : ((stamp - first) / (last - first)) * plotWidth);
  const y = (value: number) =>
    margin.top + plotHeight - ((value - yMin) / (yMax - yMin)) * plotHeight;
  const yTicks: number[] = [];

  for (let tick = yMin; tick <= yMax + step / 2; tick += step) {
    yTicks.push(tick);
  }

  const xTickCount = Math.min(stamps.length, width.value < 520 ? 3 : 5);
  const xTicks =
    stamps.length <= 1
      ? stamps
      : Array.from(
          { length: xTickCount },
          (_, position) =>
            stamps[
              Math.round(
                (position / Math.max(1, xTickCount - 1)) * (stamps.length - 1),
              )
            ],
        ).filter((stamp, position, all) => all.indexOf(stamp) === position);
  const path = (item: Series) => {
    let d = "";
    let pen = false;

    item.values.forEach((value, position) => {
      if (value === null) {
        pen = false;
        return;
      }

      d += `${pen ? "L" : "M"}${x(stamps[position]).toFixed(1)} ${y(value).toFixed(1)}`;
      pen = true;
    });

    return d;
  };
  const endLabels = labelled
    ? series
        .map((item) => {
          const position = item.values
            .map((value) => value !== null)
            .lastIndexOf(true);

          return position < 0
            ? null
            : { item, at: y(item.values[position]!), position };
        })
        .filter(
          (label): label is { item: Series; at: number; position: number } =>
            label !== null,
        )
        .sort((a, b) => a.at - b.at)
    : [];

  for (let position = 1; position < endLabels.length; position++) {
    endLabels[position].at = Math.max(
      endLabels[position].at,
      endLabels[position - 1].at + 16,
    );
  }

  const active = hover.value;
  const money = (value: number | null) =>
    value === null
      ? "–"
      : props.hidden
        ? maskedAmount
        : `${formatKr(value)} kr`;

  return (
    <div class="avz-chart">
      <div class="avz-chart-controls" role="group" aria-label="Chart view">
        {(["total", "accounts"] as const).map((choice) => (
          <button
            key={choice}
            type="button"
            class={{ "cal-chip": true, "avz-chip-on": mode.value === choice }}
            aria-pressed={mode.value === choice}
            onClick$={() => {
              mode.value = choice;
              hover.value = null;
            }}
          >
            {choice === "total" ? "Total" : "Per account"}
          </button>
        ))}
      </div>

      {series.length > 1 && (
        <ul class="avz-legend" aria-label="Accounts in the chart">
          {series.map((item) => (
            <li key={item.id}>
              <span class={`avz-key avz-series-${item.slot + 1}`} />
              {item.name}
            </li>
          ))}
        </ul>
      )}

      <div
        ref={container}
        class="avz-plot"
        tabIndex={0}
        role="img"
        aria-label={`Value over time, ${times.length} fetches. Use the arrow keys to read the values.`}
        onPointerMove$={(event, element) => {
          const pointer = event.clientX - element.getBoundingClientRect().left;
          const span = last - first;
          let nearest = 0;
          let best = Number.POSITIVE_INFINITY;

          stamps.forEach((stamp, position) => {
            const distance = Math.abs(
              margin.left +
                (span === 0
                  ? plotWidth / 2
                  : ((stamp - first) / span) * plotWidth) -
                pointer,
            );

            if (distance < best) {
              best = distance;
              nearest = position;
            }
          });

          hover.value = stamps.length > 0 ? nearest : null;
        }}
        onPointerLeave$={() => {
          hover.value = null;
        }}
        onBlur$={() => {
          hover.value = null;
        }}
        onKeyDown$={(event) => {
          if (stamps.length === 0) {
            return;
          }

          if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
            const current = hover.value ?? stamps.length - 1;

            hover.value = Math.min(
              stamps.length - 1,
              Math.max(0, current + (event.key === "ArrowLeft" ? -1 : 1)),
            );
          }
        }}
      >
        <svg
          width={width.value}
          height={height}
          viewBox={`0 0 ${width.value} ${height}`}
          aria-hidden="true"
        >
          {yTicks.map((tick) => (
            <g key={tick}>
              <line
                class="avz-grid"
                x1={margin.left}
                x2={margin.left + plotWidth}
                y1={y(tick)}
                y2={y(tick)}
              />
              <text
                class="avz-axis"
                x={margin.left - 8}
                y={y(tick) + 4}
                text-anchor="end"
              >
                {props.hidden ? "••" : formatKr(tick)}
              </text>
            </g>
          ))}

          {xTicks.map((stamp) => (
            <text
              key={stamp}
              class="avz-axis"
              x={x(stamp)}
              y={height - 8}
              text-anchor="middle"
            >
              {formatDay(stamp)}
            </text>
          ))}

          {series.map((item) => (
            <path
              key={item.id}
              class={`avz-line avz-series-${item.slot + 1}`}
              d={path(item)}
            />
          ))}

          {series.map((item) => {
            const position = item.values
              .map((value) => value !== null)
              .lastIndexOf(true);

            return position < 0 ? null : (
              <circle
                key={`end-${item.id}`}
                class={`avz-dot avz-series-${item.slot + 1}`}
                cx={x(stamps[position])}
                cy={y(item.values[position]!)}
                r={4}
              />
            );
          })}

          {endLabels.map((label) => (
            <text
              key={`label-${label.item.id}`}
              class="avz-end-label"
              x={margin.left + plotWidth + 10}
              y={label.at + 4}
            >
              {label.item.name.length > 18
                ? `${label.item.name.slice(0, 17)}…`
                : label.item.name}
            </text>
          ))}

          {active !== null && stamps[active] !== undefined && (
            <g>
              <line
                class="avz-crosshair"
                x1={x(stamps[active])}
                x2={x(stamps[active])}
                y1={margin.top}
                y2={margin.top + plotHeight}
              />
              {series.map((item) =>
                item.values[active] === null ? null : (
                  <circle
                    key={`hover-${item.id}`}
                    class={`avz-dot avz-series-${item.slot + 1}`}
                    cx={x(stamps[active])}
                    cy={y(item.values[active]!)}
                    r={5}
                  />
                ),
              )}
            </g>
          )}
        </svg>

        {active !== null && stamps[active] !== undefined && (
          <div
            class="avz-tooltip"
            style={{
              left: `${Math.min(Math.max(x(stamps[active]) + 12, 8), width.value - 200)}px`,
            }}
          >
            <span class="avz-tooltip-date">
              {formatDay(stamps[active], true)}
            </span>
            {series.map((item) => (
              <span key={item.id} class="avz-tooltip-row">
                <span class={`avz-key avz-series-${item.slot + 1}`} />
                <strong>{money(item.values[active])}</strong>
                <small>{item.name}</small>
              </span>
            ))}
          </div>
        )}
      </div>

      {times.length === 1 && (
        <p class="avz-hint">
          The chart fills in as new values arrive on Monday and Thursday
          mornings.
        </p>
      )}

      <details class="avz-table-view">
        <summary>Show the values as a table</summary>
        <div class="avz-table-wrap">
          <table class="avz-table">
            <thead>
              <tr>
                <th scope="col">Fetched</th>
                {accountSeries.map((item) => (
                  <th key={item.id} scope="col">
                    {item.name}
                  </th>
                ))}
                <th scope="col">Total</th>
              </tr>
            </thead>
            <tbody>
              {[...times.keys()].reverse().map((position) => (
                <tr key={times[position]}>
                  <th scope="row">{formatDay(stamps[position], true)}</th>
                  {accountSeries.map((item) => (
                    <td key={item.id}>{money(item.values[position])}</td>
                  ))}
                  <td>{money(totalSeries.values[position])}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
});
