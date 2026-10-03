// @vitest-environment node
/**
 * Lock: the Active sessions list in `TSecuritySettings` cannot sign the user
 * out of the page they are on with one click, and "Sign out other devices"
 * is disabled when there is nothing else to sign out.
 *
 * The backend's session DTO has no "this is you" marker; the hook derives it
 * from the access token's `session_id` claim (`isCurrentSession` /
 * `otherSessions`, tested in `useAccountSettings.test.ts`). This file checks
 * that the markup actually uses them:
 *
 *   - the current row is labelled and carries no Revoke button;
 *   - every other row's Revoke is a two-step confirmation (the marker can be
 *     unknown on a deployment without session-bound tokens, and then the row
 *     might still be this device);
 *   - the bulk button's disabled state reads `otherSessions`, not `sessions`
 *     (the list always contains the caller's own session while signed in, so
 *     `sessions.length === 0` was dead: the button stayed live with nobody
 *     else to sign out and clicking it did nothing visible).
 *
 * SFCs have no mount coverage in this package, so the contract is checked on
 * the source.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const file = resolve(dirname(fileURLToPath(import.meta.url)), '../../src/components/settings/TSecuritySettings.vue');
const source = readFileSync(file, 'utf8')
  .replace(/<!--[\s\S]*?-->/g, '')
  .replace(/\/\*[\s\S]*?\*\//g, '')
  .replace(/(^|[^:'"`])\/\/[^\n]*/g, '$1');

const template = source.slice(source.indexOf('<template>'), source.lastIndexOf('</template>'));

describe('TSecuritySettings - active sessions safeguards', () => {
  it('labels the current session row', () => {
    expect(template).toMatch(/controller\.isCurrentSession\(session\)/);
    expect(template).toMatch(/\{\{ t\.securitySettings\.thisDevice \}\}/);
  });

  it('renders no Revoke button on the current session row', () => {
    // The per-row Revoke must be gated on NOT being the current session.
    expect(template).toMatch(/v-if="!controller\.isCurrentSession\(session\)"[\s\S]{0,400}@click="onRevokeSession\(session\.id\)"/);
  });

  it('per-row Revoke is a two-step confirmation, never a direct call', () => {
    expect(template).not.toMatch(/@click="controller\.revokeSession\(/);
    expect(source).toMatch(/const confirmingSessionId = ref<string \| null>\(null\)/);
    expect(source).toMatch(/async function onRevokeSession\(sessionId: string\)/);
  });

  it('disables "Sign out other devices" off otherSessions, not sessions', () => {
    expect(template).toMatch(/:disabled="controller\.otherSessions\.value\.length === 0"/);
    expect(template).not.toMatch(/:disabled="controller\.sessions\.value\.length === 0"/);
  });
});
