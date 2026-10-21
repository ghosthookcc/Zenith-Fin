import type { APIRoute } from "astro";

import { forwardToBackend } from "../../../../utils/backend.ts";

export const prerender = false;

function invalidId(): Response {
  return new Response(
    JSON.stringify({
      success: false,
      message: "Unknown account",
    }),
    {
      status: 404,
      headers: {
        "Content-Type": "application/json",
      },
    },
  );
}

export const PUT: APIRoute = async ({ cookies, params, request }) =>
  params.id && /^\d+$/.test(params.id)
    ? forwardToBackend(
        cookies,
        "PUT",
        `/aspsp/accounts/${params.id}/role`,
        await request.text(),
      )
    : invalidId();
