import type { APIRoute } from "astro";

import { downloadFromBackend } from "../../../utils/backend.ts";

export const prerender = false;

export const GET: APIRoute = async ({ cookies, url }) =>
  downloadFromBackend(
    cookies,
    `/exports/accounts?format=${
      url.searchParams.get("format") === "standard" ? "standard" : "excel"
    }`,
  );
