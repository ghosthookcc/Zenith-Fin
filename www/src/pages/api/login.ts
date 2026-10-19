import type { APIRoute } from "astro";
import { Agent } from "undici";
import { backendUrl } from "../../utils/backend.ts";

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

        const token = cookies.get("AuthToken")?.value;
        const headers: Record<string, string> =
        {
            "Content-Type": "application/json",
        }
        if (token)
        {
            headers["Authorization"] = `Bearer ${token}`;
        }

        const response = await fetch(`${backendUrl}/auth/users/login`,
        {
            method: "POST",
            headers,
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

        if (data.success && data.token && data.jwtExpirationDate)
        {
            const expires = new Date(data.jwtExpirationDate);
            cookies.set("AuthToken", data.token,
            {
                httpOnly: true,
                secure: true,
                sameSite: "none",
                path: "/",
                expires,
            });
            console.log('🟢 SERVER: Cookie set with expires date:', expires.toUTCString());
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
