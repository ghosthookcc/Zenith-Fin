export interface AvanzaAccount {
  accountId: string;
  accountName: string;
  accountType?: string | null;
  totalValue: number;
  currency: string;
  fetchedAt: string;
}

export interface AvanzaHistoryPoint {
  accountId: string;
  accountName: string;
  fetchedAt: string;
  totalValue: number;
}

export interface AvanzaBalances {
  fetchedAt?: string | null;
  accounts: AvanzaAccount[];
  history: AvanzaHistoryPoint[];
}

export interface AvanzaStatus {
  configured: boolean;
  exists: boolean;
  keyMatches: boolean;
  updatedAt?: string | null;
  lastAttemptAt?: string | null;
  lastSuccessAt?: string | null;
  status?: string | null;
  errorClass?: string | null;
}

async function readJson<T>(url: string, what: string): Promise<T> {
  const response = await fetch(url, {
    method: "GET",
    headers: {
      Accept: "application/json",
    },
    credentials: "same-origin",
    cache: "no-store",
  });

  const payload = (await response.json().catch(() => null)) as
    (T & { message?: string }) | null;

  if (!response.ok || !payload) {
    throw new Error(
      payload?.message ?? `Could not load ${what} (${response.status})`,
    );
  }

  return payload;
}

export async function loadAvanzaBalances(): Promise<AvanzaBalances> {
  const balances = await readJson<AvanzaBalances>(
    "/api/avanza/balances",
    "your Avanza values",
  );

  return {
    fetchedAt: balances.fetchedAt ?? null,
    accounts: Array.isArray(balances.accounts) ? balances.accounts : [],
    history: Array.isArray(balances.history) ? balances.history : [],
  };
}

export async function loadAvanzaStatus(): Promise<AvanzaStatus> {
  return readJson<AvanzaStatus>("/api/avanza/status", "the Avanza status");
}

export function avanzaTypeLabel(type: string | null | undefined): string {
  if (!type) {
    return "Avanza account";
  }

  const words = type.replace(/_/g, " ").trim().toLowerCase();

  return words.charAt(0).toUpperCase() + words.slice(1);
}
