/**
 * The "continue authorization" page of a platform front end.
 *
 * A relying party sends the browser to `/connect/authorize`. When the browser
 * has no session-bridge cookie yet, the provider redirects to the platform's
 * own page (`Identity:OpenIdConnect:LoginUrl`) with `?returnUrl=<authorize URL>`.
 * That page makes sure the user is signed in, then calls
 * {@link continueOidcAuthorization}, which establishes the bridge and goes back.
 *
 * ★ The return URL comes from the query string, so anyone can craft one. It is
 * only followed when it points at THIS provider's authorize endpoint; anything
 * else would turn the page into an open redirect that runs with a fresh session.
 */

import type { HttpClient } from '../../http/http';
import { ensureOk } from '../../http/response';
import { useOidcSessionApi } from './api';
import { OIDC_CONSENT_DECISION_PARAMETER, OIDC_CONSENT_DENIED_VALUE } from './metadata';

/** Options for {@link continueOidcAuthorization}. */
export interface ContinueOidcAuthorizationOptions {
  /** HTTP client carrying the signed-in user's bearer token. */
  client: HttpClient;
  /** The provider's issuer URL (`https://id.example.com/`). */
  providerBaseUrl: string;
  /** The `returnUrl` the provider put on the page's query string. */
  returnUrl: string;
  /** Navigation hook; defaults to `window.location.assign`. */
  navigate?: (url: string) => void;
}

/**
 * Returns the return URL when it is this provider's authorize endpoint, otherwise null.
 */
export function validateOidcReturnUrl(returnUrl: string | null | undefined, providerBaseUrl: string): string | null {
  if (!returnUrl) return null;

  let target: URL;
  let provider: URL;
  try {
    target = new URL(returnUrl);
    provider = new URL(providerBaseUrl.endsWith('/') ? providerBaseUrl : `${providerBaseUrl}/`);
  } catch {
    return null;
  }

  const authorizePath = new URL('connect/authorize', provider).pathname;
  return target.origin === provider.origin && target.pathname === authorizePath ? target.toString() : null;
}

/**
 * Establish the session bridge and go back to the authorize endpoint.
 *
 * @throws Error when the return URL is not this provider's authorize endpoint,
 *   or the bridge could not be established (the user's platform session is gone).
 */
export async function continueOidcAuthorization(options: ContinueOidcAuthorizationOptions): Promise<void> {
  const target = validateOidcReturnUrl(options.returnUrl, options.providerBaseUrl);
  if (!target) {
    throw new Error('The return URL does not point at this identity provider.');
  }

  ensureOk(await useOidcSessionApi(options.client, options.providerBaseUrl).establish());
  (options.navigate ?? defaultNavigate)(target);
}

/**
 * The URL a consent page navigates to when the user declines: the authorize
 * request with the decision appended, which the provider turns into an
 * `access_denied` answer to the relying party.
 */
export function buildOidcConsentDeniedUrl(returnUrl: string): string {
  const url = new URL(returnUrl);
  url.searchParams.set(OIDC_CONSENT_DECISION_PARAMETER, OIDC_CONSENT_DENIED_VALUE);
  return url.toString();
}

function defaultNavigate(url: string): void {
  if (typeof window !== 'undefined') {
    window.location.assign(url);
  }
}
