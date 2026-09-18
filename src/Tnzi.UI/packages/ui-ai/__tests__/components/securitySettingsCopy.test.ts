// @vitest-environment node
/**
 * Lock: the "sign out" button in `TSecuritySettings` promises exactly what the
 * call underneath it does.
 *
 * `revokeAllSessions()` (no argument) has meant "every OTHER device, this tab
 * stays signed in" since the backend started excluding the caller's session on
 * 2026-08-31 (`?includeCurrent=true` opts the current one in). The hook's
 * interface doc was updated in that commit; the button was not - it kept
 * saying "Sign out everywhere, including this tab?" while the tab quietly
 * survived, and the list under it re-rendered the surviving session as proof.
 * On a shared machine that is a false security promise.
 *
 * SFCs have no unit coverage in this package (no Vue plugin in vitest), so the
 * contract is checked on the source: whichever wording the button uses, it
 * must agree with the argument it passes.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const file = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../src/components/settings/TSecuritySettings.vue',
);
const source = readFileSync(file, 'utf8')
  .replace(/<!--[\s\S]*?-->/g, '')
  .replace(/\/\*[\s\S]*?\*\//g, '')
  .replace(/(^|[^:'"`])\/\/[^\n]*/g, '$1');

describe('TSecuritySettings sign-out copy', () => {
  const callsWithoutCurrent = /revokeAllSessions\(\s*\)/.test(source);
  const callsWithCurrent = /revokeAllSessions\(\s*true\s*\)/.test(source);
  const promisesThisTab = /including this (tab|one|device|session)|everywhere/i.test(source);
  const promisesOtherDevices = /other devices?/i.test(source);

  it('has the button to check', () => {
    expect(callsWithoutCurrent || callsWithCurrent).toBe(true);
  });

  it('does not promise to end this tab when the call keeps it', () => {
    if (callsWithoutCurrent && !callsWithCurrent) {
      expect(promisesThisTab, 'label says "everywhere / including this tab" but the call excludes the current session').toBe(false);
      expect(promisesOtherDevices).toBe(true);
    }
  });

  it('does not promise to keep this tab when the call ends it', () => {
    if (callsWithCurrent && !callsWithoutCurrent) {
      expect(promisesOtherDevices, 'label says "other devices" but the call ends the current session too').toBe(false);
    }
  });
});
