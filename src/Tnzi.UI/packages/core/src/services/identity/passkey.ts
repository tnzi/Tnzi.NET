/**
 * Passkey (WebAuthn) browser helpers.
 *
 * The two-step ceremony is identical in every consuming app - parse the options
 * the server produced, call `navigator.credentials`, serialise the result back.
 * Left to each app, that becomes the same twenty lines of base64url plumbing
 * copied around, so it is collapsed here.
 *
 * Uses the native `parseCreationOptionsFromJSON` / `parseRequestOptionsFromJSON`
 * / `toJSON` bridges, which exist precisely to avoid hand-rolled ArrayBuffer
 * conversion. Call `isPasskeySupported()` first: browsers without them get a
 * clear "unsupported" answer instead of a TypeError halfway through the flow.
 */

import type { HttpClient } from '../../http/http';
import { useAuthApi } from './api';
import type { AuthApiOptions } from './api';
import type { PasskeyCredentialDto, TokenResultDto } from './types';

/**
 * Whether this browser can run the passkey ceremony.
 *
 * Checks the JSON bridge methods rather than just `PublicKeyCredential`:
 * the credential type has been around far longer than the bridges, so testing
 * for it alone reports "supported" on browsers that then fail at parse time.
 */
export function isPasskeySupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof window.PublicKeyCredential !== 'undefined' &&
    typeof (window.PublicKeyCredential as unknown as Record<string, unknown>)
      .parseCreationOptionsFromJSON === 'function' &&
    typeof (window.PublicKeyCredential as unknown as Record<string, unknown>)
      .parseRequestOptionsFromJSON === 'function'
  );
}

/** Thrown when the ceremony cannot even be attempted. */
export class PasskeyUnsupportedError extends Error {
  constructor() {
    super('This browser does not support passkeys.');
    this.name = 'PasskeyUnsupportedError';
  }
}

/**
 * Register a passkey for the current user, or for the user an enrollment token
 * points at.
 *
 * @returns The stored credential, or `null` when the user dismissed the system
 *   dialog. Dismissal is a normal outcome, not an error - do not surface it as
 *   a failure.
 */
export async function registerPasskey(
  client: HttpClient,
  options: { enrollmentToken?: string; deviceName?: string } = {},
): Promise<PasskeyCredentialDto | null> {
  if (!isPasskeySupported()) {
    throw new PasskeyUnsupportedError();
  }

  const api = useAuthApi(client);
  const begun = ensureData(await api.beginPasskeyRegistration(options.enrollmentToken));

  const creationOptions = PublicKeyCredential.parseCreationOptionsFromJSON(
    JSON.parse(begun.optionsJson),
  );

  const credential = (await navigator.credentials.create({
    publicKey: creationOptions,
  })) as PublicKeyCredential | null;

  if (!credential) {
    return null;
  }

  const completed = await api.completePasskeyRegistration(
    {
      stateId: begun.stateId,
      credentialJson: JSON.stringify(credential.toJSON()),
      deviceName: options.deviceName,
    },
    options.enrollmentToken,
  );

  return ensureData(completed);
}

/**
 * Sign in with a passkey.
 *
 * @param userName Omit for a discoverable-credential flow (no username field on
 *   the login page at all).
 * @returns The token result, or `null` when the user dismissed the system dialog.
 *
 * The response can still be a `2FA_REQUIRED` failure when the account has
 * two-factor enabled - passkey sign-in goes through the same issuance path as
 * password login, so continue with the existing 2FA flow rather than treating
 * that as a passkey error.
 *
 * @param options Forwarded to `useAuthApi`. The completing call issues the
 *   session, so a cross-origin cookie-mode deployment must pass
 *   `{ withCredentials: true }` here exactly as it does for password login -
 *   otherwise the browser drops the refresh cookie and the session dies at
 *   its first refresh.
 */
export async function signInWithPasskey(
  client: HttpClient,
  userName?: string,
  options: AuthApiOptions = {},
): Promise<TokenResultDto | null> {
  if (!isPasskeySupported()) {
    throw new PasskeyUnsupportedError();
  }

  const api = useAuthApi(client, options);
  const begun = ensureData(await api.beginPasskeyAssertion(userName ? { userName } : undefined));

  const requestOptions = PublicKeyCredential.parseRequestOptionsFromJSON(
    JSON.parse(begun.optionsJson),
  );

  const credential = (await navigator.credentials.get({
    publicKey: requestOptions,
  })) as PublicKeyCredential | null;

  if (!credential) {
    return null;
  }

  const completed = await api.completePasskeyAssertion({
    stateId: begun.stateId,
    credentialJson: JSON.stringify(credential.toJSON()),
  });

  return ensureData(completed);
}

/**
 * Unwrap an envelope, throwing on failure.
 *
 * The ceremony is a chain: a failed `begin` must not fall through to
 * `navigator.credentials`, where it would surface as an opaque browser error
 * instead of the server's actual message.
 */
function ensureData<T>(result: { succeeded?: boolean; data?: T; message?: string }): T {
  if (result?.succeeded !== true || result.data == null) {
    throw new Error(result?.message || 'Passkey request failed');
  }

  return result.data;
}
