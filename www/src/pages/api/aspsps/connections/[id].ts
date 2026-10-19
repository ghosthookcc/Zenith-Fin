import type { APIRoute } from "astro";

import { forwardToBackend } from "../../../../utils/backend.ts";

export const prerender = false;

function invalidId(): Response {
  return new Response(
    JSON.stringify({
      success: false,
      message: "Unknown bank connection",
    }),
    {
      status: 404,
      headers: {
        "Content-Type": "application/json",
      },
    },
  );
}

export const DELETE: APIRoute = async ({ cookies, params }) =>
  params.id && /^\d+$/.test(params.id)
    ? forwardToBackend(cookies, "DELETE", `/aspsp/connections/${params.id}`)
    : invalidId();
