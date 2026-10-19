import type { APIRoute } from "astro";
import { Agent } from "undici";

import { fetchWithTimeout } from "../../../utils/network.ts";
import { backendUrl } from "../../../utils/backend.ts";

const insecureDispatcher = new Agent({
  connect: {
    rejectUnauthorized: false,
  },
});

export const prerender = false;

export const POST: APIRoute = async ({ cookies, url }) => {
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

    const force = url.searchParams.get("force") === "true";

    const response = await fetchWithTimeout(
      `${backendUrl}/aspsp/accounts/transactions/sync?force=${force}`,
      {
        method: "POST",
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
    console.error("🔴 SERVER: POST /api/transactions/sync failed:", errno);

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
