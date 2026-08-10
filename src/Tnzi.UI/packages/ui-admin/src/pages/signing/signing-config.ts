/**
 * Signing config - columns, form schemas and the status vocabularies shared by
 * the requests and templates pages.
 *
 * Backend: `Tnzi.Signing`'s `DefaultSigningRequestAdminController`
 * (`/admin/signing/requests`) and `DefaultSigningTemplateAdminController`
 * (`/admin/signing/templates`).
 */
import {
  EnvelopeStatus,
  FieldPlacementMode,
  SigningFieldType,
  SigningRecipientStatus,
  TemplateSource,
  type EnvelopeListDto,
  type EnvelopeTemplateListDto,
} from '@tnzi/core/services/signing'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem, FormSchemaSection } from '../_shared/form-schema'

type StatusTone = 'success' | 'warning' | 'error' | 'info' | 'default'

/**
 * A request's state as a colour.
 *
 * Declined / Expired are `warning`, not `error`: neither is a malfunction. One
 * is a person exercising their right to say no, the other is a deadline doing
 * what deadlines do. `error` is reserved for "something went wrong", and
 * painting these red teaches operators to ignore red.
 */
export function envelopeStatusTone(status?: EnvelopeStatus | null): StatusTone {
  switch (status) {
    case EnvelopeStatus.Completed: return 'success'
    case EnvelopeStatus.InProgress: return 'info'
    case EnvelopeStatus.Sent: return 'info'
    case EnvelopeStatus.Declined:
    case EnvelopeStatus.Expired: return 'warning'
    case EnvelopeStatus.Voided: return 'default'
    default: return 'default'
  }
}

export function recipientStatusTone(status?: SigningRecipientStatus | null): StatusTone {
  switch (status) {
    case SigningRecipientStatus.Signed: return 'success'
    case SigningRecipientStatus.Viewed: return 'info'
    case SigningRecipientStatus.Sent: return 'warning'
    case SigningRecipientStatus.Declined: return 'warning'
    default: return 'default'
  }
}

/** Status filter options for the requests list. */
export const ENVELOPE_STATUS_OPTIONS = [
  EnvelopeStatus.Draft,
  EnvelopeStatus.Sent,
  EnvelopeStatus.InProgress,
  EnvelopeStatus.Completed,
  EnvelopeStatus.Declined,
  EnvelopeStatus.Expired,
  EnvelopeStatus.Voided,
] as const

const TEMPLATE_SOURCE_OPTIONS = [TemplateSource.Composed, TemplateSource.Uploaded] as const

export const SIGNING_FIELD_TYPE_OPTIONS = [
  SigningFieldType.Text,
  SigningFieldType.Date,
  SigningFieldType.Number,
  SigningFieldType.Checkbox,
  SigningFieldType.Signature,
  SigningFieldType.Initials,
] as const

export const FIELD_PLACEMENT_OPTIONS = [FieldPlacementMode.Absolute, FieldPlacementMode.Anchor] as const

/**
 * Columns exist because `useCrudPage` needs them for column settings and the
 * mobile card fallback; both signing pages render their own shapes (document
 * rows / tiles) rather than a grid.
 */
export const envelopeColumns: ColumnDef<Partial<EnvelopeListDto>>[] = [
  { key: 'title', title: 'columns.title', minWidth: 200, primary: true },
  { key: 'status', title: 'columns.status', width: 120 },
  { key: 'recipientCount', title: 'columns.progress', width: 110 },
  { key: 'expiresAt', title: 'columns.expiresAt', width: 160 },
  { key: 'creationTime', title: 'columns.createdAt', width: 160 },
]

export const templateColumns: ColumnDef<Partial<EnvelopeTemplateListDto>>[] = [
  { key: 'name', title: 'columns.name', minWidth: 200, primary: true },
  { key: 'category', title: 'columns.category', width: 140 },
  { key: 'source', title: 'columns.source', width: 120 },
  { key: 'fieldCount', title: 'columns.fieldCount', width: 100 },
  { key: 'isActive', title: 'columns.isActive', width: 100 },
]

/**
 * Template metadata form. The placed fields are NOT here - they need a
 * per-row editor, so the page supplies one through a field renderer.
 */
export const templateFormSections: FormSchemaSection[] = [
  { key: 'basics', labelKey: 'admin.shared.formSections.basics' },
  { key: 'source', labelKey: 'admin.shared.formSections.content' },
  { key: 'fields', labelKey: 'sections.fields' },
]

export const templateFormSchema: FormSchemaItem[] = [
  { key: 'name', label: 'form.name', type: 'input', required: true, section: 'basics' },
  { key: 'category', label: 'form.category', type: 'input', section: 'basics' },
  {
    key: 'source',
    label: 'form.source',
    type: 'select',
    required: true,
    section: 'basics',
    options: TEMPLATE_SOURCE_OPTIONS.map((v) => ({ label: v, value: v })),
  },
  { key: 'isActive', label: 'form.isActive', type: 'switch', section: 'basics' },
  {
    key: 'requiresWetSignature',
    label: 'form.requiresWetSignature',
    type: 'switch',
    section: 'basics',
  },
  // Which host record types may use this template. Empty means "any" - and the
  // backend treats it that way, so leaving it blank is a real answer, not a
  // missing one.
  { key: 'hostEntityTypes', label: 'form.hostEntityTypes', type: 'input', section: 'basics' },
  {
    key: 'bodyTemplate',
    label: 'form.bodyTemplate',
    type: 'textarea',
    section: 'source',
    span: 'full',
  },
  { key: 'pageCount', label: 'form.pageCount', type: 'number', section: 'source', min: 1 },
  { key: 'fields', label: 'form.fields', type: 'custom', section: 'fields' },
]
