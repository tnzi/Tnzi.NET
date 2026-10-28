import { describe, it, expect } from 'vitest'
import { SigningFieldType, TemplateSource } from '@tnzi/core/services/signing'
import { isRequiredAtCreate, isSenderField, templateFormSchema } from '../../../src/pages/signing/signing-config'

describe('signing-config', () => {
  it('treats a non-signature field with no recipient role as the sender\'s', () => {
    expect(isSenderField({ type: SigningFieldType.Text, recipientRole: null })).toBe(true)
    expect(isSenderField({ type: SigningFieldType.Date, recipientRole: '   ' })).toBe(true)
    expect(isSenderField({ type: SigningFieldType.Text, recipientRole: 'Client' })).toBe(false)
    // A signature always belongs to a recipient, even when the role is missing.
    expect(isSenderField({ type: SigningFieldType.Signature, recipientRole: null })).toBe(false)
    expect(isSenderField({ type: SigningFieldType.Initials, recipientRole: '' })).toBe(false)
  })

  it('requires a value on the create form only for required fields with no merge binding', () => {
    expect(isRequiredAtCreate({ required: true, binding: null })).toBe(true)
    expect(isRequiredAtCreate({ required: true, binding: 'ClientName' })).toBe(false)
    expect(isRequiredAtCreate({ required: false, binding: null })).toBe(false)
  })

  it('asks for a page count only on composed templates: an uploaded one is read from its PDF', () => {
    const pageCount = templateFormSchema.find((item) => item.key === 'pageCount')
    expect(pageCount?.visible?.({ source: TemplateSource.Composed })).toBe(true)
    expect(pageCount?.visible?.({ source: TemplateSource.Uploaded })).toBe(false)
  })
})
