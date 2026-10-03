import { describe, expect, it } from 'vitest'
import { createUserFormSchema } from '../../src/pages/identity/user-config'

const field = (useEmailAsUserName: boolean, key: string) => {
  const item = createUserFormSchema({ useEmailAsUserName }).find((f) => f.key === key)
  if (!item) throw new Error(`no field ${key}`)
  return item
}

describe('createUserFormSchema', () => {
  it('drops the username and requires the email when the email is the username', () => {
    expect(field(true, 'userName').visible?.({})).toBe(false)
    expect(field(true, 'email').required).toBe(true)
  })

  it('asks for a username on create when usernames are independent', () => {
    expect(field(false, 'userName').visible?.({})).toBe(true)
    expect(field(false, 'userName').visible?.({ id: 'u1' })).toBe(false)
    expect(field(false, 'email').required).toBe(false)
  })
})
