import type { AstroCookies } from "astro";
import { Agent } from "undici";

import { fetchWithTimeout } from "./network.ts";

const insecureDispatcher = new Agent({
    connect: {
        rejectUnauthorized: false,
    },
});

const backendUrl = "https://localhost:4446/api/v1";

function json(status: number, body: unknown): Response {
    return new Response(JSON.stringify(body), {
        status,
        headers: {
            "Content-Type": "application/json",
        },
    });
}

export async function forwardToBackend(
    cookies: AstroCookies,
    method: string,
    path: string,
    body?: string,
): Promise<Response> {
    const jwt = cookies.get("AuthToken")?.value;

    if (!jwt) {
        return json(401, {
            success: false,
            message: "No token provided",
        });
    }

    try {
        const response = await fetchWithTimeout(`${backendUrl}${path}`, {
            method,
            headers: {
                Authorization: `Bearer ${jwt}`,
                Accept: "application/json",
                ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
            },
            body,
            dispatcher: insecureDispatcher,
        });

        const text = await response.text();

        return new Response(text, {
            status: response.status,
            headers: {
                "Content-Type":
                    response.headers.get("Content-Type") ?? "application/json",
            },
        });
    } catch (errno) {
        console.error(`🔴 SERVER: ${method} ${path} failed:`, errno);

        return json(503, {
            success: false,
            message: "Could not connect to banking server",
            error: errno instanceof Error ? errno.message : "Unknown error",
        });
    }
}
