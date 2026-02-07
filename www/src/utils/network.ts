export async function fetchWithTimeout(url: string, options = {}, timeout = 5000)
{
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeout);

  try 
  {
    const response = await fetch(url,
    {
      ...options,
      signal: controller.signal
    });
    clearTimeout(timeoutId);
    return response;
  }
  catch (error)
  {
    clearTimeout(timeoutId);
    throw error;
  }
}
