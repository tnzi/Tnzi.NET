import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import {
  EnvelopeStatus,
  FieldPlacementMode,
  SigningFieldType,
  SigningRecipientStatus,
  TemplateSource,
} from '@tnzi/core/services/signing'
import { en } from '../../../src/locales/en'
import { zhCn } from '../../../src/locales/zh-cn'
import { lookupMessage } from '../../../src/i18n/translate'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'
import {
  envelopeStatusLabel,
  fieldTypeLabel,
  placementLabel,
  recipientStatusLabel,
  signingEnumKey,
  templateSourceLabel,
  type SigningEnumGroup,
} from '../../../src/pages/signing/signing-labels'

// The signing enums are string enums whose values are the member names. The
// pages used to print those values, so the zh-cn admin read 'InProgress',
// 'Viewed', 'Signature' everywhere a status chip, filter or field type showed.
const groups: Array<[SigningEnumGroup, readonly string[]]> = [
  ['envelopeStatus', Object.values(EnvelopeStatus)],
  ['recipientStatus', Object.values(SigningRecipientStatus)],
  ['fieldType', Object.values(SigningFieldType)],
  ['placement', Object.values(FieldPlacementMode)],
  ['templateSource', Object.values(TemplateSource)],
]

describe('signing enum labels', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it.each(groups)('every %s member has a label in both locales', (group, members) => {
    for (const member of members) {
      const key = signingEnumKey(group, member)
      expect(lookupMessage(en as unknown as Record<string, unknown>, key), `en ${key}`).toBeTruthy()
      expect(lookupMessage(zhCn as unknown as Record<string, unknown>, key), `zh-cn ${key}`).toBeTruthy()
    }
  })

  it('resolves through the active locale', () => {
    useAdminAppStore().setLocale('zh-cn')

    expect(envelopeStatusLabel(EnvelopeStatus.InProgress)).toBe('签署中')
    expect(recipientStatusLabel(SigningRecipientStatus.Viewed)).toBe('已查看')
    expect(fieldTypeLabel(SigningFieldType.Signature)).toBe('签名')
    expect(placementLabel(FieldPlacementMode.Anchor)).toBe('锚文本')
    expect(templateSourceLabel(TemplateSource.Uploaded)).toBe('上传文档')

    useAdminAppStore().setLocale('en')
    expect(envelopeStatusLabel(EnvelopeStatus.InProgress)).toBe('In progress')
  })

  it('falls back to a readable name for a member the dictionary does not know yet', () => {
    expect(envelopeStatusLabel('AwaitingCountersign' as EnvelopeStatus)).toBe('Awaiting Countersign')
    expect(envelopeStatusLabel(null)).toBe('')
  })
})
