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
   *
   * Capped at half of each token's remaining lifetime, so a deployment whose
   * token TTL is shorter than the margin still caches for a while instead of
   * treating every token as stale the moment it arrives.
   */
  refreshMarginMs?: number;
  /**
   * `invalidate()` keeps a token minted less than this many milliseconds ago.
   * Default 10s. It is what stops an `<img @error>` that re-signs from looping
   * on a file that fails for another reason (no thumbnail, unsupported type):
   * the second error finds a fresh token, the URL stays the same, and the
   * browser does not try again.
   */
  minReissueIntervalMs?: number;
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
   * The moment (epoch ms) the earliest cached token among these ids stops
   * being served from the cache, so the next resolve mints a new one; `null`
   * when none of them holds a usable token. A long-lived view schedules its
   * re-resolve for this moment: the URLs it already handed out stop loading
   * once their token expires.
   */
  refreshAt(fileIds: readonly string[]): number | null;
  /**
   * Drop the cached tokens of these ids so the next resolve mints new ones.
   * For a URL the server refused even though the cache still considered its
   * token valid (the client clock running behind the server's). Tokens minted
   * within `minReissueIntervalMs` are kept.
   */
  invalidate(fileIds: readonly string[]): void;
  /**
   * Drop every cached token. Call on logout / user switch: tokens are minted
   * against a session, and keeping them across identities would let the next
   * user render the previous one's files until they expire.
   *
   * Also covers what is still under way: a batch that answers after this call
   * caches nothing, and every resolve still waiting settles as `null`.
   */
  clear(): void;
}

interface CachedToken {
  token: string;
  /** When the cache stops serving it: expiry minus the refresh margin. */
  staleAtMs: number;
  mintedAtMs: number;
}

interface PendingEntry {
  resolve: (token: string | null) => void;
}

const DEFAULT_REFRESH_MARGIN_MS = 60_000;
const DEFAULT_MAX_BATCH_SIZE = 100;
const DEFAULT_MIN_REISSUE_INTERVAL_MS = 10_000;

export function createFileUrlResolver(
  client: HttpClient,
  options: FileUrlResolverOptions = {},
): FileUrlResolver {
  const api = useStorageApi(client);
  const refreshMarginMs = options.refreshMarginMs ?? DEFAULT_REFRESH_MARGIN_MS;
  const maxBatchSize = options.maxBatchSize ?? DEFAULT_MAX_BATCH_SIZE;
  const minReissueIntervalMs = options.minReissueIntervalMs ?? DEFAULT_MIN_REISSUE_INTERVAL_MS;

  const cache = new Map<string, CachedToken>();
  /** Ids waiting for the next batch. */
  const pending = new Map<string, PendingEntry[]>();
  /**
   * Ids a batch has asked for and not heard back on. A resolve for one of them
   * waits on that batch: putting it in `pending` again made the next batch
   * repeat every id still in flight.
   */
  const inFlight = new Map<string, PendingEntry[]>();
  /**
   * Bumped by `clear()`. A batch that started before it answers for the
   * previous session: its tokens are not cached and its waiters get null.
   */
  let generation = 0;
  let flushTimer: ReturnType<typeof setTimeout> | null = null;

  function urlFor(fileId: string, kind: FileUrlKind): string {
    // One literal path per kind so the frontend/backend contract check can see each endpoint.
    switch (kind) {
      case 'preview':
        return client.resolveUrl(`/files/${fileId}/preview`);
      case 'thumbnail':
        return client.resolveUrl(`/files/${fileId}/thumbnail`);
      default:
        return client.resolveUrl(`/files/${fileId}/download`);
    }
  }

  function cachedToken(fileId: string): string | null {
    const hit = cache.get(fileId);
    if (!hit) return null;
    if (hit.staleAtMs <= Date.now()) {
      cache.delete(fileId);
      return null;
    }
    return hit.token;
  }

  function store(fileId: string, token: string, expiresAt: string): void {
    const now = Date.now();
    const parsed = Date.parse(expiresAt);
    // An unparseable expiry would otherwise be cached as NaN and treated as
    // "expired" forever, turning every render into a fresh request.
    const expiresAtMs = Number.isNaN(parsed) ? now + DEFAULT_REFRESH_MARGIN_MS * 2 : parsed;
    const remainingMs = Math.max(0, expiresAtMs - now);
    cache.set(fileId, {
      token,
      staleAtMs: expiresAtMs - Math.min(refreshMarginMs, remainingMs / 2),
      mintedAtMs: now,
    });
  }

  /** Resolve every waiter in the list once; a list may be settled early by `clear()`. */
  function settle(waiters: PendingEntry[], token: string | null): void {
    for (const waiter of waiters.splice(0)) waiter.resolve(token);
  }

  async function flush(): Promise<void> {
    flushTimer = null;
    const ids = [...pending.keys()].slice(0, maxBatchSize);
    if (ids.length === 0) return;

    // This batch owns its ids' waiters from here on.
    const batch = new Map<string, PendingEntry[]>();
    for (const id of ids) {
      const waiters = pending.get(id)!;
      pending.delete(id);
      batch.set(id, waiters);
      inFlight.set(id, waiters);
    }
    const batchGeneration = generation;

    const tokens = new Map<string, string>();
    try {
      const result = await api.getAccessTokens(ids);
      // Minted for the session that was signed in when the batch went out.
      // After a clear() they belong to nobody here.
      if (batchGeneration === generation) {
        for (const item of isSuccess(result) ? result.data : []) {
          store(item.fileId, item.token, item.expiresAt);
          tokens.set(item.fileId, item.token);
        }
      }
    } catch {
      // A failed mint is not an exception for the caller: a broken thumbnail
      // must not take down the list that contains it.
    }

    // Anything the server omitted (not readable / does not exist) settles as
    // null so its waiters are never left hanging.
    for (const [id, waiters] of batch) {
      if (inFlight.get(id) === waiters) inFlight.delete(id);
      settle(waiters, tokens.get(id) ?? null);
    }

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
      const requested = inFlight.get(fileId);
      if (requested) {
        requested.push({ resolve });
        return;
      }
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

    refreshAt(fileIds) {
      const now = Date.now();
      let earliest: number | null = null;
      for (const id of fileIds) {
        const hit = cache.get(id);
        // A token already past its stale point is not "due later": the next
        // resolve mints anyway, and reporting a moment in the past would make
        // a caller's timer fire again at once.
        if (!hit || hit.staleAtMs <= now) continue;
        if (earliest === null || hit.staleAtMs < earliest) earliest = hit.staleAtMs;
      }
      return earliest;
    },

    invalidate(fileIds) {
      const now = Date.now();
      for (const id of fileIds) {
        const hit = cache.get(id);
        if (hit && now - hit.mintedAtMs >= minReissueIntervalMs) cache.delete(id);
      }
    },

    clear() {
      // Everything asked for so far was asked as the previous identity: a batch
      // still out must not write its tokens back afterwards, and the views that
      // were waiting on it are being torn down with that session.
      generation += 1;
      cache.clear();
      if (flushTimer !== null) {
        clearTimeout(flushTimer);
        flushTimer = null;
      }
      const dropped = [...pending.values(), ...inFlight.values()];
      pending.clear();
      inFlight.clear();
      for (const waiters of dropped) settle(waiters, null);
    },
  };
}
