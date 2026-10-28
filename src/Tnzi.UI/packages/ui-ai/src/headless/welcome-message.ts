/**
 * The message a signed-out visitor typed on the welcome screen, carried across sign-in.
 *
 * The welcome screen (`defineChatApp({ guestWelcome })`) lets a visitor type first and
 * sign in after. What they typed is put here before the sign-in page opens, and
 * `useChatThreads` takes it (once) when the signed-in conversation screen starts, then
 * sends it: the visitor's first message is not lost to the sign-in detour.
 *
 * Session storage, not local storage: the detour happens in the same tab (an OAuth
 * provider sends the browser back to it), and a message typed in one tab must not be sent
 * from another. It also expires (default 30 minutes): a draft abandoned at the sign-in page
 * should not surface as a sent message days later.
 */

const STORAGE_KEY = 'tnzi-ui-ai:welcome-message';
const DEFAULT_MAX_AGE_MS = 30 * 60 * 1000;

interface StoredWelcomeMessage {
  text: string;
  at: number;
}

/** Session storage when the page has one; null in SSR, tests without DOM, or locked-down browsers. */
function sessionStore(): Storage | null {
  try {
    return typeof window !== 'undefined' && window.sessionStorage ? window.sessionStorage : null;
  } catch {
    // Accessing sessionStorage throws in some sandboxed frames
    return null;
  }
}

/**
 * Keep the visitor's message for after sign-in. Returns false when it could not be kept
 * (no session storage): the caller should then not promise it will be sent.
 */
export function stashWelcomeMessage(text: string, storage: Storage | null = sessionStore()): boolean {
  const trimmed = text.trim();
  if (!trimmed || !storage) return false;
  try {
    const value: StoredWelcomeMessage = { text: trimmed, at: Date.now() };
    storage.setItem(STORAGE_KEY, JSON.stringify(value));
    return true;
  } catch {
    return false;
  }
}

/**
 * Take the kept message, removing it: a message is sent at most once. Null when there is
 * none, it is older than `maxAgeMs`, or it cannot be read.
 */
export function takeWelcomeMessage(
  maxAgeMs = DEFAULT_MAX_AGE_MS,
  storage: Storage | null = sessionStore(),
): string | null {
  if (!storage) return null;
  let raw: string | null;
  try {
    raw = storage.getItem(STORAGE_KEY);
    storage.removeItem(STORAGE_KEY);
  } catch {
    return null;
  }
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as Partial<StoredWelcomeMessage>;
    if (typeof parsed.text !== 'string' || typeof parsed.at !== 'number') return null;
    if (Date.now() - parsed.at > maxAgeMs) return null;
    return parsed.text.trim() || null;
  } catch {
    return null;
  }
}
