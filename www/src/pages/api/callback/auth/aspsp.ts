import type { APIRoute } from "astro";
import { Agent } from "undici";

const insecureDispatcher = new Agent({
  connect: {
    rejectUnauthorized: false,
  },
});

export const prerender = false;

export const GET: APIRoute = async ({ request, cookies }) => {
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

        return new Response(JSON.stringify(
                            {
                                message: "Callback received",
                                success: true
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
