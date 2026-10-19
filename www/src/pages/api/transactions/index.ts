import type { APIRoute } from "astro";
import { Agent } from "undici";

import { fetchWithTimeout } from "../../../utils/network.ts";
import { backendUrl } from "../../../utils/backend.ts";

const insecureDispatcher = new Agent({
  connect: {
    rejectUnauthorized: false,
  },
});

const isoDate = /^\d{4}-\d{2}-\d{2}$/;

export const prerender = false;

export const GET: APIRoute = async ({ cookies, url }) => {
  try {
    const jwt = cookies.get("AuthToken")?.value;

    if (!jwt) {
      return new Response(
        JSON.stringify({
          success: false,
          message: "No token provided",
        }),
        {
          status: 401,
          headers: {
            "Content-Type": "application/json",
          },
        },
      );
    }

    const query = new URLSearchParams();

    for (const key of ["from", "to"]) {
      const value = url.searchParams.get(key);

      if (value && isoDate.test(value)) {
        query.set(key, value);
      }
    }

    const response = await fetchWithTimeout(
      `${backendUrl}/aspsp/accounts/transactions?${query}`,
      {
        method: "GET",
        headers: {
          Authorization: `Bearer ${jwt}`,
          Accept: "application/json",
        },
        dispatcher: insecureDispatcher,
      },
    );

    const body = await response.text();

    return new Response(body, {
      status: response.status,
      headers: {
        "Content-Type":
          response.headers.get("Content-Type") ?? "application/json",
      },
    });
  } catch (errno) {
    console.error("🔴 SERVER: GET /api/transactions failed:", errno);

    return new Response(
      JSON.stringify({
        success: false,
        message: "Could not connect to banking server",
        error: errno instanceof Error ? errno.message : "Unknown error",
      }),
      {
        status: 503,
        headers: {
          "Content-Type": "application/json",
        },
      },
    );
  }
};
