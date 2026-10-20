import type { APIRoute } from "astro";

import { forwardToBackend } from "../../../utils/backend.ts";

export const prerender = false;

export const POST: APIRoute = async ({ cookies, request }) =>
  forwardToBackend(
    cookies,
    "POST",
    "/manual-transactions",
    await request.text(),
  );
