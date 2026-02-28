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

        const body = await request.json();
        console.log('🟢 SERVER: Received data:', body);

        const response = await fetch("https://localhost:4446/api/v1/auth/aspsp/connect",
        {
            method: "POST",
            headers:
            {
                "Authorization": `Bearer ${jwt}`,
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({"aspsps" : body}),
            dispatcher: insecureDispatcher,
        });

        console.log('🟢 SERVER: Received response:', response);

        const data = await response.json();
        console.log('🟢 SERVER: Received response as json:', data);

        return new Response(JSON.stringify(
                            {
                                message: data.message,
                                success: data.success,
                                urls: data.urls
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

export const GET: APIRoute = async () => {
    console.log('🟢 SERVER: GET handler called');
    try
    {
        const response = await fetch("https://localhost:4446/api/v1/aspsp/all",
        {
            method: "GET",
            headers:
            {
                'Content-Type': 'application/json',
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
