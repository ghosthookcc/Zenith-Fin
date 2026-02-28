import { component$, useSignal, useVisibleTask$, $ } from '@builder.io/qwik';
import { fetchWithTimeout } from '../../utils/network';

interface Aspsp {
  id: string;
  bank: string;
  country: string;
  psuType: string;
  checked?: boolean;
}

interface AuthUrl {
    bank: string;
    url: string;
}

export default component$(() => {
  const aspsps = useSignal<Aspsp[]>([]);
  const authUrls = useSignal<AuthUrl[]>([]);

  const error = useSignal<string | null>(null);
  const loading = useSignal(true);

  const toggleAspsp = $((id: string, checked: boolean) =>
  {
    aspsps.value = aspsps.value.map(a =>
    {
      if (a.id === id)
      {
        return { ...a, checked };
      }
      return a;
    });
  });

  const handleSubmit = $(async () =>
  {
          error.value = '';
          loading.value = true;

          const payload = aspsps.value.filter(a => a.checked)
                                      .map(({ bank, country, psuType }) =>
                                      ({
                                        bank,
                                        country,
                                        psuType
                                      }));

          console.log("PAYLOAD:", payload);

          try
          {
              const response = await fetchWithTimeout('/api/aspsps',
              {
                  method: 'POST',
                  headers:
                  {
                      'Content-Type': 'application/json',
                  },
                  body: JSON.stringify(payload),
              }, 5000);

              const data = await response.json();

              if (!data.success)
              {
                  throw new Error(data.message || "Aspsp authentication failed");
              }

              authUrls.value = data.urls || [];
          }
          catch (errno: any)
          {
              error.value = errno.message;
          }
          finally
          {
              loading.value = false;
          }
    });

  useVisibleTask$(async () => {
    try {
      const response = await fetch('/api/aspsps', {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      });
      const data = await response.json();

      if (!data.success) {
        throw new Error(data.message);
      }

      const raw: Record<string, { country: string; psuType: string }>[] = data.aspsps.aspsps;

      aspsps.value = raw.flatMap((item) =>
        Object.entries(item).map(([name, details]) => ({
          id: name,
          bank: name,
          country: details.country,
          psuType: details.psuType,
        }))
      );
    } catch (err) {
      console.error('Fetch error:', err);
      error.value = err instanceof Error ? err.message : 'Unknown error';
    } finally {
      loading.value = false;
    }
  });

  return (
    <div>
      <form preventdefault:submit
            onSubmit$={handleSubmit}>

      {loading.value && <p>Loading...</p>}
      {!loading.value && error.value && <p>Error: {error.value}</p>}
      {!loading.value && !error.value && (
        <ul>
          {aspsps.value.map((a) => (
            <li key={a.id}>
              <strong>{a.bank}</strong> — {a.psuType}
              <input type="checkbox"
                     checked={a.checked ?? false}
                     onChange$={(e) => toggleAspsp(a.id, (e.target as HTMLInputElement).checked)} />
            </li>
          ))}
        </ul>
      )}
        <button type="submit">Authenticate</button>
      </form>

      {authUrls.value.length > 0 && (
          <div style={{ marginTop: "1rem" }}>
            <h3>Authenticate with banks:</h3>
            {authUrls.value.map(auth => (
                <a href={auth.url}
                   target="_blank"
                   rel="noopener noreferrer">
                    <button key={auth.url}
                            style={{ display: "block", margin: "0.5rem 0" }}>
                        Authenticate with {auth.bank}
                    </button>
                </a>
            ))}
          </div>
      )}
    </div>
  );
});
