import { component$, $, useSignal, useVisibleTask$ } from '@builder.io/qwik';

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

    const toggleAspsp = $((id: string, checked: boolean) => {
        aspsps.value = aspsps.value.map((a) => {
            if (a.id === id) {
                return { ...a, checked };
            }

            return a;
        });
    });
    
    useVisibleTask$(async () => 
    {
        try 
        {
            const response = await fetch('/api/aspsps/inactive', {
                method: 'GET',
                headers: {
                    Accept: 'application/json',
                },
            });

            const data = await response.json();

            if (!response.ok || !data.success) throw new Error(data.message || 'Could not load banks',);

            const raw: Record<
                string,
                {
                    country: string;
                    psuType: string;
                }
            >[] = data.aspsps?.aspsps ?? [];

            aspsps.value = raw.flatMap((item) =>
                Object.entries(item).map(([name, details]) => ({
                    id: name,
                    bank: name,
                    country: details.country,
                    psuType: details.psuType,
                })),
            );
        } 
        catch (err) 
        {
            console.error('ASPSP fetch error:', err);

            error.value =
                err instanceof Error
                    ? err.message
                    : 'Unknown ASPSP error';
        } 
        finally 
        {
            loading.value = false;
        }
    });

    return (
        <div>
            <form
                preventdefault:submit
                onSubmit$={handleSubmit}
            >
                {loading.value && <p>Loading banks...</p>}

                {!loading.value && error.value && (
                    <p>Error: {error.value}</p>
                )}

                {!loading.value && !error.value && (
                    <div>
                        <h2>Available banks</h2>

                        <ul>
                            {aspsps.value.map((a) => (
                                <li key={a.id}>
                                    <strong>{a.bank}</strong> — {a.psuType}

                                    <input
                                        type="checkbox"
                                        checked={a.checked ?? false}
                                        onChange$={(e) =>
                                            toggleAspsp(
                                                a.id,
                                                (e.target as HTMLInputElement).checked,
                                            )
                                        }
                                    />
                                </li>
                            ))}
                        </ul>
                    </div>
                )}

                {aspsps.value.length > 0 && (
                    <button type="submit">
                        Authenticate
                    </button>
                )}
            </form>

            {authUrls.value.length > 0 && (
                <div style={{ marginTop: '1rem' }}>
                    <h3>Authenticate with banks:</h3>

                    {authUrls.value.map((auth) => (
                        <a
                            key={auth.bank}
                            href={auth.url}
                            target="_blank"
                            rel="noopener noreferrer"
                        >
                            <button
                                type="button"
                                style={{
                                    display: 'block',
                                    margin: '0.5rem 0',
                                }}
                            >
                                Authenticate with {auth.bank}
                            </button>
                        </a>
                    ))}
                </div>
            )}
            <hr style={{ margin: '2rem 0' }} />
        </div>
    );
});