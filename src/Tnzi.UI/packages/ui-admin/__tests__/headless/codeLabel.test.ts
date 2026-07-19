import { describe, it, expect } from 'vitest'
import { isCodeRedundant } from '../../src/headless/codeLabel'

describe('isCodeRedundant', () => {
  it('is true when name and code collapse to the same token', () => {
    expect(isCodeRedundant('SuperAdmin', 'SUPERADMIN')).toBe(true)
    expect(isCodeRedundant('Super Admin', 'SUPERADMIN')).toBe(true)
    expect(isCodeRedundant('Identity', 'identity')).toBe(true)
    expect(isCodeRedundant('blog', 'blog')).toBe(true)
    // Separators (spaces vs dots) are ignored — a code that is just the spaced
    // name with punctuation still collapses to the same token.
    expect(isCodeRedundant('Finance Account', 'finance.account')).toBe(true)
  })

  it('is false when the code still carries information', () => {
    // Plural label vs singular prefix — the code namespace is distinct.
    expect(isCodeRedundant('Users', 'user')).toBe(false)
    expect(isCodeRedundant('View Users', 'user')).toBe(false)
    // A localized name has no ascii token to match against, so the English
    // code stays visible.
    expect(isCodeRedundant('身份管理', 'identity')).toBe(false)
    expect(isCodeRedundant('Access Logs', 'system.accessLog')).toBe(false)
  })

  it('is false when either side is missing', () => {
    expect(isCodeRedundant(undefined, 'x')).toBe(false)
    expect(isCodeRedundant('x', null)).toBe(false)
    expect(isCodeRedundant('', '')).toBe(false)
  })
})
