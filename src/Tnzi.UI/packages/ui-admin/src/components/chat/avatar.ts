/**
 * Chat avatar helpers.
 *
 * The deterministic colour + initial logic now lives in `@tnzi/ui`
 * (`avatarColor` / `avatarInitial`) so every package shares one palette via the
 * `TAvatar` primitive. They are re-exported here to keep existing chat imports
 * stable; only the storage-specific preview-URL builder is chat-local.
 */
export { avatarColor, avatarInitial } from '@tnzi/ui'

/** Build the preview URL for a stored file (avatar, image or file message). */
export function resolveChatAvatarUrl(fileId?: string | null): string | null {
  return fileId ? `/api/files/${fileId}/preview` : null
}
