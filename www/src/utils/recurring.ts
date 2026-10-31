export type PlanDirection = "IN" | "OUT";
export type PlanUnit = "WEEK" | "MONTH" | "YEAR";
export type PlanState = "ACTIVE" | "QUIET" | "ENDED" | "MISSING";
export type PlanStatus = "PAID" | "LATE" | "PASSED" | "UPCOMING";

export interface PlanItem {
  id: number;
  source: "FIXED" | "BANK";
  seriesKey?: string | null;
  direction: PlanDirection;
  name: string;
  customName?: string | null;
  amount?: number | null;
  customAmount?: number | null;
  bankAmount?: number | null;
  every: number;
  unit: PlanUnit | "";
  cadenceLabel: string;
  customCadence: boolean;
  bankCadenceLabel?: string | null;
  lastDate?: string | null;
  endDate?: string | null;
  next?: string | null;
  monthly: number;
  state: PlanState;
  kind?: string | null;
  categoryIcon?: string | null;
  categoryColor?: string | null;
  accounts: string[];
}

export interface PlanOccurrence {
  itemId: number;
  name: string;
  direction: PlanDirection;
  date: string;
  amount: number;
  status: PlanStatus;
}

export interface PlanMonth {
  month: string;
  current: boolean;
  in: number;
  out: number;
  left: number;
  occurrences: PlanOccurrence[];
}

export interface PlanTotals {
  in: number;
  out: number;
  left: number;
}

export interface PlanSuggestion {
  key: string;
  name: string;
  kind: string;
  direction: PlanDirection;
  amount: number;
  cadenceLabel: string;
  last: string;
  monthly: number;
  categoryIcon?: string | null;
  categoryColor?: string | null;
  accounts: string[];
}

export interface Plan {
  items: PlanItem[];
  months: PlanMonth[];
  average: PlanTotals;
  suggestions: PlanSuggestion[];
}

export const maskedAmount = "•• •••,••";

export function formatKr(amount: number): string {
  return new Intl.NumberFormat("sv-SE", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
}

export function formatDay(date: string, withYear = false): string {
  return new Intl.DateTimeFormat("en-GB", {
    day: "numeric",
    month: "short",
    ...(withYear ? { year: "numeric" } : {}),
  }).format(new Date(`${date}T12:00:00`));
}

export function formatMonth(month: string, style: "short" | "long"): string {
  return new Intl.DateTimeFormat("en-GB", {
    month: style,
    year: "numeric",
  }).format(new Date(`${month}-01T12:00:00`));
}

export function count(value: number, singular: string, plural: string): string {
  return `${value} ${value === 1 ? singular : plural}`;
}
