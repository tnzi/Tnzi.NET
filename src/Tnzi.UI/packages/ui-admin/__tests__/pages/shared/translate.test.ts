import { describe, it, expect } from 'vitest'
import { maybeTranslateKey } from '../../../src/pages/_shared/translate'

describe('maybeTranslateKey', () => {
  const t = (k: string) => (k === 'admin.crud.list' ? 'List' : `T:${k}`)

  it('translates a dotted key-shaped string', () => {
    expect(maybeTranslateKey(t, 'admin.crud.list', 'fallback')).toBe('List')
  })
  it('returns a human literal (with spaces/caps) verbatim', () => {
    expect(maybeTranslateKey(t, 'User Management', 'fallback')).toBe('User Management')
  })
  it('uses the fallback when value is empty', () => {
    expect(maybeTranslateKey(t, undefined, 'admin.crud.list')).toBe('List')
  })
  it('passes through when no translate fn given', () => {
    expect(maybeTranslateKey(undefined, 'admin.crud.list', 'fb')).toBe('admin.crud.list')
  })
})
