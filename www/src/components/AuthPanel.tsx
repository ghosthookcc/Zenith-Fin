import { $, component$, useSignal, useStore } from '@builder.io/qwik';

import { Icon } from './Icon';
import { fetchWithTimeout } from '../utils/network';

type Mode = 'signin' | 'register';

interface AuthResponse {
    success?: boolean;
    message?: string;
    url?: string | null;
}

const unreachable = "Zenith couldn't reach its server. Check that it is running and try again.";

function landingPath(url: string | null | undefined): string {
    return typeof url === 'string' && url.startsWith('/') && !url.startsWith('//') ? url : '/accounts';
}

async function post(path: string, body: unknown): Promise<{ status: number; data: AuthResponse | null }> {
    const response = await fetchWithTimeout(
        path,
        {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(body),
        },
        10000,
    );

    const data = (await response.json().catch(() => null)) as AuthResponse | null;

    return { status: response.status, data };
}

export const AuthPanel = component$(() => {
    const mode = useSignal<Mode>('signin');
    const busy = useSignal(false);
    const revealed = useSignal(false);
    const error = useSignal<string | null>(null);
    const notice = useSignal<string | null>(null);

    const signIn = useStore({ email: '', password: '' });
    const account = useStore({
        firstName: '',
        lastName: '',
        email: '',
        phone: '',
        password: '',
        repeatPassword: '',
    });

    const switchTo = $((next: Mode) => {
        mode.value = next;
        revealed.value = false;
        error.value = null;
        notice.value = null;
    });

    const submitSignIn = $(async () => {
        if (busy.value) {
            return;
        }

        busy.value = true;
        error.value = null;
        notice.value = null;

        try {
            const { status, data } = await post('/api/login', {
                email: signIn.email.trim(),
                password: signIn.password,
            });

            if (status === 401) {
                throw new Error("That email and password don't match an account.");
            }

            if (status === 503) {
                throw new Error(unreachable);
            }

            if (!data?.success) {
                throw new Error(data?.message || 'Signing in failed. Try again.');
            }

            window.location.href = landingPath(data.url);
        } catch (err) {
            error.value = err instanceof TypeError || (err instanceof Error && err.name === 'AbortError')
                ? unreachable
                : err instanceof Error
                  ? err.message
                  : 'Signing in failed. Try again.';
            busy.value = false;
        }
    });

    const submitRegister = $(async () => {
        if (busy.value) {
            return;
        }

        error.value = null;
        notice.value = null;

        if (account.password !== account.repeatPassword) {
            error.value = "The passwords don't match.";
            return;
        }

        busy.value = true;

        try {
            const { status, data } = await post('/api/register', {
                firstName: account.firstName.trim(),
                lastName: account.lastName.trim(),
                email: account.email.trim(),
                phone: account.phone.trim(),
                password: account.password,
            });

            if (status === 503) {
                throw new Error(unreachable);
            }

            if (!data?.success) {
                throw new Error(data?.message || 'Creating the account failed. Try again.');
            }

            signIn.email = account.email.trim();
            signIn.password = '';
            account.password = '';
            account.repeatPassword = '';
            mode.value = 'signin';
            revealed.value = false;
            notice.value = 'Your account is ready. Sign in to continue.';
        } catch (err) {
            error.value = err instanceof TypeError || (err instanceof Error && err.name === 'AbortError')
                ? unreachable
                : err instanceof Error
                  ? err.message
                  : 'Creating the account failed. Try again.';
        } finally {
            busy.value = false;
        }
    });

    const signingIn = mode.value === 'signin';
    const passwordType = revealed.value ? 'text' : 'password';

    return (
        <section class="auth-card" aria-labelledby="auth-title">
            <div class="auth-tabs" role="tablist" aria-label="Sign in or create an account">
                <button
                    type="button"
                    role="tab"
                    id="auth-tab-signin"
                    class="auth-tab"
                    aria-selected={signingIn}
                    aria-controls="auth-panel"
                    onClick$={() => switchTo('signin')}
                >
                    Sign in
                </button>
                <button
                    type="button"
                    role="tab"
                    id="auth-tab-register"
                    class="auth-tab"
                    aria-selected={!signingIn}
                    aria-controls="auth-panel"
                    onClick$={() => switchTo('register')}
                >
                    Create account
                </button>
            </div>

            <div id="auth-panel" role="tabpanel" aria-labelledby={signingIn ? 'auth-tab-signin' : 'auth-tab-register'}>
                <h2 id="auth-title" class="auth-title">
                    {signingIn ? 'Welcome back' : 'Create your account'}
                </h2>
                <p class="auth-subtitle">
                    {signingIn ? 'Sign in to see your accounts and transactions.' : 'Set up Zenith in a minute.'}
                </p>

                {notice.value && (
                    <p class="auth-message auth-notice" role="status">
                        <Icon name="check" />
                        {notice.value}
                    </p>
                )}

                {error.value && (
                    <p class="auth-message auth-error" role="alert">
                        <Icon name="info" />
                        {error.value}
                    </p>
                )}

                {signingIn ? (
                    <form class="auth-form" preventdefault:submit onSubmit$={submitSignIn}>
                        <div class="auth-field">
                            <label for="auth-email">Email</label>
                            <input
                                id="auth-email"
                                type="email"
                                name="email"
                                autoComplete="username"
                                required
                                value={signIn.email}
                                onInput$={(_, element) => {
                                    signIn.email = element.value;
                                }}
                            />
                        </div>

                        <div class="auth-field">
                            <label for="auth-password">Password</label>
                            <div class="auth-password">
                                <input
                                    id="auth-password"
                                    type={passwordType}
                                    name="password"
                                    autoComplete="current-password"
                                    required
                                    value={signIn.password}
                                    onInput$={(_, element) => {
                                        signIn.password = element.value;
                                    }}
                                />
                                <button
                                    type="button"
                                    class="auth-reveal"
                                    aria-label={revealed.value ? 'Hide password' : 'Show password'}
                                    aria-pressed={revealed.value}
                                    onClick$={() => {
                                        revealed.value = !revealed.value;
                                    }}
                                >
                                    <Icon key={passwordType} name={revealed.value ? 'eyeoff' : 'eye'} />
                                </button>
                            </div>
                        </div>

                        <button type="submit" class="auth-submit" disabled={busy.value}>
                            {busy.value ? 'Signing in…' : 'Sign in'}
                        </button>
                    </form>
                ) : (
                    <form class="auth-form" preventdefault:submit onSubmit$={submitRegister}>
                        <div class="auth-row">
                            <div class="auth-field">
                                <label for="auth-first-name">First name</label>
                                <input
                                    id="auth-first-name"
                                    type="text"
                                    autoComplete="given-name"
                                    required
                                    value={account.firstName}
                                    onInput$={(_, element) => {
                                        account.firstName = element.value;
                                    }}
                                />
                            </div>
                            <div class="auth-field">
                                <label for="auth-last-name">Last name</label>
                                <input
                                    id="auth-last-name"
                                    type="text"
                                    autoComplete="family-name"
                                    required
                                    value={account.lastName}
                                    onInput$={(_, element) => {
                                        account.lastName = element.value;
                                    }}
                                />
                            </div>
                        </div>

                        <div class="auth-field">
                            <label for="auth-new-email">Email</label>
                            <input
                                id="auth-new-email"
                                type="email"
                                autoComplete="email"
                                required
                                value={account.email}
                                onInput$={(_, element) => {
                                    account.email = element.value;
                                }}
                            />
                        </div>

                        <div class="auth-field">
                            <label for="auth-phone">Phone</label>
                            <input
                                id="auth-phone"
                                type="tel"
                                autoComplete="tel"
                                required
                                value={account.phone}
                                onInput$={(_, element) => {
                                    account.phone = element.value;
                                }}
                            />
                        </div>

                        <div class="auth-field">
                            <label for="auth-new-password">Password</label>
                            <div class="auth-password">
                                <input
                                    id="auth-new-password"
                                    type={passwordType}
                                    autoComplete="new-password"
                                    required
                                    value={account.password}
                                    onInput$={(_, element) => {
                                        account.password = element.value;
                                    }}
                                />
                                <button
                                    type="button"
                                    class="auth-reveal"
                                    aria-label={revealed.value ? 'Hide passwords' : 'Show passwords'}
                                    aria-pressed={revealed.value}
                                    onClick$={() => {
                                        revealed.value = !revealed.value;
                                    }}
                                >
                                    <Icon key={passwordType} name={revealed.value ? 'eyeoff' : 'eye'} />
                                </button>
                            </div>
                        </div>

                        <div class="auth-field">
                            <label for="auth-repeat-password">Repeat password</label>
                            <input
                                id="auth-repeat-password"
                                type={passwordType}
                                autoComplete="new-password"
                                required
                                value={account.repeatPassword}
                                onInput$={(_, element) => {
                                    account.repeatPassword = element.value;
                                }}
                            />
                        </div>

                        <button type="submit" class="auth-submit" disabled={busy.value}>
                            {busy.value ? 'Creating your account…' : 'Create account'}
                        </button>
                    </form>
                )}

                <p class="auth-switch">
                    {signingIn ? 'New to Zenith?' : 'Already have an account?'}{' '}
                    <button
                        type="button"
                        class="auth-link"
                        onClick$={() => switchTo(signingIn ? 'register' : 'signin')}
                    >
                        {signingIn ? 'Create an account' : 'Sign in instead'}
                    </button>
                </p>
            </div>
        </section>
    );
});
