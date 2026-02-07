import type { APIRoute } from "astro";

export const POST: APIRoute = async ({ request, cookies }) => {
  const body = await request.json();
  const { email, password } = body;

  const response = await fetch('https://api.yourdomain.com/auth/login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ email, password }),
  });

  if (!response.ok) {
    return new Response(
      JSON.stringify({ message: 'Invalid credentials' }),
      { status: 401 }
    );
  }

  const { jwt, sessionId, expiresIn } = await response.json();

  cookies.set('jwt', jwt, {
    httpOnly: true,
    secure: true,
    sameSite: 'none',
    path: '/',
    maxAge: expiresIn,
  });

  cookies.set('sessionId', sessionId, {
    httpOnly: true,
    secure: true,
    sameSite: 'none',
    path: '/',
    maxAge: expiresIn,
  });

  return new Response(
    JSON.stringify({ success: true }),
    { status: 200 }
  );
};
