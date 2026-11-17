/**
 * OpenID Connect provider DTOs - mirror `Tnzi.Identity.OpenIdConnect/Dtos/*.cs`.
 */

import type { OidcClientType, OidcConsentType } from './metadata';

/** A registered client (relying-party application). */
export interface OidcClientDto {
  id: string;
  clientId: string;
  displayName?: string | null;
  description?: string | null;
  clientType: OidcClientType;
  consentType: OidcConsentType;
  isEnabled: boolean;
  /** Always true for public clients. */
  requirePkce: boolean;
  redirectUris: string[];
  postLogoutRedirectUris: string[];
  /** Scopes the client may request (`openid` is always allowed and not listed). */
  scopes: string[];
  externalReference?: string | null;
  creationTime: string;
}

/**
 * Result of creating a client or rotating its secret.
 *
 * ★ `clientSecret` is the plaintext secret and is returned only here; the
 * server stores a hash. Lost secrets can only be rotated.
 */
export interface OidcClientSecretDto {
  client: OidcClientDto;
  clientSecret?: string | null;
}

/** Create a client. */
export interface CreateOidcClientDto {
  /** Leave empty to have one generated. Letters, digits, `-`, `_`, `.`. */
  clientId?: string | null;
  displayName: string;
  description?: string | null;
  clientType?: OidcClientType;
  /** Defaults to Explicit; first-party apps choose Implicit explicitly. */
  consentType?: OidcConsentType;
  /** Defaults to true; ignored (always true) for public clients. */
  requirePkce?: boolean;
  redirectUris: string[];
  postLogoutRedirectUris?: string[] | null;
  /** Defaults to `profile` + `email`. */
  scopes?: string[] | null;
  externalReference?: string | null;
}

/** Update a client. `clientId` and the client type cannot change. */
export interface UpdateOidcClientDto {
  displayName: string;
  description?: string | null;
  consentType: OidcConsentType;
  requirePkce?: boolean;
  redirectUris: string[];
  postLogoutRedirectUris?: string[] | null;
  scopes?: string[] | null;
  externalReference?: string | null;
}

/** Paged client query. */
export interface OidcClientQueryDto {
  pageIndex?: number;
  pageSize?: number;
  keyword?: string;
  externalReference?: string;
  isEnabled?: boolean;
}

/** One grant: a user's authorization of a client. */
export interface OidcGrantDto {
  id: string;
  userId: string;
  /**
   * The person's display name (nickname, then full name, then user name).
   * Filled by the admin list only; empty when the account cannot be resolved.
   */
  userDisplayName?: string | null;
  /** The person's account email. Filled by the admin list only. */
  userEmail?: string | null;
  clientId: string;
  clientDisplayName?: string | null;
  scopes: string[];
  /** `valid` or `revoked`. */
  status?: string | null;
  creationDate?: string | null;
}

/** Paged grant query (admin). */
export interface OidcGrantQueryDto {
  pageIndex?: number;
  pageSize?: number;
  userId?: string;
  clientId?: string;
  includeRevoked?: boolean;
}

/** What the consent page shows. */
export interface OidcConsentInfoDto {
  clientId: string;
  displayName?: string | null;
  description?: string | null;
  /** Requested scopes, trimmed to what the client is allowed. */
  scopes: string[];
  /** True when the user already consented; the page can go straight back. */
  alreadyGranted: boolean;
}

/** The user approved the consent page. */
export interface GrantOidcConsentDto {
  clientId: string;
  scopes?: string[] | null;
}
