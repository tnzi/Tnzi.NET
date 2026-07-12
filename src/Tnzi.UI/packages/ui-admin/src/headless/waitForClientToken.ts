/**
 * Wait until an HttpClient carries an access token.
 *
 * Consumers typically restore the core auth session asynchronously (e.g. in
 * a router guard), which lands AFTER app install - firing an authenticated
 * request immediately would send it without an Authorization header and
 * produce guaranteed 401 console noise on every reload of a signed-in
 * session. Poll the cheap in-memory token accessor instead and give up
 * quietly when no session ever materialises.
 *
 * Returns true when a token is present (or the client has no
 * `getAccessToken` accessor - older clients keep the fire-immediately
 * behavior), false when the deadline passed without one.
 */
export async function waitForClientToken(client: unknown, timeoutMs = 15_000): Promise<boolean> {
  const c = client as { getAccessToken?: () => string | null } | null | undefined
  if (!c || typeof c.getAccessToken !== 'function') return true
  const deadline = Date.now() + timeoutMs
  while (!c.getAccessToken()) {
    if (Date.now() > deadline) return false
    await new Promise((resolve) => setTimeout(resolve, 250))
  }
  return true
}
