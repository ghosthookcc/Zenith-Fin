export interface CreditAccountRule {
    id: number;
    name: string;
    accountId: number;
    counting: string;
    creditLimit?: number | null;
}

export const uncountedStatuses = ['CNCL', 'RJCT', 'SCHD'];

export const countingChoices = [
    {
        value: 'OUT',
        label: 'Money out',
        hint: 'Every transaction on the linked account counts as money out, even money coming in.',
    },
    {
        value: 'IN',
        label: 'Money in',
        hint: 'Every transaction on the linked account counts as money in, even money going out.',
    },
    {
        value: 'FLOW',
        label: 'Money in & out',
        hint: 'Money coming in counts as money in and money going out counts as money out.',
    },
    {
        value: 'NEUTRAL',
        label: 'Neutral',
        hint: "Its transactions are left out of the month's money in and out.",
    },
];

export function countingLabel(counting: string): string {
    return countingChoices.find((choice) => choice.value === counting)?.label ?? counting;
}

export function countedAmount(amount: number, counting: string): number | null {
    switch (counting) {
        case 'NEUTRAL':
            return null;
        case 'OUT':
            return -Math.abs(amount);
        case 'IN':
            return Math.abs(amount);
        default:
            return amount;
    }
}

export function creditUsed(
    transactions: { amount: number; status: string }[],
    counting: string,
): number {
    let out = 0;
    let incoming = 0;

    for (const transaction of transactions) {
        if (uncountedStatuses.includes(transaction.status)) {
            continue;
        }

        const minor = Math.round(Math.abs(transaction.amount) * 100);

        if (transaction.amount < 0) {
            out += minor;
        } else {
            incoming += minor;
        }
    }

    return counting === 'OUT' || counting === 'IN' ? out + incoming : Math.max(0, out - incoming);
}
