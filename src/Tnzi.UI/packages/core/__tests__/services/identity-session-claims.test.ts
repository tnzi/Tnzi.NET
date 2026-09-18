import { describe, it, expect } from 'vitest';
import { readSessionIdClaim } from '../../src/services/identity/session-claims';

// ---------------------------------------------------------------------------
// The session list the backend returns has no "this is you" marker, but the
// access token it issued does: the `session_id` claim it checks on every
// request. Reading it back is how a settings page can label the caller's own
// row and keep a one-click "Revoke" from signing the user out of the page they
// are on.
// ---------------------------------------------------------------------------

function base64url(input: string): string {
  return Buffer.from(input, 'utf8').toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function jwt(payload: Record<string, unknown>): string {
  return `${base64url('{"alg":"HS256","typ":"JWT"}')}.${base64url(JSON.stringify(payload))}.signature`;
}

describe('readSessionIdClaim', () => {
  it('reads the session_id claim off a JWT access token', () => {
    const token = jwt({ sub: 'u1', session_id: '0f8fad5b-d9cb-469f-a165-70867728950e' });
    expect(readSessionIdClaim(token)).toBe('0f8fad5b-d9cb-469f-a165-70867728950e');
  });

  it('survives base64url characters and a payload with multi-byte text', () => {
    // `-` / `_` in the alphabet and a non-ASCII display name in the payload -
    // both broke a naive `atob(JSON)` decode.
    const token = jwt({ name: 'Zoë Ångström 测试', session_id: 'abc-123' });
    expect(readSessionIdClaim(token)).toBe('abc-123');
  });

  it('is null when the token carries no session claim', () => {
    expect(readSessionIdClaim(jwt({ sub: 'u1' }))).toBeNull();
    expect(readSessionIdClaim(jwt({ sub: 'u1', session_id: '' }))).toBeNull();
    expect(readSessionIdClaim(jwt({ sub: 'u1', session_id: 42 }))).toBeNull();
  });

  it('is null for anything that is not a JWT (opaque tokens, garbage, no token)', () => {
    // Fail closed: "unknown" must not be read as "this row is not mine" AND
    // must not be read as "this row is mine" - the caller treats null as
    // "cannot tell" and keeps every safeguard on.
    expect(readSessionIdClaim(null)).toBeNull();
    expect(readSessionIdClaim(undefined)).toBeNull();
    expect(readSessionIdClaim('')).toBeNull();
    expect(readSessionIdClaim('opaque-reference-token')).toBeNull();
    expect(readSessionIdClaim('a.b')).toBeNull();
    expect(readSessionIdClaim('a.!!!not-base64!!!.c')).toBeNull();
    expect(readSessionIdClaim(`h.${base64url('[1,2]')}.s`)).toBeNull();
  });
});
