/**
 * OpenID Connect provider API - `Tnzi.Identity.OpenIdConnect`.
 *
 * Three factories because the backend keeps three separate surfaces:
 *
 * - {@link useAdminOidcApi} - `/admin/oidc/*`: the client registry and the grant
 *   list, permission-gated (`identity.oidcClient.*` / `identity.oidcGrant.*`).
 * - {@link useOidcApi} - `/oidc/*`: the signed-in user's own grants and the two
 *   consent-page endpoints.
 * - {@link useOidcSessionApi} - `/connect/session`: the session bridge. These
 *   routes sit OUTSIDE the API prefix (relying parties and reverse proxies reach
 *   them at fixed paths), so the factory takes the provider's base URL.
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  CreateOidcClientDto,
  GrantOidcConsentDto,
  OidcClientDto,
  OidcClientQueryDto,
  OidcClientSecretDto,
  OidcConsentInfoDto,
  OidcGrantDto,
  OidcGrantQueryDto,
  UpdateOidcClientDto,
} from './types';

// Aligned with the backend controller routes.
const ADMIN_CLIENT_BASE = '/admin/oidc/clients';
const ADMIN_GRANT_BASE = '/admin/oidc/grants';
const USER_BASE = '/oidc';

/** Admin OIDC API - client registry and grants. */
export function useAdminOidcApi(client: HttpClient) {
  return {
    /** List clients (paged). */
    getClients: (params?: OidcClientQueryDto) =>
      client.get<PagedList<OidcClientDto>>(ADMIN_CLIENT_BASE, { params }),

    /** One client. */
    getClient: (id: string) =>
      client.get<OidcClientDto>(`${ADMIN_CLIENT_BASE}/${encodeURIComponent(id)}`),

    /**
     * Register a client.
     *
     * ★ For a confidential client the plaintext secret is in the result and
     * nowhere else; the server keeps a hash.
     */
    createClient: (data: CreateOidcClientDto) =>
      client.post<OidcClientSecretDto>(ADMIN_CLIENT_BASE, data),

    /** Update a client. */
    updateClient: (id: string, data: UpdateOidcClientDto) =>
      client.put<OidcClientDto>(`${ADMIN_CLIENT_BASE}/${encodeURIComponent(id)}`, data),

    /**
     * Enable or disable a client. Disabling revokes every grant and token under
     * it; enabling again does not bring them back.
     */
    setClientEnabled: (id: string, enabled: boolean) =>
      client.put<void>(`${ADMIN_CLIENT_BASE}/${encodeURIComponent(id)}/enabled`, { enabled }),

    /** Issue a new secret; the old one stops working immediately. */
    rotateClientSecret: (id: string) =>
      client.post<OidcClientSecretDto>(`${ADMIN_CLIENT_BASE}/${encodeURIComponent(id)}/rotate-secret`),

    /** Delete a client together with its grants and tokens. */
    deleteClient: (id: string) =>
      client.delete<void>(`${ADMIN_CLIENT_BASE}/${encodeURIComponent(id)}`),

    /** List grants (paged). */
    getGrants: (params?: OidcGrantQueryDto) =>
      client.get<PagedList<OidcGrantDto>>(ADMIN_GRANT_BASE, { params }),

    /** Revoke one grant; refresh tokens under it stop working. */
    revokeGrant: (id: string) =>
      client.delete<void>(`${ADMIN_GRANT_BASE}/${encodeURIComponent(id)}`),
  };
}

/** Signed-in user's OIDC API - own grants and the consent page. */
export function useOidcApi(client: HttpClient) {
  return {
    /** Applications the current user has authorized. */
    getMyGrants: () => client.get<OidcGrantDto[]>(`${USER_BASE}/grants`),

    /** Revoke every grant the current user gave one client. */
    revokeMyGrants: (clientId: string) =>
      client.delete<void>(`${USER_BASE}/grants/${encodeURIComponent(clientId)}`),

    /** What the consent page shows for a client and a requested scope string. */
    getConsentInfo: (clientId: string, scope?: string) =>
      client.get<OidcConsentInfoDto>(`${USER_BASE}/consents/${encodeURIComponent(clientId)}`, {
        params: scope ? { scope } : undefined,
      }),

    /** Approve the consent page; then navigate back to the return URL. */
    grantConsent: (data: GrantOidcConsentDto) => client.post<void>(`${USER_BASE}/consents`, data),
  };
}

/**
 * Session bridge API.
 *
 * @param providerBaseUrl Absolute base URL of the provider, i.e. the issuer
 *   (`https://id.example.com/`). The bridge routes are `connect/session` under it.
 */
export function useOidcSessionApi(client: HttpClient, providerBaseUrl: string) {
  const sessionUrl = new URL('connect/session', ensureTrailingSlash(providerBaseUrl)).toString();
  return {
    /**
     * Exchange the current bearer session for the bridge cookie the authorize
     * endpoint recognises. `withCredentials` is required when the provider is a
     * different origin, otherwise the browser drops the `Set-Cookie`.
     */
    // contract-scan-exempt: `/connect/session` sits outside the API prefix and is resolved against the provider's issuer URL at runtime.
    establish: () => client.post<void>(sessionUrl, undefined, { withCredentials: true }),

    /** Clear the bridge cookie (call on sign-out). */
    // contract-scan-exempt: same runtime-resolved `/connect/session` route as `establish`.
    clear: () => client.delete<void>(sessionUrl, { withCredentials: true }),
  };
}

function ensureTrailingSlash(url: string): string {
  return url.endsWith('/') ? url : `${url}/`;
}
