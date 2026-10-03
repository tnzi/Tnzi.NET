// @vitest-environment node
/**
 * Lock: the sign-in route renders why the previous session ended.
 *
 * `@tnzi/core` leaves `auth.sessionEndReason` = `'security'` after the backend
 * revoked a session for a security reason (refresh-token replay, session
 * binding mismatch), and keeps it alive across the unauthorized signal for
 * exactly one reader: the login page. Until 2026-09-12 nothing read it - the
 * user landed on an empty identifier field with no idea their credentials were
 * in use elsewhere.
 *
 * Most SFCs have no mount coverage in this package (only `TAuthPage.mount.test.ts` mounts one), so
 * the contract is checked on the source: `TAuthRoute` must forward the
 * runtime's reason and `TAuthPage` must render it with a status role.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));

function source(relative: string): string {
  return readFileSync(resolve(here, relative), 'utf8')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:'"`])\/\/[^\n]*/g, '$1');
}

const route = source('../../src/auth/TAuthRoute.vue');
const page = source('../../src/auth/TAuthPage.vue');

describe('TAuthRoute forwards the session-end reason', () => {
  it('reads it off the runtime auth manager', () => {
    expect(route).toMatch(/runtime\.auth\.sessionEndReason/);
  });

  it('passes it to TAuthPage', () => {
    expect(route).toMatch(/:session-end-reason="/);
  });
});

describe('TAuthPage renders the session-end reason', () => {
  it('declares the prop', () => {
    expect(page).toMatch(/sessionEndReason\?:\s*SessionEndReason\s*\|\s*null/);
  });

  it('renders it as a status notice, with the security copy and a translate key', () => {
    expect(page).toMatch(/data-test="t-auth-session-notice"/);
    expect(page).toMatch(/role="status"[^>]*>\s*\{\{\s*sessionNotice/);
    expect(page).toMatch(/auth\.notice\.sessionEndedForSecurity/);
    expect(page).toMatch(/ended for security reasons/i);
    expect(page).toMatch(/auth\.notice\.sessionExpired/);
  });

  it('renders the IP allow-list refusal with its own translate key', () => {
    expect(page).toMatch(/case 'ipNotAllowed'/);
    expect(page).toMatch(/auth\.notice\.sessionEndedIpNotAllowed/);
    expect(page).toMatch(/allowed network/i);
  });

  it('marks the security case visually, not just textually', () => {
    expect(page).toMatch(/t-auth__session-notice--warning/);
  });
});
