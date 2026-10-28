/**
 * Display labels for the signing enums.
 *
 * The enums are string enums whose values are the PascalCase member names
 * (`InProgress`, `Viewed`, `Signature`). Rendering the value itself left every
 * status chip, filter option, recipient badge and field type in English in the
 * zh-cn admin. Labels live under `admin.modules.signing.enums`; a member the
 * dictionary does not know yet (a newer backend) falls back to its humanised
 * name rather than an empty string or a dotted key.
 */
import type {
  EnvelopeStatus,
  FieldPlacementMode,
  SigningFieldType,
  SigningRecipientStatus,
  TemplateSource,
} from '@tnzi/core/services/signing'
import { translatePageKey } from '../_shared/translate'

export type SigningEnumGroup = 'envelopeStatus' | 'recipientStatus' | 'fieldType' | 'placement' | 'templateSource'

/** The absolute i18n key of one enum member (usable as a schema option `labelKey`). */
export function signingEnumKey(group: SigningEnumGroup, value: string): string {
  return `admin.modules.signing.enums.${group}.${value}`
}

function label(group: SigningEnumGroup, value: string | null | undefined): string {
  if (value == null || value === '') return ''
  return translatePageKey('', signingEnumKey(group, String(value)))
}

export const envelopeStatusLabel = (value?: EnvelopeStatus | null): string => label('envelopeStatus', value)

export const recipientStatusLabel = (value?: SigningRecipientStatus | null): string => label('recipientStatus', value)

export const fieldTypeLabel = (value?: SigningFieldType | null): string => label('fieldType', value)

export const placementLabel = (value?: FieldPlacementMode | null): string => label('placement', value)

export const templateSourceLabel = (value?: TemplateSource | null): string => label('templateSource', value)
