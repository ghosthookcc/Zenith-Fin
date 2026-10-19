import type { APIRoute } from "astro";

import { forwardToBackend } from "../../../../utils/backend.ts";

export const prerender = false;

export const GET: APIRoute = async ({ cookies, params }) => {
  if (!params.id || !/^\d+$/.test(params.id)) {
    return new Response(
      JSON.stringify({
        success: false,
        message: "Unknown person",
      }),
      {
        status: 404,
        headers: {
          "Content-Type": "application/json",
          "Cache-Control": "no-store",
        },
      },
    );
  }

  const response = await forwardToBackend(
    cookies,
    "GET",
    `/account-shares/${params.id}/personal-number`,
  );

  response.headers.set("Cache-Control", "no-store");

  return response;
};
