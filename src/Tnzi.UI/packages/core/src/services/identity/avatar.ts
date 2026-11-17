/**
 * The picture to show for a user profile.
 *
 * A profile carries two avatar fields and they mean different things:
 * `avatarId` is a file the user uploaded (Tnzi.Storage), `avatar` /
 * `avatarUrl` is an external link (usually imported from an OAuth provider).
 * An uploaded photo is the user's own choice, so it wins over the imported
 * link. Showing only `avatar` - which is what the auth state did before this
 * helper - meant an uploaded photo never appeared anywhere outside the page
 * that uploaded it.
 *
 * The uploaded file is readable anonymously: the backend publishes it the
 * moment its id is written into `AvatarId` (`[FileField(Public = true)]`), so a
 * plain `<img src>` without an Authorization header works.
 */
import type { HttpClient } from '../../http';

export interface AvatarSource {
  avatar?: string | null;
  avatarUrl?: string | null;
  avatarId?: string | null;
}

/** Upload limit the backend applies by default (`Identity:Avatar:MaxBytes`). */
export const DEFAULT_AVATAR_MAX_BYTES = 10 * 1024 * 1024;

/** MIME types the backend accepts as an avatar (checked there by file signature). */
export const AVATAR_ACCEPTED_TYPES: readonly string[] = ['image/png', 'image/jpeg', 'image/webp'];

/**
 * The URL to render for this profile's avatar, or null when it has none
 * (render the initials fallback then). Goes through `resolveUrl`, so a
 * deployment mounted under a path prefix still gets a working link.
 */
export function profileAvatarUrl(
  client: Pick<HttpClient, 'resolveUrl'> | null | undefined,
  source: AvatarSource | null | undefined,
): string | null {
  if (!source) return null;
  const id = source.avatarId?.trim();
  if (id && client) return client.resolveUrl(`/files/${encodeURIComponent(id)}/download`);
  const external = (source.avatar ?? source.avatarUrl ?? '').trim();
  return external || null;
}
