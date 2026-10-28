/**
 * The global `window.$message` handle the headless facilities report through.
 *
 * `TAdminAppRoot` registers it automatically from inside its provider stack;
 * apps not using `TAdminAppRoot` must register their own. The interface is
 * duck-typed so the helper stays decoupled from naive-ui's type surface and
 * works in test environments where the handle isn't registered (it then
 * returns null and the caller's toast is a no-op).
 */
export interface GlobalToastApi {
  error: (content: string) => void
  success?: (content: string) => void
}

export function getGlobalMessage(): GlobalToastApi | null {
  if (typeof window === 'undefined') return null
  const m = (window as unknown as { $message?: unknown }).$message
  if (m && typeof (m as { error?: unknown }).error === 'function') {
    return m as GlobalToastApi
  }
  return null
}
