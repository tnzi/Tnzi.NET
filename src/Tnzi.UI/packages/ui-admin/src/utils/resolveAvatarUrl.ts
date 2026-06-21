/**
 * `resolveAvatarUrl` — turn a user-like profile object into a displayable
 * avatar `<img>` source, or `null` when the user has no avatar.
 *
 * Two facts about the backend shape this helper hides from callers:
 *
 *  1. **Naming drift across DTOs.** `UserDto` (list/profile endpoint) exposes
 *     the external avatar link as `avatar`, while `UserDetailDto` /
 *     `UpdateUserDto` call the same concept `avatarUrl`. Rather than force
 *     every call site to know which DTO it holds, we read **both** and prefer
 *     `avatarUrl`. (We deliberately do NOT touch the DTO contracts here —
 *     renaming them would ripple into the music/webshop consumers.)
 *  2. **Local file uploads.** When the avatar was uploaded through the storage
 *     module there is no external URL — only an `avatarId` pointing at a file
 *     record. `GET /files/{id}/preview` is `[AllowAnonymous]`, so the file id
 *     resolves to a plain image URL via `storageApi.getPreviewUrl(id)` with no
 *     token/cookie/extra request needed.
 *
 * Resolution order: external link (`avatarUrl` → `avatar`) → preview URL from
 * `avatarId` → `null`.
 */

/** The subset of a storage API this helper depends on. */
export interface AvatarStorageApi {
  /** Synchronously build the inline preview URL for a stored file id. */
  getPreviewUrl: (id: string) => string
}

/**
 * A user-like object carrying any subset of the avatar fields. Kept loose on
 * purpose so it accepts `UserDto`, `UserDetailDto`, `UserProfile`, the admin
 * store's `AdminUserInfo`, etc. without coupling to one concrete DTO.
 */
export interface AvatarBearer {
  /** `UserDetailDto` / `UpdateUserDto` naming for the external link. */
  avatarUrl?: string | null
  /** `UserDto` / `UserProfile` / store naming for the external link. */
  avatar?: string | null
  /** Local storage file id (preferred when no external link exists). */
  avatarId?: string | null
}

/** True for a non-empty, non-whitespace string. */
function hasText(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

export function resolveAvatarUrl(
  profile: AvatarBearer | null | undefined,
  storageApi: AvatarStorageApi,
): string | null {
  if (!profile) return null

  // 1. External link wins — covers both DTO naming conventions.
  if (hasText(profile.avatarUrl)) return profile.avatarUrl
  if (hasText(profile.avatar)) return profile.avatar

  // 2. Local upload — turn the file id into an anonymous preview URL.
  if (hasText(profile.avatarId)) return storageApi.getPreviewUrl(profile.avatarId)

  // 3. No avatar.
  return null
}
