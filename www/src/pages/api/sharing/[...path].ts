import type { APIRoute } from "astro";

import { forwardToBackend } from "../../../utils/backend.ts";

export const prerender = false;

function target(path: string | undefined): string | null {
  const clean = path ?? "";

  return /^[a-z0-9/-]*$/.test(clean)
    ? `/sharing${clean ? `/${clean}` : ""}`
    : null;
}

function notFound(): Response {
  return new Response(
    JSON.stringify({ success: false, message: "Unknown request" }),
    { status: 404, headers: { "Content-Type": "application/json" } },
  );
}

export const GET: APIRoute = async ({ cookies, params }) => {
  const path = target(params.path);

  return path ? forwardToBackend(cookies, "GET", path) : notFound();
};

export const POST: APIRoute = async ({ cookies, params, request }) => {
  const path = target(params.path);
  const body = await request.text();

  return path
    ? forwardToBackend(cookies, "POST", path, body || "{}")
    : notFound();
};
