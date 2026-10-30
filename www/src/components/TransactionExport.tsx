import {
  $,
  component$,
  useSignal,
  useStore,
  useStyles$,
} from "@builder.io/qwik";

import { csvFormats } from "./ExportMenu";
import { Icon } from "./Icon";
import { downloadFile } from "../utils/download";

type Unit = "days" | "months" | "years";

interface ExportProps {
  year: number;
  month: number;
}

interface ExportState {
  unit: Unit;
  fromDay: string;
  toDay: string;
  fromMonth: string;
  toMonth: string;
  fromYear: string;
  toYear: string;
  format: string;
  exporting: boolean;
  error: string | null;
}

const units: { value: Unit; label: string }[] = [
  { value: "days", label: "Days" },
  { value: "months", label: "Months" },
  { value: "years", label: "Years" },
];

const formatKey = "zenith-export-format";

function pad(value: number): string {
  return String(value).padStart(2, "0");
}

function lastDayOf(year: number, month: number): number {
  return new Date(year, month, 0).getDate();
}

function monthKey(year: number, month: number): string {
  return `${year}-${pad(month)}`;
}

function rangeOf(state: ExportState): { from: string; to: string } | null {
  if (state.unit === "days") {
    return state.fromDay && state.toDay
      ? { from: state.fromDay, to: state.toDay }
      : null;
  }

  if (state.unit === "months") {
    const [toYear, toMonth] = state.toMonth.split("-").map(Number);

    return state.fromMonth && toYear && toMonth
      ? {
          from: `${state.fromMonth}-01`,
          to: `${state.toMonth}-${pad(lastDayOf(toYear, toMonth))}`,
        }
      : null;
  }

  return state.fromYear && state.toYear
    ? { from: `${state.fromYear}-01-01`, to: `${state.toYear}-12-31` }
    : null;
}

function describe(range: { from: string; to: string }): string {
  const format = (date: string) =>
    new Intl.DateTimeFormat("en-GB", {
      day: "numeric",
      month: "short",
      year: "numeric",
    }).format(new Date(`${date}T12:00:00`));
  const days =
    Math.round(
      (Date.parse(`${range.to}T12:00:00`) -
        Date.parse(`${range.from}T12:00:00`)) /
        86_400_000,
    ) + 1;

  return `${format(range.from)} – ${format(range.to)} · ${days} ${
    days === 1 ? "day" : "days"
  }`;
}

function readFormat(): string {
  try {
    const stored = window.localStorage.getItem(formatKey);

    return csvFormats.some((format) => format.value === stored)
      ? stored!
      : csvFormats[0].value;
  } catch {
    return csvFormats[0].value;
  }
}

function saveFormat(format: string): void {
  try {
    window.localStorage.setItem(formatKey, format);
  } catch {
    return;
  }
}

export const TransactionExport = component$<ExportProps>((props) => {
  useStyles$(`
    .xp-dialog {
      width: min(520px, calc(100vw - 24px));
      max-width: none;
      max-height: calc(100dvh - 24px);
      margin: auto;
      padding: 0;
      border: 1px solid #2b3240;
      border-radius: 20px;
      background: #161b25;
      color: #f1f2f5;
      box-shadow: 0 30px 90px #000000a6;
      font-size: 15px;
      line-height: 1.5;
      overflow-y: auto;
      overscroll-behavior: contain;
    }
    .xp-dialog, .xp-dialog * { box-sizing: border-box; }
    .xp-dialog::backdrop {
      background: #05080db3;
      backdrop-filter: blur(3px);
      -webkit-backdrop-filter: blur(3px);
    }
    html:has(.xp-dialog[open]) { overflow: hidden; }
    .xp-form { padding: 24px 26px 22px; }
    .xp-head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 22px;
    }
    .xp-head h2 {
      margin: 0;
      font-family: 'Manrope', sans-serif;
      font-size: 1.25rem;
      font-weight: 750;
      letter-spacing: -0.4px;
    }
    .xp-head p { margin: 4px 0 0; color: #939bad; font-size: 0.8125rem; }
    .xp-close {
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
    .xp-close:hover { background: #1d2330; color: #f1f2f5; }
    .xp-close svg { width: 18px; height: 18px; }
    .xp-section { display: flex; flex-direction: column; gap: 8px; margin: 0 0 18px; padding: 0; border: 0; }
    .xp-label { padding: 0; font-size: 0.8125rem; font-weight: 500; color: #ccd1da; }
    .xp-choices { display: flex; flex-wrap: wrap; gap: 8px; }
    .xp-choice {
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
    .xp-choice input { margin: 0; accent-color: #c1d8b6; }
    .xp-choice:focus-within { outline: 2px solid #c1d8b6; outline-offset: 2px; }
    .xp-choice-selected { border-color: #c1d8b6; color: #f1f2f5; }
    .xp-range { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
    .xp-field { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
    .xp-input {
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
    .xp-hint { margin: 0; font-size: 0.75rem; line-height: 1.5; color: #939bad; }
    .xp-summary {
      margin: 0;
      padding: 10px 13px;
      border: 1px solid #262d3a;
      border-radius: 9px;
      background: #131821;
      font-size: 0.8125rem;
      color: #ccd1da;
    }
    .xp-error {
      margin: 16px 0 0;
      padding: 10px 13px;
      border: 1px solid #6b3434;
      border-radius: 9px;
      background: #2c1d20;
      font-size: 0.8125rem;
      color: #f3c1bd;
    }
    .xp-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 22px; }
    .xp-secondary, .xp-primary {
      padding: 9px 18px;
      border-radius: 999px;
      font-size: 0.875rem;
      font-weight: 600;
    }
    .xp-secondary { border: 1px solid #3a4352; background: transparent; color: #ccd1da; }
    .xp-secondary:hover { border-color: #4a5466; background: #232a37; color: #f1f2f5; }
    .xp-primary { border: 1px solid #c1d8b6; background: #c1d8b6; color: #11151d; }
    .xp-primary:hover:not(:disabled) { border-color: #d3e6c9; background: #d3e6c9; }
    .xp-primary:disabled { opacity: 0.6; }
    .xp-dialog :focus-visible { outline: 2px solid #c1d8b6; outline-offset: 2px; }
    @media (max-width: 560px) {
      .xp-dialog { width: calc(100vw - 16px); max-height: calc(100dvh - 16px); border-radius: 18px; }
      .xp-form { padding: 20px 16px 18px; }
      .xp-range { grid-template-columns: minmax(0, 1fr); }
      .xp-actions > * { flex: 1; }
    }
  `);

  const dialog = useSignal<HTMLDialogElement>();
  const state = useStore<ExportState>({
    unit: "months",
    fromDay: "",
    toDay: "",
    fromMonth: "",
    toMonth: "",
    fromYear: "",
    toYear: "",
    format: csvFormats[0].value,
    exporting: false,
    error: null,
  });

  const thisYear = new Date().getFullYear();
  const firstYear = Math.min(thisYear - 5, props.year);
  const lastYear = Math.max(thisYear, props.year);
  const years: string[] = [];
  const months: { value: string; label: string }[] = [];

  for (let year = lastYear; year >= firstYear; year--) {
    years.push(String(year));

    for (let month = 12; month >= 1; month--) {
      months.push({
        value: monthKey(year, month),
        label: new Intl.DateTimeFormat("en-GB", {
          month: "long",
          year: "numeric",
        }).format(new Date(year, month - 1, 1)),
      });
    }
  }

  const open = $(() => {
    const viewed = monthKey(props.year, props.month);

    state.fromDay = `${viewed}-01`;
    state.toDay = `${viewed}-${pad(lastDayOf(props.year, props.month))}`;
    state.fromMonth = viewed;
    state.toMonth = viewed;
    state.fromYear = String(props.year);
    state.toYear = String(props.year);
    state.format = readFormat();
    state.error = null;

    if (dialog.value && !dialog.value.open) {
      dialog.value.showModal();
    }
  });

  const close = $(() => {
    dialog.value?.close();
  });

  const download = $(async () => {
    const range = rangeOf(state);

    if (!range) {
      state.error = "Choose both a start and an end.";
      return;
    }

    if (range.from > range.to) {
      state.error = "The start must come before the end.";
      return;
    }

    state.exporting = true;
    state.error = null;
    saveFormat(state.format);

    try {
      await downloadFile(
        `/api/exports/transactions?${new URLSearchParams({
          from: range.from,
          to: range.to,
          format: state.format,
        })}`,
        "zenith-transactions.csv",
      );
      await close();
    } catch (err) {
      state.error =
        err instanceof Error ? err.message : "Could not export transactions.";
    } finally {
      state.exporting = false;
    }
  });

  const range = rangeOf(state);
  const selectedFormat = csvFormats.find(
    (format) => format.value === state.format,
  );

  return (
    <>
      <button
        type="button"
        class="visibility-button"
        title="Export transactions as a CSV file"
        onClick$={open}
      >
        <Icon name="download" />
        Export CSV
      </button>

      <dialog
        ref={dialog}
        class="xp-dialog"
        aria-labelledby="xp-title"
        onClose$={() => {
          state.error = null;
        }}
      >
        <form class="xp-form" preventdefault:submit onSubmit$={download}>
          <div class="xp-head">
            <div>
              <h2 id="xp-title">Export transactions</h2>
              <p>
                Every transaction in the period, with its category, type and
                what it counts as.
              </p>
            </div>
            <button
              type="button"
              class="xp-close"
              aria-label="Close"
              onClick$={close}
            >
              <Icon name="close" />
            </button>
          </div>

          <fieldset class="xp-section">
            <legend class="xp-label">Period</legend>
            <div class="xp-choices">
              {units.map((unit) => (
                <label
                  key={unit.value}
                  class={{
                    "xp-choice": true,
                    "xp-choice-selected": state.unit === unit.value,
                  }}
                >
                  <input
                    type="radio"
                    name="xp-unit"
                    value={unit.value}
                    checked={state.unit === unit.value}
                    onChange$={() => {
                      state.unit = unit.value;
                      state.error = null;
                    }}
                  />
                  {unit.label}
                </label>
              ))}
            </div>
          </fieldset>

          <div class="xp-section">
            <div class="xp-range">
              {state.unit === "days" && (
                <>
                  <label class="xp-field">
                    <span class="xp-label">From</span>
                    <input
                      class="xp-input"
                      type="date"
                      required
                      value={state.fromDay}
                      onInput$={(_, element) => {
                        state.fromDay = element.value;
                      }}
                    />
                  </label>
                  <label class="xp-field">
                    <span class="xp-label">To</span>
                    <input
                      class="xp-input"
                      type="date"
                      required
                      value={state.toDay}
                      onInput$={(_, element) => {
                        state.toDay = element.value;
                      }}
                    />
                  </label>
                </>
              )}

              {state.unit === "months" && (
                <>
                  <label class="xp-field">
                    <span class="xp-label">From</span>
                    <select
                      class="xp-input"
                      onChange$={(_, element) => {
                        state.fromMonth = element.value;
                      }}
                    >
                      {months.map((month) => (
                        <option
                          key={month.value}
                          value={month.value}
                          selected={state.fromMonth === month.value}
                        >
                          {month.label}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label class="xp-field">
                    <span class="xp-label">To</span>
                    <select
                      class="xp-input"
                      onChange$={(_, element) => {
                        state.toMonth = element.value;
                      }}
                    >
                      {months.map((month) => (
                        <option
                          key={month.value}
                          value={month.value}
                          selected={state.toMonth === month.value}
                        >
                          {month.label}
                        </option>
                      ))}
                    </select>
                  </label>
                </>
              )}

              {state.unit === "years" && (
                <>
                  <label class="xp-field">
                    <span class="xp-label">From</span>
                    <select
                      class="xp-input"
                      onChange$={(_, element) => {
                        state.fromYear = element.value;
                      }}
                    >
                      {years.map((year) => (
                        <option
                          key={year}
                          value={year}
                          selected={state.fromYear === year}
                        >
                          {year}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label class="xp-field">
                    <span class="xp-label">To</span>
                    <select
                      class="xp-input"
                      onChange$={(_, element) => {
                        state.toYear = element.value;
                      }}
                    >
                      {years.map((year) => (
                        <option
                          key={year}
                          value={year}
                          selected={state.toYear === year}
                        >
                          {year}
                        </option>
                      ))}
                    </select>
                  </label>
                </>
              )}
            </div>

            {range && range.from <= range.to && (
              <p class="xp-summary">{describe(range)}</p>
            )}
          </div>

          <fieldset class="xp-section">
            <legend class="xp-label">Format</legend>
            <div class="xp-choices">
              {csvFormats.map((format) => (
                <label
                  key={format.value}
                  class={{
                    "xp-choice": true,
                    "xp-choice-selected": state.format === format.value,
                  }}
                >
                  <input
                    type="radio"
                    name="xp-format"
                    value={format.value}
                    checked={state.format === format.value}
                    onChange$={() => {
                      state.format = format.value;
                    }}
                  />
                  {format.label}
                </label>
              ))}
            </div>
            {selectedFormat && <p class="xp-hint">{selectedFormat.hint}</p>}
          </fieldset>

          {state.error && (
            <p class="xp-error" role="alert">
              {state.error}
            </p>
          )}

          <div class="xp-actions">
            <button type="button" class="xp-secondary" onClick$={close}>
              Cancel
            </button>
            <button type="submit" class="xp-primary" disabled={state.exporting}>
              {state.exporting ? "Exporting…" : "Download CSV"}
            </button>
          </div>
        </form>
      </dialog>
    </>
  );
});
