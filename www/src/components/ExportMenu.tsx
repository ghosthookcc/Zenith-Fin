import { component$, type QRL, useSignal } from "@builder.io/qwik";

import { Icon } from "./Icon";

export const csvFormats = [
  {
    value: "excel",
    label: "For Excel",
    hint: "Semicolons and decimal commas, so it opens straight into columns in Swedish Excel",
  },
  {
    value: "standard",
    label: "Standard CSV",
    hint: "Commas and decimal points, for LibreOffice, Google Sheets, Numbers, Python and R",
  },
];

interface ExportMenuProps {
  id: string;
  label: string;
  description: string;
  exporting: boolean;
  onExport$: QRL<(format: string) => Promise<void>>;
}

export const ExportMenu = component$<ExportMenuProps>((props) => {
  const open = useSignal(false);

  return (
    <div
      class="reconnect-menu"
      onKeyDown$={(event) => {
        if (event.key === "Escape") {
          open.value = false;
        }
      }}
    >
      <button
        type="button"
        class="visibility-button"
        title={props.description}
        aria-expanded={open.value}
        aria-controls={props.id}
        disabled={props.exporting}
        onClick$={() => {
          open.value = !open.value;
        }}
      >
        <Icon name="download" />
        {props.exporting ? "Exporting…" : props.label}
      </button>

      {open.value && (
        <div
          id={props.id}
          class="reconnect-menu-list export-menu-list"
          role="group"
          aria-label={props.description}
        >
          {csvFormats.map((format) => (
            <button
              key={format.value}
              type="button"
              class="reconnect-menu-item"
              onClick$={async () => {
                open.value = false;
                await props.onExport$(format.value);
              }}
            >
              <span>{format.label}</span>
              <small>{format.hint}</small>
            </button>
          ))}
        </div>
      )}
    </div>
  );
});
