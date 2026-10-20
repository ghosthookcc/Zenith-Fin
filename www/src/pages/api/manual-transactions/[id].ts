import type { APIRoute } from "astro";

import { forwardToBackend } from "../../../utils/backend.ts";

export const prerender = false;

export const DELETE: APIRoute = async ({ cookies, params }) =>
  params.id && /^\d+$/.test(params.id)
    ? forwardToBackend(cookies, "DELETE", `/manual-transactions/${params.id}`)
    : new Response(
        JSON.stringify({
          success: false,
          message: "Unknown transaction",
        }),
        {
          status: 404,
          headers: {
            "Content-Type": "application/json",
          },
        },
      );
