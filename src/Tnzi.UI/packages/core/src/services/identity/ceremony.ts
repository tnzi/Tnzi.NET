/**
 * The browser half of every WebAuthn ceremony (registration, sign-in, two-factor,
 * step-up). Internal to the identity services: not re-exported from the barrel.
 */

/**
 * Run the browser half of the ceremony, reporting a dismissal as `null`.
 *
 * Browsers do not resolve `navigator.credentials.create/get` with `null` when the
 * user cancels the system dialog: they reject with a `NotAllowedError`
 * DOMException. The same error covers the ceremony timing out and the page losing
 * focus, and the spec deliberately makes them indistinguishable (telling them apart
 * would leak whether a credential exists). All of them mean "the user did not
 * complete it", which is the outcome the `null` return promises, so a caller never
 * shows a browser-worded error for what is a normal choice. Every other rejection
 * (`InvalidStateError` for an authenticator that is already registered, a
 * `SecurityError` for a wrong RP id) is a real failure and propagates.
 */
export async function runCeremony(ceremony: () => Promise<Credential | null>): Promise<Credential | null> {
  try {
    return await ceremony();
  } catch (err) {
    if (isCeremonyDismissal(err)) {
      return null;
    }
    throw err;
  }
}

function isCeremonyDismissal(err: unknown): boolean {
  return typeof err === 'object' && err !== null && (err as { name?: unknown }).name === 'NotAllowedError';
}
