import type { APIRoute } from "astro";
import { Agent } from "undici";

const insecureDispatcher = new Agent({
    connect: {
        rejectUnauthorized: false,
    },
});

export const prerender = false;

export const GET: APIRoute = async ({ cookies }) => {
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

        const response = await fetch(
            "https://localhost:4446/api/v1/aspsp/accounts/balances",
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
        console.error("🔴 SERVER: GET /api/accounts/balances failed:", errno);

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