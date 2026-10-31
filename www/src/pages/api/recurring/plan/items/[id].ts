import type { APIRoute } from "astro";

import {
  forwardToBackend,
  isSameOrigin,
} from "../../../../../utils/backend.ts";

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

function validId(id: string | undefined): id is string {
  return id !== undefined && /^\d{1,18}$/.test(id);
}

export const PUT: APIRoute = async ({ cookies, params, request }) => {
  if (!isSameOrigin(request)) {
    return refuse(403, "This request must come from Zenith itself");
  }

  if (!validId(params.id)) {
    return refuse(400, "That row is not in your plan");
  }

  if (!request.headers.get("Content-Type")?.startsWith("application/json")) {
    return refuse(415, "Send the row as JSON");
  }

  return forwardToBackend(
    cookies,
    "PUT",
    `/recurring/plan/items/${params.id}`,
    await request.text(),
  );
};

export const DELETE: APIRoute = async ({ cookies, params, request }) => {
  if (!isSameOrigin(request)) {
    return refuse(403, "This request must come from Zenith itself");
  }

  if (!validId(params.id)) {
    return refuse(400, "That row is not in your plan");
  }

  return forwardToBackend(
    cookies,
    "DELETE",
    `/recurring/plan/items/${params.id}`,
  );
};
