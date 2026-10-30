import type { APIRoute } from "astro";

import { downloadFromBackend } from "../../../utils/backend.ts";

export const prerender = false;

const isoDate = /^\d{4}-\d{2}-\d{2}$/;

export const GET: APIRoute = async ({ cookies, url }) => {
  const from = url.searchParams.get("from") ?? "";
  const to = url.searchParams.get("to") ?? "";

  if (!isoDate.test(from) || !isoDate.test(to)) {
    return new Response(
      JSON.stringify({
        success: false,
        message: "Choose both a start and an end date",
      }),
      {
        status: 400,
        headers: {
          "Content-Type": "application/json",
        },
      },
    );
  }

  const query = new URLSearchParams({
    from,
    to,
    format:
      url.searchParams.get("format") === "standard" ? "standard" : "excel",
  });

  return downloadFromBackend(cookies, `/exports/transactions?${query}`);
};
