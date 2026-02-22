import type { APIRoute } from "astro";
import { Agent } from "undici";

const insecureDispatcher = new Agent({
  connect: {
    rejectUnauthorized: false,
  },
});

export const prerender = false;

export const POST: APIRoute = async ({ request }) => {
    console.log('🟢 SERVER: POST handler called');
    try
    {
        const body = await request.json();
        console.log('🟢 SERVER: Received data:', body);

        console.log(JSON.stringify({"aspsps" : body}));

        const response = await fetch("https://localhost:4446/api/v1/auth/aspsp/connect",
        {
            method: "POST",
            headers:
            {
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
                                message: "test",
                                success: true
                            }),
                            { status: 200 });
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
