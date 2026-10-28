import { describe, it, expect } from 'vitest'
import { AccountSystemRole } from '@tnzi/core/services/finance'
import { SYSTEM_ROLE_LABELS, accountFormSchema } from '../../../src/pages/finance/account-config'

/**
 * Every system role the backend resolves must be assignable from the account
 * editor. Payroll posting resolves `WagesPayable`, and a purchase under a
 * non-recoverable tax code resolves `NonRecoverableTaxExpense`; a chart of
 * accounts built by hand could not carry either role while the select did not
 * offer them, and an account seeded with one showed the raw enum name.
 */
describe('account system roles', () => {
  const roles = Object.values(AccountSystemRole)

  it('labels every role', () => {
    for (const role of roles) {
      expect(SYSTEM_ROLE_LABELS[role], role).toBeTruthy()
    }
  })

  it('offers every role in the editor', () => {
    const field = accountFormSchema.find((item) => item.key === 'systemRole')
    const values = (field?.options ?? []).map((option) => option.value)
    expect(values).toEqual(expect.arrayContaining([...roles]))
    expect(values).toContain(AccountSystemRole.WagesPayable)
    expect(values).toContain(AccountSystemRole.NonRecoverableTaxExpense)
  })
})
