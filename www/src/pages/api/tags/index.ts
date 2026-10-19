import type { APIRoute } from "astro";

import { forwardToBackend } from "../../../utils/backend.ts";

export const prerender = false;

export const GET: APIRoute = async ({ cookies }) =>
  forwardToBackend(cookies, "GET", "/tags");

export const POST: APIRoute = async ({ cookies, request }) =>
  forwardToBackend(cookies, "POST", "/tags", await request.text());
