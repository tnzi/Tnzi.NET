/**
 * File URL resolution for PRIVATE files.
 *
 * The problem this solves: `<img>`, `<a download>`, `<video>` and `<iframe>`
 * issue their own requests and cannot carry an Authorization header. The
 * framework authenticates with bearer tokens only, so a plain
 * `/api/files/{id}/preview` is an anonymous request - which means a private
 * file is unrenderable, even for the person who uploaded it.
 *
 * So: exchange the session for a short-lived per-file token first
 * (`GET /files/{id}/access-token`), then append it as `?sig=`.
 *
 * Two things make that practical rather than a request storm:
 *  - **Batching.** A message list mounts N bubbles in one tick; every `resolve`
 *    in that tick is coalesced into a single `POST /files/access-tokens`.
 *  - **Caching.** A token is reused until shortly before it expires.
 *
 * Public files (avatars, site assets) need none of this - use `plain()`.
 */

import type { HttpClient } from '../../http/http';
import { isSuccess } from '../../http/response';
import { useStorageApi } from './api';

/** Which read endpoint the URL should point at. */
export type FileUrlKind = 'preview' | 'download' | 'thumbnail';

/** Query parameter the backend reads the token from (`IFileUrlSigner.QueryParameterName`). */
export const FILE_SIGNATURE_PARAM = 'sig';

export interface FileUrlResolverOptions {
  /**
   * Refresh a cached token once it is within this many milliseconds of
   * expiring. Default 60s: long enough that a page which starts loading now
   * will not have its images 404 half way through.
   */
  refreshMarginMs?: number;
  /**
   * Maximum ids per batch request. Default 100 - large enough that a normal
   * page is one round trip, small enough to keep the URL/body sane.
   */
  maxBatchSize?: number;
}

export interface FileUrlResolver {
  /**
   * Signed URL for one file, or `null` when the caller may not read it (the
   * backend omits unreadable ids rather than erroring, so this is the normal
   * "not allowed / gone" answer, not an exception).
   */
  resolve(fileId: string, kind?: FileUrlKind): Promise<string | null>;
  /** Signed URLs for many files. Unreadable ids are absent from the map. */
  resolveMany(fileIds: string[], kind?: FileUrlKind): Promise<Map<string, string>>;
  /**
   * Unsigned URL. Correct for files known to be public (`FileRecordDto.isPublic`);
   * for anything else the browser will get a 404.
   */
  plain(fileId: string, kind?: FileUrlKind): string;
  /**
   * Drop every cached token. Call on logout / user switch: tokens are minted
   * against a session, and keeping them across identities would let the next
   * user render the previous one's files until they expire.
   */
  clear(): void;
}

interface CachedToken {
  token: string;
  expiresAtMs: number;
}

interface PendingEntry {
  resolve: (token: string | null) => void;
}

const DEFAULT_REFRESH_MARGIN_MS = 60_000;
const DEFAULT_MAX_BATCH_SIZE = 100;

export function createFileUrlResolver(
  client: HttpClient,
  options: FileUrlResolverOptions = {},
): FileUrlResolver {
  const api = useStorageApi(client);
  const refreshMarginMs = options.refreshMarginMs ?? DEFAULT_REFRESH_MARGIN_MS;
  const maxBatchSize = options.maxBatchSize ?? DEFAULT_MAX_BATCH_SIZE;

  const cache = new Map<string, CachedToken>();
  const pending = new Map<string, PendingEntry[]>();
  let flushTimer: ReturnType<typeof setTimeout> | null = null;

  function urlFor(fileId: string, kind: FileUrlKind): string {
    const suffix = kind === 'preview' ? 'preview' : kind === 'thumbnail' ? 'thumbnail' : 'download';
    return client.resolveUrl(`/files/${fileId}/${suffix}`);
  }

  function cachedToken(fileId: string): string | null {
    const hit = cache.get(fileId);
    if (!hit) return null;
    if (hit.expiresAtMs - refreshMarginMs <= Date.now()) {
      cache.delete(fileId);
      return null;
    }
    return hit.token;
  }

  function store(fileId: string, token: string, expiresAt: string): void {
    const expiresAtMs = Date.parse(expiresAt);
    // An unparseable expiry would otherwise be cached as NaN and treated as
    // "expired" forever, turning every render into a fresh request.
    cache.set(fileId, {
      token,
      expiresAtMs: Number.isNaN(expiresAtMs) ? Date.now() + DEFAULT_REFRESH_MARGIN_MS * 2 : expiresAtMs,
    });
  }

  function settle(fileId: string, token: string | null): void {
    const waiters = pending.get(fileId);
    pending.delete(fileId);
    waiters?.forEach((w) => w.resolve(token));
  }

  async function flush(): Promise<void> {
    flushTimer = null;
    const ids = [...pending.keys()].slice(0, maxBatchSize);
    if (ids.length === 0) return;

    try {
      const result = await api.getAccessTokens(ids);
      const minted = isSuccess(result) ? result.data : [];
      for (const item of minted) {
        store(item.fileId, item.token, item.expiresAt);
        settle(item.fileId, item.token);
      }
    } catch {
      // A failed mint is not an exception for the caller: a broken thumbnail
      // must not take down the list that contains it.
    }

    // Anything the server omitted (not readable / does not exist) settles as
    // null so its waiters are never left hanging.
    for (const id of ids) settle(id, null);

    // More ids arrived while this batch was in flight, or the batch was capped.
    if (pending.size > 0) schedule();
  }

  function schedule(): void {
    if (flushTimer !== null) return;
    // A 0ms timer (not a microtask) so an entire mount pass lands in one batch.
    flushTimer = setTimeout(() => void flush(), 0);
  }

  function tokenFor(fileId: string): Promise<string | null> {
    const hit = cachedToken(fileId);
    if (hit) return Promise.resolve(hit);

    return new Promise<string | null>((resolve) => {
      const waiters = pending.get(fileId);
      if (waiters) {
        waiters.push({ resolve });
      } else {
        pending.set(fileId, [{ resolve }]);
      }
      schedule();
    });
  }

  function sign(url: string, token: string): string {
    const separator = url.includes('?') ? '&' : '?';
    return `${url}${separator}${FILE_SIGNATURE_PARAM}=${encodeURIComponent(token)}`;
  }

  return {
    plain: (fileId, kind = 'preview') => urlFor(fileId, kind),

    async resolve(fileId, kind = 'preview') {
      if (!fileId) return null;
      const token = await tokenFor(fileId);
      return token ? sign(urlFor(fileId, kind), token) : null;
    },

    async resolveMany(fileIds, kind = 'preview') {
      const unique = [...new Set(fileIds.filter(Boolean))];
      const entries = await Promise.all(
        unique.map(async (id) => [id, await tokenFor(id)] as const),
      );
      const map = new Map<string, string>();
      for (const [id, token] of entries) {
        if (token) map.set(id, sign(urlFor(id, kind), token));
      }
      return map;
    },

    clear() {
      cache.clear();
    },
  };
}
