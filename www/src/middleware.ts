import type { MiddlewareHandler } from 'astro';

import { Agent } from 'undici';

const insecureDispatcher = new Agent({ connect: { rejectUnauthorized: false } });

const PUBLIC_ROUTES = ['/', '/api/login', '/api/register', '/api/session', '/favicon.ico'];

const LOGGED_IN_LANDING_ROUTE = "/dashboard";

export const onRequest: MiddlewareHandler = async ({ request, redirect, cookies }, next) =>
{
    const url = new URL(request.url);
    console.log('🔵 MIDDLEWARE: Request to:', url.pathname);

    const jwt = cookies.get('AuthToken')?.value;
    console.log('🔵 MIDDLEWARE: JWT present?', !!jwt);

    const isPublic = PUBLIC_ROUTES.includes(url.pathname);
    const isApi = url.pathname.startsWith("/api/");

    if (isPublic)
    {
        console.log('🔵 MIDDLEWARE: Public route, skipping auth');
        if (jwt && !isApi)
        {
            const response = await fetch(new URL('/api/session', url.origin),
            {
                method: 'POST',
                headers:
                {
                    "Content-Type": "application/json",
                    "Cookie": `AuthToken=${jwt}`
                },
                dispatcher: insecureDispatcher,
            });
            const data = await response.json();
            if (data.success === true)
            {
                return redirect(LOGGED_IN_LANDING_ROUTE);
            }
        }
        return next();
    }

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
                "Content-Type": "application/json",
                "Cookie": `AuthToken=${jwt}`
            },
            dispatcher: insecureDispatcher,
        });

        console.log('🔵 MIDDLEWARE: Session API response status:', response.status);

        if (!response.ok)
        {
            console.log('🔵 MIDDLEWARE: Session invalid, redirecting');
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
        return redirect('/');
    }
};
