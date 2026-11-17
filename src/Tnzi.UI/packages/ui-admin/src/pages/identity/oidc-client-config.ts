import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { formatDateTime } from '@tnzi/core'
import {
  OidcClientType,
  OidcConsentType,
  type CreateOidcClientDto,
  type OidcClientDto,
  type OidcGrantDto,
  type UpdateOidcClientDto,
} from '@tnzi/core/services/oidc'

/**
 * OpenID Connect client registry config - aligned with OidcClientDto
 * (Tnzi.Identity.OpenIdConnect, Identity_OidcClient).
 *
 * Redirect URIs and scopes are lists on the wire but are edited as text (one
 * URI per line, scopes separated by spaces): the form schema has no list field,
 * and a textarea is what an operator pastes a callback list into anyway. The
 * conversion lives here, next to the form that needs it, as pure functions.
 */

/** Form model: the DTO plus the text forms of its list fields. */
export interface OidcClientFormModel extends Partial<OidcClientDto> {
  redirectUrisText?: string
  postLogoutRedirectUrisText?: string
  scopesText?: string
}

export const OIDC_CLIENT_ENABLED_BADGE = {
  true: { type: 'success' as const, labelKey: 'tnzi.admin.modules.identity.oidcClients.status.enabled' },
  false: { type: 'default' as const, labelKey: 'tnzi.admin.modules.identity.oidcClients.status.disabled' },
}

/**
 * Column definitions. The page renders tiles, not a table; these still name the
 * fields for the shell (column settings, export) and the phone card fallback.
 */
export const oidcClientColumns: ColumnDef<OidcClientDto>[] = [
  { key: 'displayName', title: 'columns.displayName', primary: true, render: (row) => row.displayName || EMPTY_DASH },
  { key: 'clientId', title: 'columns.clientId' },
  { key: 'clientType', title: 'columns.clientType' },
  { key: 'consentType', title: 'columns.consentType' },
  {
    key: 'isEnabled',
    title: 'columns.status',
    render: (row) => h(TStatusBadge, { value: row.isEnabled, mapping: OIDC_CLIENT_ENABLED_BADGE }),
  },
  { key: 'scopes', title: 'columns.scopes', render: (row) => (row.scopes?.length ? row.scopes.join(' ') : EMPTY_DASH) },
  { key: 'externalReference', title: 'columns.externalReference', visible: false, render: (row) => row.externalReference || EMPTY_DASH },
  { key: 'creationTime', title: 'columns.creationTime', render: (row) => formatDateTime(row.creationTime) },
]

/** Create / edit form. `clientId` and `clientType` only appear on create: changing either is a different client. */
export const oidcClientFormSchema: FormSchemaItem[] = [
  { key: 'displayName', labelKey: 'form.displayName', label: 'Display name', type: 'text', required: true },
  {
    key: 'clientId',
    labelKey: 'form.clientId',
    label: 'Client ID',
    type: 'text',
    placeholderKey: 'form.clientIdPlaceholder',
    visible: (model) => !model.id,
  },
  {
    key: 'clientType',
    labelKey: 'form.clientType',
    label: 'Client type',
    type: 'select',
    required: true,
    visible: (model) => !model.id,
    options: [
      { label: 'Confidential', value: OidcClientType.Confidential, labelKey: 'clientTypes.Confidential' },
      { label: 'Public', value: OidcClientType.Public, labelKey: 'clientTypes.Public' },
    ],
  },
  {
    key: 'consentType',
    labelKey: 'form.consentType',
    label: 'Consent',
    type: 'select',
    required: true,
    options: [
      { label: 'Ask the user', value: OidcConsentType.Explicit, labelKey: 'consentTypes.Explicit' },
      { label: 'First-party, no prompt', value: OidcConsentType.Implicit, labelKey: 'consentTypes.Implicit' },
    ],
  },
  {
    key: 'requirePkce',
    labelKey: 'form.requirePkce',
    label: 'Require PKCE',
    type: 'switch',
    // Public clients always require it; the switch would be a control that changes nothing.
    visible: (model) => model.clientType !== OidcClientType.Public,
  },
  { key: 'redirectUrisText', labelKey: 'form.redirectUris', label: 'Redirect URIs (one per line)', type: 'textarea', required: true, span: 'full' },
  { key: 'postLogoutRedirectUrisText', labelKey: 'form.postLogoutRedirectUris', label: 'Post-logout redirect URIs (one per line)', type: 'textarea', span: 'full' },
  { key: 'scopesText', labelKey: 'form.scopes', label: 'Scopes (space-separated)', type: 'text', placeholderKey: 'form.scopesPlaceholder', span: 'full' },
  { key: 'externalReference', labelKey: 'form.externalReference', label: 'External reference', type: 'text' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea', span: 'full' },
]

/** Defaults for a new client: confidential, explicit consent, PKCE on, the two common scopes. */
export const oidcClientCreateDefaults: OidcClientFormModel = {
  clientType: OidcClientType.Confidential,
  consentType: OidcConsentType.Explicit,
  requirePkce: true,
  scopesText: 'profile email',
}

/** Lines → list, dropping blanks and surrounding whitespace. */
export function splitLines(text: string | undefined | null): string[] {
  return (text ?? '').split(/\r?\n/).map((s) => s.trim()).filter((s) => s.length > 0)
}

/** Whitespace / comma separated → list. */
export function splitScopes(text: string | undefined | null): string[] {
  return (text ?? '').split(/[\s,]+/).map((s) => s.trim()).filter((s) => s.length > 0)
}

/** A row as the edit form wants it. */
export function toOidcClientFormModel(row: OidcClientDto): OidcClientFormModel {
  return {
    ...row,
    redirectUrisText: (row.redirectUris ?? []).join('\n'),
    postLogoutRedirectUrisText: (row.postLogoutRedirectUris ?? []).join('\n'),
    scopesText: (row.scopes ?? []).join(' '),
  }
}

export function toCreateOidcClientDto(model: OidcClientFormModel): CreateOidcClientDto {
  return {
    clientId: model.clientId?.trim() || null,
    displayName: (model.displayName ?? '').trim(),
    description: model.description || null,
    clientType: model.clientType ?? OidcClientType.Confidential,
    consentType: model.consentType ?? OidcConsentType.Explicit,
    requirePkce: model.requirePkce ?? true,
    redirectUris: splitLines(model.redirectUrisText),
    postLogoutRedirectUris: splitLines(model.postLogoutRedirectUrisText),
    scopes: splitScopes(model.scopesText),
    externalReference: model.externalReference || null,
  }
}

export function toUpdateOidcClientDto(model: OidcClientFormModel): UpdateOidcClientDto {
  return {
    displayName: (model.displayName ?? '').trim(),
    description: model.description || null,
    consentType: model.consentType ?? OidcConsentType.Explicit,
    requirePkce: model.requirePkce ?? true,
    redirectUris: splitLines(model.redirectUrisText),
    postLogoutRedirectUris: splitLines(model.postLogoutRedirectUrisText),
    scopes: splitScopes(model.scopesText),
    externalReference: model.externalReference || null,
  }
}

export const OIDC_GRANT_STATUS_BADGE = {
  valid: { type: 'success' as const, labelKey: 'tnzi.admin.modules.identity.oidcGrants.status.valid' },
  revoked: { type: 'default' as const, labelKey: 'tnzi.admin.modules.identity.oidcGrants.status.revoked' },
}

/**
 * Who a grant belongs to, as an administrator reads it: the person's name and
 * email. The id is the fallback for an account that no longer resolves.
 */
export function grantPersonLabel(row: OidcGrantDto): string {
  const name = row.userDisplayName?.trim()
  const email = row.userEmail?.trim()
  if (name && email && name !== email) return `${name} · ${email}`
  return name || email || row.userId
}

export function grantClientLabel(row: OidcGrantDto): string {
  return row.clientDisplayName || row.clientId
}

export function grantScopesLabel(row: OidcGrantDto): string {
  return row.scopes?.length ? row.scopes.join(' ') : EMPTY_DASH
}

/**
 * Column definitions. The page renders document rows, not a table; these still
 * name the fields for the shell (column settings, export).
 */
export const oidcGrantColumns: ColumnDef<OidcGrantDto>[] = [
  { key: 'clientDisplayName', title: 'columns.client', primary: true, render: (row) => grantClientLabel(row) },
  { key: 'userDisplayName', title: 'columns.user', render: (row) => grantPersonLabel(row) },
  { key: 'scopes', title: 'columns.scopes', render: (row) => grantScopesLabel(row) },
  {
    key: 'status',
    title: 'columns.status',
    render: (row) => h(TStatusBadge, { value: row.status ?? '', mapping: OIDC_GRANT_STATUS_BADGE }),
  },
  { key: 'creationDate', title: 'columns.creationDate', render: (row) => formatDateTime(row.creationDate) },
  { key: 'userId', title: 'columns.userId', visible: false },
]

/** Advanced search: the two filters the grant query takes besides the user id. */
export const oidcGrantSearchFields: FormSchemaItem[] = [
  { key: 'clientId', labelKey: 'search.clientId', label: 'Client ID', type: 'text' },
  { key: 'includeRevoked', labelKey: 'search.includeRevoked', label: 'Include revoked', type: 'switch' },
]
