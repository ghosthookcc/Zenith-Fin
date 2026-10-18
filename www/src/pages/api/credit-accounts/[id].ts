import type { APIRoute } from "astro";

import { forwardToBackend } from "../../../utils/backend.ts";

export const prerender = false;

function invalidId(): Response {
    return new Response(
        JSON.stringify({
            success: false,
            message: "Unknown credit account",
        }),
        {
            status: 404,
            headers: {
                "Content-Type": "application/json",
            },
        },
    );
}

export const PUT: APIRoute = async ({ cookies, params, request }) =>
    params.id && /^\d+$/.test(params.id)
        ? forwardToBackend(cookies, "PUT", `/credit-accounts/${params.id}`, await request.text())
        : invalidId();

export const DELETE: APIRoute = async ({ cookies, params }) =>
    params.id && /^\d+$/.test(params.id)
        ? forwardToBackend(cookies, "DELETE", `/credit-accounts/${params.id}`)
        : invalidId();
