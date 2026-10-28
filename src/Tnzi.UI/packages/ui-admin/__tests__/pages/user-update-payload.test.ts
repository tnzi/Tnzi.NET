import { describe, it, expect } from 'vitest'
import type { UserDto } from '@tnzi/core/services/identity'
import {
  buildUserUpdatePayload,
  isUserEditableKey,
  pickEdits,
} from '../../src/pages/identity/user-update-payload'

/**
 * The profile half of `PUT admin/users/{id}` is replaced as a whole once the
 * request carries any profile field. These pin the body the admin pages send:
 * every profile field is present, edits win, everything else is carried
 * through from the user as loaded, including the avatar.
 */
const current: UserDto = {
  id: 'u1',
  userName: 'ivy@corp.example',
  email: 'ivy@corp.example',
  phoneNumber: '+15550000001',
  firstName: 'Ivy',
  lastName: 'Lee',
  nickname: 'ivy',
  avatar: 'https://cdn.example/ivy.png',
  avatarId: 'file-1',
  gender: 2,
  birthday: '1990-05-01T00:00:00Z',
  bio: 'Payroll lead',
  address: '1 Main St',
  website: 'https://ivy.example',
} as UserDto

describe('buildUserUpdatePayload', () => {
  it('carries every profile field through when only a contact field is edited', () => {
    const payload = buildUserUpdatePayload(current, { phoneNumber: '+15550000002' })

    expect(payload).toEqual({
      email: 'ivy@corp.example',
      phoneNumber: '+15550000002',
      firstName: 'Ivy',
      lastName: 'Lee',
      nickname: 'ivy',
      gender: 2,
      birthday: '1990-05-01T00:00:00Z',
      bio: 'Payroll lead',
      address: '1 Main St',
      website: 'https://ivy.example',
      avatarId: 'file-1',
      avatarUrl: 'https://cdn.example/ivy.png',
    })
  })

  it('carries the avatar through from `avatar`, the name the server returns it under', () => {
    const payload = buildUserUpdatePayload(current, { nickname: 'ivy-2', firstName: 'Ivy', bio: '' })

    expect(payload.avatarId).toBe('file-1')
    expect(payload.avatarUrl).toBe('https://cdn.example/ivy.png')
    expect(payload.nickname).toBe('ivy-2')
  })

  it('treats a blank edit as clearing the field, and an absent key as not edited', () => {
    const payload = buildUserUpdatePayload(current, { bio: '', nickname: undefined, website: null })

    expect(payload.bio).toBeNull()
    expect(payload.website).toBeNull()
    expect(payload.nickname).toBe('ivy')
  })

  it('keeps gender 0 (unspecified) rather than sending null', () => {
    const payload = buildUserUpdatePayload({ ...current, gender: undefined } as unknown as UserDto, {})

    expect(payload.gender).toBe(0)
  })
})

describe('pickEdits', () => {
  it('copies only the listed keys and skips undefined values', () => {
    const model = { id: 'u1', roles: ['Admin'], email: 'x@corp.example', nickname: undefined, phoneNumber: '' }

    expect(pickEdits(model, ['email', 'nickname', 'phoneNumber'])).toEqual({ email: 'x@corp.example', phoneNumber: '' })
  })

  it('recognises only fields the endpoint accepts', () => {
    expect(isUserEditableKey('nickname')).toBe(true)
    expect(isUserEditableKey('password')).toBe(false)
    expect(isUserEditableKey('userName')).toBe(false)
  })
})
