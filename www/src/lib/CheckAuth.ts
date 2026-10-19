import { backendUrl } from "../utils/backend.ts";

export async function isAuthorized()
{
    try
    {
        const response = await fetch(`${backendUrl}/oauth/users/session`,
        {
            method: "GET",
            credentials: "include",
            headers:
            {
                "Content-Type": "application/json"
            },
            redirect: "manual"
        });
        console.log(response.status);
        return response.status === 200;
    }
    catch (error)
    {
        console.error("[-] Session check failed:", error);
    }
}
