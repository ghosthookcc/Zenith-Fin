export async function isAuthorized()
{
    try
    {
        const response = await fetch("https://localhost:4446/api/v1/oauth/users/session",
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
