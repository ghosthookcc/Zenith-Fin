import type { APIRoute } from "astro";
import { Agent } from "undici";

const insecureDispatcher = new Agent({
  connect: {
    rejectUnauthorized: false,
  },
});

export const prerender = false;

export const GET: APIRoute = async ({ request, cookies, redirect }) => {
    console.log('🟢 SERVER: GET handler called');
    try
    {
        const jwt = cookies.get("AuthToken")?.value;

        if (!jwt)
        {
            return new Response(
                JSON.stringify(
                {
                    message: "No token provided",
                    success: false
                }),
                { status: 401 }
            );
        }

        const searchParams = new URL(request.url).searchParams;
        const paramsAsState = JSON.stringify(
        {
                      state: searchParams.get("state"),
                      code: searchParams.get("code")
        });

        const response = await fetch("https://localhost:4446/api/v1/aspsp/auth/callback",
        {
            method: "POST",
            headers:
            {
                "Authorization": `Bearer ${jwt}`,
                'Content-Type': 'application/json',
            },
            body: paramsAsState,
            dispatcher: insecureDispatcher,
        });

        console.log('🟢 SERVER: Received response:', response);

        const data = await response.json();
        console.log('🟢 SERVER: Received response as json:', data);

        return redirect(response.ok && data?.expiresAt ? "/accounts?bank=connected" : "/accounts?bank=failed", 303);
  }
  catch (errno)
  {
      console.error('🟢 SERVER: Fetch failed:', errno);

      return redirect("/accounts?bank=failed", 303);
  }
};
