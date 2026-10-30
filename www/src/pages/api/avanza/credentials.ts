import type { APIRoute } from "astro";

import { forwardToBackend, isSameOrigin } from "../../../utils/backend.ts";

export const prerender = false;

function refuse(status: number, message: string): Response {
  return new Response(JSON.stringify({ success: false, message }), {
    status,
    headers: {
      "Content-Type": "application/json",
      "Cache-Control": "no-store",
    },
  });
}

export const PUT: APIRoute = async ({ cookies, request }) => {
  if (!isSameOrigin(request)) {
    return refuse(403, "This request must come from Zenith itself");
  }

  if (!request.headers.get("Content-Type")?.startsWith("application/json")) {
    return refuse(415, "Send the details as JSON");
  }

  return forwardToBackend(
    cookies,
    "PUT",
    "/avanza/credentials",
    await request.text(),
  );
};

export const DELETE: APIRoute = async ({ cookies, request }) => {
  if (!isSameOrigin(request)) {
    return refuse(403, "This request must come from Zenith itself");
  }

  return forwardToBackend(cookies, "DELETE", "/avanza/credentials");
};
