import type { APIRoute } from "astro";
import { Agent } from "undici";

const insecureDispatcher = new Agent({
  connect: {
    rejectUnauthorized: false,
  },
});

export const prerender = false;

export const POST: APIRoute = async ({ request, cookies }) => {
    console.log('🟢 SERVER: POST handler called');
    try
    {
        const body = await request.json();
        console.log('🟢 SERVER: Received data:', body);

        const response = await fetch("https://localhost:4446/api/v1/auth/users/login",
        {
            method: "POST",
            headers:
            {
                "Content-Type": "application/json",
            },
            body: JSON.stringify(
            {
                email: body.email,
                password: body.password,
            }),
            dispatcher: insecureDispatcher,
        });

        console.log('🟢 SERVER: Received response:', response);

        const data = await response.json();
        console.log('🟢 SERVER: Received response as json:', data);

        const setCookieHeader = response.headers.get("set-cookie");
        console.log('🟢 SERVER: Set-Cookie header:', setCookieHeader);

        if (setCookieHeader)
        {
            const cookieMatch = setCookieHeader.match(/AuthToken=([^;]+)/);
            if (cookieMatch)
            {
                const tokenValue = cookieMatch[1];

                cookies.set("AuthToken", tokenValue,
                {
                    httpOnly: true,
                    secure: true,
                    sameSite: "none",
                    path: "/",
                    maxAge: 60 * 5
                });
                console.log('🟢 SERVER: Cookie set in Astro');
            }
        }

        return new Response(JSON.stringify(
                            {
                                message: data.message,
                                url: data.url,
                                success: data.success,
                            }),
                            { status: data.code });
  }
  catch (errno)
  {
      console.error('🟢 SERVER: Fetch failed:', errno);

      return new Response(
          JSON.stringify(
          {
              message: 'Could not connect to authentication server',
              success: false,
              error: errno instanceof Error ? errno.message : 'Unknown error'
          }),
          { status: 503 }
      );
  }
};
