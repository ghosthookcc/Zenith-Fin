import type { APIRoute } from "astro";
import { Agent } from "undici";

const insecureDispatcher = new Agent({
  connect: {
    rejectUnauthorized: false,
  },
});

export const prerender = false;

export const GET: APIRoute = async ({ cookies }) => {
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

        const response = await fetch("https://localhost:4446/api/v1/aspsp/all/inactive",
        {
            method: "GET",
            headers:
            {
                "Authorization": `Bearer ${jwt}`,
                "Content-Type": "application/json",
            },
            dispatcher: insecureDispatcher,
        });

        console.log('🟢 SERVER: Received response:', response);

        const data = await response.json();
        console.log('🟢 SERVER: Received response as json:', data);

        return new Response(JSON.stringify(
                            {
                                message: data.message,
                                success: data.success,
                                aspsps: data.aspsps
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
