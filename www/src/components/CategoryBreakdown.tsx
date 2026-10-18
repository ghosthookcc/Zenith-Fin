import { component$, type QRL } from '@builder.io/qwik';

import { Icon } from './Icon';

export interface CategoryBreakdownRow {
    id: number;
    selectable?: boolean;
    name: string;
    icon: string;
    color: string;
    amountLabel: string;
    shareLabel: string;
    width: number;
}

interface CategoryBreakdownProps {
    rows: CategoryBreakdownRow[];
    monthName: string;
    selectedId: number | null;
    onSelect$: QRL<(id: number) => void>;
}

export const CategoryBreakdown = component$<CategoryBreakdownProps>((props) => {
    return (
        <section class="overview-panel cal-breakdown" aria-labelledby="cal-breakdown-title">
            <div class="cal-breakdown-head">
                <h2 id="cal-breakdown-title">
                    <span class="tiny-mark" />
                    Where your money went
                </h2>
                <p>
                    Spending by category in {props.monthName}, without transfers between your own
                    accounts.
                </p>
            </div>

            {props.rows.length === 0 ? (
                <p class="cal-empty">No spending in {props.monthName}.</p>
            ) : (
                <ul class="cal-breakdown-list">
                    {props.rows.map((row) => {
                        const content = (
                            <>
                                <span
                                    class="cal-tag-tile"
                                    style={{ '--tag-color': row.color }}
                                    aria-hidden="true"
                                >
                                    <Icon name={row.icon} />
                                </span>
                                <span class="cal-breakdown-name">{row.name}</span>
                                <span class="cal-breakdown-amount">{row.amountLabel}</span>
                                <span class="cal-breakdown-share">{row.shareLabel}</span>
                                <span class="cal-breakdown-track" aria-hidden="true">
                                    <span style={{ width: `${row.width}%` }} />
                                </span>
                            </>
                        );

                        return (
                            <li key={row.id}>
                                {row.selectable === false ? (
                                    <div class="cal-breakdown-row cal-breakdown-static">{content}</div>
                                ) : (
                                    <button
                                        type="button"
                                        class={{
                                            'cal-breakdown-row': true,
                                            'cal-breakdown-selected': props.selectedId === row.id,
                                        }}
                                        aria-pressed={props.selectedId === row.id}
                                        onClick$={() => props.onSelect$(row.id)}
                                    >
                                        {content}
                                    </button>
                                )}
                            </li>
                        );
                    })}
                </ul>
            )}
        </section>
    );
});
