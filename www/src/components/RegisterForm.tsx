import { component$, useSignal, $ } from '@builder.io/qwik';
import { fetchWithTimeout } from '../utils/network';

export const RegisterForm = component$(() =>
{
    const firstName = useSignal('');
    const lastName = useSignal('');

    const email = useSignal('');
    const phone = useSignal('');

    const password = useSignal('');
    const repeatPassword = useSignal('');

    const error = useSignal('');

    const handleSubmit = $(async () => 
    {
        error.value = '';

        if (password.value !== repeatPassword.value) 
        {
            error.value = 'Passwords do not match';
            return;
        }

        try
        {
            const response = await fetchWithTimeout('/api/register',
            {
                method: 'POST',
                headers:
                {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(
                {
                    firstName: firstName.value,
                    lastName: lastName.value,
                    email: email.value,
                    phone: phone.value,
                    password: password.value,
                }, 5000),
            });

            const data = await response.json(); 

            if (!data.success)
            {
                throw new Error(data.message || "Registration failed");
            }
        }
        catch (errno: any)
        {
            error.value = errno.message;
        }
    });

    return (
        <div class="max-w-md rounded-2xl bg-white p-8 shadow-lg">
            <h2 class="mb-6 text-center text-2xl font-bold text-gray-800">
                Create account
            </h2>

            <form preventdefault:submit
                  onSubmit$={handleSubmit}
                  class="space-y-4">
                <div class="flex gap-3">
                    <input type="text"
                           placeholder="First name"
                           required
                          class="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                          onInput$={(e) =>
                            (firstName.value = (e.target as HTMLInputElement).value)
                          } />
                    <input type="text"
                           placeholder="Last name"
                           required
                           class="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                           onInput$={(e) =>
                               (lastName.value = (e.target as HTMLInputElement).value)
                           } />
                </div>

                <input type="email"
                       placeholder="Email"
                       required
                       class="w-full rounded-lg border text-red border-gray-300 px-4 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                       onInput$={(e) =>
                           (email.value = (e.target as HTMLInputElement).value)
                       } />

                <input type="tel"
                       placeholder="Phone"
                       required
                       class="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                       onInput$={(e) =>
                           (phone.value = (e.target as HTMLInputElement).value)
                       } />

                <input type="password"
                       placeholder="Password"
                       required
                       class="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                       onInput$={(e) =>
                           (password.value = (e.target as HTMLInputElement).value)
                       } />

                <input type="password"
                       placeholder="Repeat password"
                       required
                       class="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                       onInput$={(e) =>
                           (repeatPassword.value = (e.target as HTMLInputElement).value)
                       } />

                {error.value && (
                    <p class="text-center text-sm text-red-600">{error.value}</p>
                )}

                <button type="submit"
                        class="w-full rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white transition hover:bg-indigo-700 active:scale-[0.98]">
                    Register
                </button>
            </form>
        </div>
    );
});

