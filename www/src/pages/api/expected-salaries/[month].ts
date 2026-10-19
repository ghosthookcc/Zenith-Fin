import type { APIRoute } from "astro";

import { forwardToBackend } from "../../../utils/backend.ts";

export const prerender = false;

function invalidMonth(): Response {
  return new Response(
    JSON.stringify({
      success: false,
      message: "Months must look like 2026-10",
    }),
    {
      status: 400,
      headers: {
        "Content-Type": "application/json",
      },
    },
  );
}

export const PUT: APIRoute = async ({ cookies, params, request }) =>
  params.month && /^\d{4}-\d{2}$/.test(params.month)
    ? forwardToBackend(
        cookies,
        "PUT",
        `/expected-salaries/${params.month}`,
        await request.text(),
      )
    : invalidMonth();

export const DELETE: APIRoute = async ({ cookies, params }) =>
  params.month && /^\d{4}-\d{2}$/.test(params.month)
    ? forwardToBackend(cookies, "DELETE", `/expected-salaries/${params.month}`)
    : invalidMonth();
