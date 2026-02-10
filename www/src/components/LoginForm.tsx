import { component$, useSignal, $ } from '@builder.io/qwik';
import { fetchWithTimeout } from '../utils/network';

export const LoginForm = component$(() =>
{
    const email = useSignal('');
    const password = useSignal('');

    const error = useSignal('');
    const loading = useSignal(false);

    const handleSubmit = $(async () =>
    {
        error.value = '';
        loading.value = true;

        try
        {
            const response = await fetchWithTimeout('/api/login',
            {
                method: 'POST',
                headers:
                {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(
                {
                    email: email.value,
                    password: password.value,
                }),
            }, 5000);

            const data = await response.json();

            if (!data.success)
            {
                throw new Error(data.message || "Login failed");
            }
            else if (data.success && data.url)
            {
                window.location.href = data.url;
            }
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

  return (
      <div class="max-w-md rounded-2xl bg-white p-8 mt-4 shadow-lg">
        <h2 class="mb-6 text-center text-2xl font-bold text-gray-800">
          Sign in
        </h2>

        <form
          preventdefault:submit
          onSubmit$={handleSubmit}
          class="space-y-4"
        >
          <input
            type="email"
            placeholder="Email"
            required
            class="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
            onInput$={(e) =>
              (email.value = (e.target as HTMLInputElement).value)
            }
          />

          <input
            type="password"
            placeholder="Password"
            required
            class="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
            onInput$={(e) =>
              (password.value = (e.target as HTMLInputElement).value)
            }
          />

          {error.value && (
            <p class="text-center text-sm text-red-600">{error.value}</p>
          )}

          <button
            type="submit"
            disabled={loading.value}
            class="w-full rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60 active:scale-[0.98]"
          >
            {loading.value ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
  );
});
