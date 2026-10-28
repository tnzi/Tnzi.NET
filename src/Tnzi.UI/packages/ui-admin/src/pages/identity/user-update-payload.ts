import type { UpdateUserDto, UserDto } from '@tnzi/core/services/identity'

/**
 * The fields of `PUT admin/users/{id}` an admin page may change, keyed as the
 * page's form keys them.
 */
export const USER_EDITABLE_KEYS = [
  'email',
  'phoneNumber',
  'firstName',
  'lastName',
  'nickname',
  'gender',
  'birthday',
  'bio',
  'address',
  'website',
  'avatarId',
  'avatarUrl',
] as const

export type UserEditableKey = (typeof USER_EDITABLE_KEYS)[number]

/** Whether a form key is one `PUT admin/users/{id}` accepts from an admin page. */
export function isUserEditableKey(key: string): key is UserEditableKey {
  return (USER_EDITABLE_KEYS as readonly string[]).includes(key)
}

/**
 * Build the body of `PUT admin/users/{id}` from the user as the server last
 * returned it plus the fields the page edited.
 *
 * The profile half of that endpoint (names, nickname, avatar, gender, birthday,
 * bio, address, website) is REPLACED AS A WHOLE whenever the request carries
 * any profile field: a field the request leaves null is cleared. A page that
 * edits only some of those fields, or never loaded them, must therefore carry
 * every other one through unchanged, or one save wipes the person's profile
 * and drops the avatar file reference. Every profile field is always sent:
 *
 * - an edit wins when the page supplied the key (`undefined` = not edited, so
 *   a form that never showed a field cannot clear it);
 * - otherwise the current value is carried through, including the avatar,
 *   which the server returns as `avatar` and accepts as `avatarUrl`.
 *
 * Blank strings become `null` (the server stores "cleared" as null), matching
 * the self-service profile page.
 */
export function buildUserUpdatePayload(
  current: UserDto,
  edits: Partial<Record<UserEditableKey, unknown>>,
): UpdateUserDto {
  const pick = (key: UserEditableKey, fallback: unknown): unknown =>
    edits[key] === undefined ? fallback : edits[key]
  const text = (key: UserEditableKey, fallback: string | null | undefined): string | null => {
    const value = pick(key, fallback)
    return typeof value === 'string' && value.trim() !== '' ? value : null
  }

  return {
    email: text('email', current.email),
    phoneNumber: text('phoneNumber', current.phoneNumber),
    firstName: text('firstName', current.firstName),
    lastName: text('lastName', current.lastName),
    nickname: text('nickname', current.nickname),
    gender: (pick('gender', current.gender) as number | null | undefined) ?? 0,
    birthday: (pick('birthday', current.birthday) as UpdateUserDto['birthday']) ?? null,
    bio: text('bio', current.bio),
    address: text('address', current.address),
    website: text('website', current.website),
    avatarId: text('avatarId', current.avatarId),
    avatarUrl: text('avatarUrl', current.avatar),
  }
}

/** Copy only `keys` out of a form model, so unrelated row data never reaches the payload. */
export function pickEdits(
  model: Record<string, unknown>,
  keys: readonly UserEditableKey[],
): Partial<Record<UserEditableKey, unknown>> {
  const out: Partial<Record<UserEditableKey, unknown>> = {}
  for (const key of keys) {
    if (model[key] !== undefined) out[key] = model[key]
  }
  return out
}
