import type { APIRoute } from "astro";

import { forwardToBackend } from "../../utils/backend.ts";

export const prerender = false;

export const GET: APIRoute = async ({ cookies, url }) => {
  const from = url.searchParams.get("from") ?? "";
  const to = url.searchParams.get("to") ?? "";

  if (!/^\d{4}-\d{2}-\d{2}$/.test(from) || !/^\d{4}-\d{2}-\d{2}$/.test(to)) {
    return new Response(
      JSON.stringify({ success: false, message: "Choose a period" }),
      { status: 400, headers: { "Content-Type": "application/json" } },
    );
  }

  return forwardToBackend(cookies, "GET", `/economy?from=${from}&to=${to}`);
};
