function fileNameFrom(disposition: string | null, fallback: string): string {
  const encoded = disposition?.match(/filename\*=UTF-8''([^;]+)/i)?.[1];

  if (encoded) {
    try {
      return decodeURIComponent(encoded);
    } catch {
      return fallback;
    }
  }

  return disposition?.match(/filename="?([^";]+)"?/i)?.[1] ?? fallback;
}

export async function downloadFile(
  url: string,
  fallbackName: string,
): Promise<void> {
  const response = await fetch(url, {
    method: "GET",
    headers: {
      Accept: "text/csv, application/json",
    },
    credentials: "same-origin",
    cache: "no-store",
  });

  if (!response.ok) {
    const payload = (await response.json().catch(() => null)) as {
      message?: string;
    } | null;

    throw new Error(
      payload?.message ?? `The export didn't work (${response.status})`,
    );
  }

  const link = document.createElement("a");

  link.href = URL.createObjectURL(await response.blob());
  link.download = fileNameFrom(
    response.headers.get("Content-Disposition"),
    fallbackName,
  );
  document.body.append(link);
  link.click();
  link.remove();

  setTimeout(() => URL.revokeObjectURL(link.href), 1000);
}
