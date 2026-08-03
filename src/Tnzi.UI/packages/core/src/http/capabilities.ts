/**
 * Cross-version capability negotiation (client side).
 *
 * The framework ships as NuGet packages and `@tnzi/*` ships as npm packages, so the two sides
 * upgrade independently. A capability is a named, versioned protocol feature that **both ends
 * must agree on before it is used**.
 *
 * Two directions, deliberately asymmetric:
 * - What *this client* understands rides along on every request in the
 *   {@link CAPABILITY_HEADER} header, because the server has to know it per request.
 * - What *the server* understands is fetched once from `GET /capabilities`.
 *
 * Note that this is not the same as defensive parsing. Tolerating an unknown payload keeps an old
 * client from crashing; negotiation is what lets a new behaviour be switched on safely.
 * Most changes need neither: a new response field is simply ignored by clients that predate it.
 */

/** Request header through which a client declares the capabilities it understands. */
export const CAPABILITY_HEADER = 'X-Tnzi-Capabilities';

/**
 * Capability names must be lowercase kebab-case ending in a version suffix, e.g.
 * `chat-draft-restore-v1`. Matches the server-side rule so a name is either valid on both sides
 * or on neither.
 */
const CAPABILITY_NAME_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*-v[1-9][0-9]*$/;

/** Whether the given string is a well-formed capability name. */
export function isValidCapabilityName(name: string): boolean {
  return CAPABILITY_NAME_PATTERN.test(name);
}

/**
 * The capabilities this client understands.
 *
 * Empty by default: the framework declares nothing until a real cross-version capability exists.
 * Applications add theirs with {@link declareClientCapability}.
 */
const declared = new Set<string>();

/**
 * Declare a capability this client understands.
 *
 * Throws on a malformed name rather than silently registering something no server can ever match,
 * since at runtime a typo is indistinguishable from "the server does not support it yet".
 */
export function declareClientCapability(name: string): void {
  if (!isValidCapabilityName(name)) {
    throw new Error(
      `'${name}' is not a valid capability name. Names must be lowercase kebab-case with a ` +
        `version suffix, e.g. 'chat-draft-restore-v1'.`
    );
  }
  declared.add(name);
}

/** Capability names this client declared, sorted. */
export function getClientCapabilities(): string[] {
  return [...declared].sort();
}

/**
 * Header value for the current declaration, or `undefined` when nothing is declared.
 *
 * Returning undefined rather than an empty string keeps an empty header off every request in the
 * (currently universal) case where no capability has been declared.
 */
export function buildCapabilityHeaderValue(): string | undefined {
  return declared.size === 0 ? undefined : getClientCapabilities().join(',');
}

/**
 * Reset the declaration. Intended for tests - production code declares once at startup.
 */
export function resetClientCapabilities(): void {
  declared.clear();
}

/**
 * Whether the server declared the given capability.
 *
 * @param serverCapabilities the list returned by `GET /capabilities`
 *
 * A capability is only safe to use when **both** sides have it, so callers must check this against
 * their own declaration too - the server advertising something says nothing about whether this
 * build knows how to speak it.
 */
export function serverSupports(serverCapabilities: readonly string[], name: string): boolean {
  return serverCapabilities.includes(name);
}

/** Serialization shape of `GET /capabilities`. */
export interface ServerCapabilitiesDto {
  capabilities: string[];
}

/** Minimal client surface needed to read the server capability list. */
export interface CapabilityHttpClient {
  get<T>(url: string): Promise<{ succeeded: boolean; data?: T | null }>;
}

/**
 * Read the server's capability list.
 *
 * Anonymous endpoint: a client needs to know the protocol surface before it can pick a login flow,
 * so gating it behind auth would create a chicken-and-egg problem for exactly the changes
 * negotiation exists to support.
 *
 * A failed or unreachable call yields an empty list - unknown must degrade to "assume nothing new",
 * never to "assume everything works".
 */
export async function fetchServerCapabilities(client: CapabilityHttpClient): Promise<string[]> {
  try {
    const result = await client.get<ServerCapabilitiesDto>('/capabilities');
    return result.succeeded ? (result.data?.capabilities ?? []) : [];
  } catch {
    return [];
  }
}
