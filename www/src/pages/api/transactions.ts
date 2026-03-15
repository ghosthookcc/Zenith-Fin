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

        const response = await fetch("https://localhost:4446/api/v1/auth/users/session",
        {
            method: "GET",
            headers:
            {
                "Authorization": `Bearer ${jwt}`,
                "Content-Type": "application/json",
            },
            dispatcher: insecureDispatcher,
        }); 

        const data = await response.json();
        console.log('🟢 SERVER: Received response as json:', data);

        return new Response(
            JSON.stringify(
            {
                success: data.success,
            }),
            { status: data.code }
        );
  }
  catch (errno)
  {
      console.error('🟢 SERVER: Fetch failed:', errno);

      return new Response(
          JSON.stringify(
          {
              message: 'Could not validate session',
              success: false,
              error: errno instanceof Error ? errno.message : 'Unknown error'
          }),
          { status: 503 }
      );
  }
};
