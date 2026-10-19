import type { APIRoute } from "astro";

import { Agent } from 'undici';
import { backendUrl } from "../../utils/backend.ts";

const insecureDispatcher = new Agent(
{
    connect: 
    {
        rejectUnauthorized: false,
    },
});

export const prerender = false;

export const POST: APIRoute = async ({ request }) => 
{
  console.log('🟢 SERVER: POST handler called');
  try 
  {
      const body = await request.json();
          
      console.log('🟢 SERVER: Received data:', body);

      const response = await fetch(`${backendUrl}/auth/users/register`,
      {
          method: 'POST',
          headers: 
          {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            firstName: body.firstName,
            lastName: body.lastName,
            email: body.email,
            phone: body.phone,
            password: body.password,
          }),
          dispatcher: insecureDispatcher,
      });

      console.log('🟢 SERVER: Received response:', response); 

      const data = await response.json();
      console.log('🟢 SERVER: Received response as json:', data); 

      return new Response(
          JSON.stringify(
          {
              message: data.message,
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
            message: 'Could not connect to authentication server',
            success: false,
            error: errno instanceof Error ? errno.message : 'Unknown error'
        }),
        { status: 503 }
    );
  }
};
