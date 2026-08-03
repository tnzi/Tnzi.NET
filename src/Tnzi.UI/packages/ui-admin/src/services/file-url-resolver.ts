/**
 * One shared file-URL resolver per HTTP client.
 *
 * `createStorageBridge()` returns a fresh bridge on every call (by design), so
 * a resolver owned by the bridge would throw its token cache away on every
 * page mount and re-mint the same tokens over and over. The cache is what makes
 * signed URLs cheap, so it lives here, keyed by the client the tokens belong to.
 *
 * Keyed by client - not global - because a token is minted against a session:
 * two clients are two identities, and they must not share tokens.
 */
import { createFileUrlResolver, type FileUrlResolver } from '@tnzi/core/services/storage'

type HttpClient = Parameters<typeof createFileUrlResolver>[0]

const resolvers = new WeakMap<object, FileUrlResolver>()
// A parallel list so `resetAllFileUrlResolvers` can reach every cache on
// logout, where the caller does not necessarily hold the client. One entry per
// client (an app has one), so the strong reference costs nothing.
const created = new Set<FileUrlResolver>()

/** The shared resolver for this client, created on first use. */
export function getFileUrlResolver(client: HttpClient): FileUrlResolver {
  const key = client as unknown as object
  let resolver = resolvers.get(key)
  if (!resolver) {
    resolver = createFileUrlResolver(client)
    resolvers.set(key, resolver)
    created.add(resolver)
  }
  return resolver
}

/**
 * Drop cached tokens for this client. Call on logout / user switch: tokens
 * outlive the request that minted them, and carrying them across identities
 * would let the next user render the previous one's files until they expire.
 */
export function resetFileUrlResolver(client: HttpClient): void {
  resolvers.get(client as unknown as object)?.clear()
}

/** Same, for every client. Used by the framework's logout / session-expiry paths. */
export function resetAllFileUrlResolvers(): void {
  created.forEach((resolver) => resolver.clear())
}
