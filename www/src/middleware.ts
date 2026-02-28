import type { MiddlewareHandler } from 'astro';

import { Agent } from 'undici';

const insecureDispatcher = new Agent({ connect: { rejectUnauthorized: false } });

const PUBLIC_ROUTES = ['/', '/api/login', '/api/register', '/api/session', '/favicon.ico'];

export const onRequest: MiddlewareHandler = async ({ request, redirect, cookies }, next) =>
{
    const url = new URL(request.url);
    console.log('🔵 MIDDLEWARE: Request to:', url.pathname);

    if (PUBLIC_ROUTES.includes(url.pathname))
    {
        console.log('🔵 MIDDLEWARE: Public route, skipping auth');
        return next();
    }

    const jwt = cookies.get('AuthToken')?.value;
    console.log('🔵 MIDDLEWARE: JWT present?', !!jwt);

    if (!jwt)
    {
        console.log('🔵 MIDDLEWARE: No JWT, redirecting to /');
        return redirect('/');
    }

    try
    {
        const response = await fetch(new URL('/api/session', url.origin),
        {
            method: 'POST',
            headers:
            {
                "Cookie": `AuthToken=${jwt}`,
                "Content-Type": "application/json"
            },
            dispatcher: insecureDispatcher,
        });

        console.log('🔵 MIDDLEWARE: Session API response status:', response.status);

        if (!response.ok)
        {
            console.log('🔵 MIDDLEWARE: Session invalid, redirecting');
            cookies.delete("AuthToken", { path: "/" });
            return redirect('/');
        }

        const data = await response.json();
        console.log('🔵 MIDDLEWARE: Session valid, user:', data.userId);

        (request as any).user = data.userId;

        return next();
    }
    catch (errno)
    {
        console.error('🔵 MIDDLEWARE: Session check failed:', errno);
        console.error('Session check failed', errno);
        cookies.delete("AuthToken", { path: "/" });
        return redirect('/');
    }
};
