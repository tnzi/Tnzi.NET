/**
 * OpenID Connect provider metadata - mirrors `Tnzi.Identity.OpenIdConnect/Metadata/OidcConstants.cs`.
 *
 * ★ STRING enums (member name = value): the backend registers a global
 * `JsonStringEnumConverter`, so these arrive on the wire as names.
 */

/** Client type. */
export enum OidcClientType {
  /** Has a server and can keep a secret; the token endpoint checks `client_secret`. */
  Confidential = 'Confidential',
  /** Browser or native app without a secret; PKCE is always required. */
  Public = 'Public',
}

/** How user consent is obtained. */
export enum OidcConsentType {
  /** No consent screen (first-party apps). */
  Implicit = 'Implicit',
  /** The user confirms once on a consent page; the grant is remembered until revoked. */
  Explicit = 'Explicit',
}

/** Scopes the provider always supports. Additional scopes are configured server-side. */
export const OIDC_STANDARD_SCOPES = ['openid', 'profile', 'email', 'offline_access'] as const

/**
 * Query parameter the consent page appends to the return URL when the user
 * declines. It can only make an authorization fail, so it needs no signature.
 */
export const OIDC_CONSENT_DECISION_PARAMETER = 'tnzi_consent'

/** Value of {@link OIDC_CONSENT_DECISION_PARAMETER} for a declined request. */
export const OIDC_CONSENT_DENIED_VALUE = 'denied'
