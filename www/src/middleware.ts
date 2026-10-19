import type { MiddlewareHandler } from "astro";

import { validateSession } from "./utils/backend.ts";

const PUBLIC_ROUTES = [
  "/",
  "/api/login",
  "/api/register",
  "/api/session",
  "/favicon.ico",
];

const LOGGED_IN_LANDING_ROUTE = "/accounts";

export const onRequest: MiddlewareHandler = async (
  { request, redirect, cookies },
  next,
) => {
  const url = new URL(request.url);
  console.log("🔵 MIDDLEWARE: Request to:", url.pathname);

  const jwt = cookies.get("AuthToken")?.value;
  console.log("🔵 MIDDLEWARE: JWT present?", !!jwt);

  const isPublic = PUBLIC_ROUTES.includes(url.pathname);
  const isApi = url.pathname.startsWith("/api/");

  if (isPublic) {
    console.log("🔵 MIDDLEWARE: Public route, skipping auth");
    if (jwt && !isApi && (await validateSession(jwt))) {
      return redirect(LOGGED_IN_LANDING_ROUTE);
    }
    return next();
  }

  if (!jwt) {
    console.log("🔵 MIDDLEWARE: No JWT, redirecting to /");
    return redirect("/");
  }

  const session = await validateSession(jwt);

  if (!session) {
    console.log("🔵 MIDDLEWARE: Session invalid, redirecting");
    return redirect("/");
  }

  console.log("🔵 MIDDLEWARE: Session valid, user:", session.userId);

  (request as any).user = session.userId;

  return next();
};
