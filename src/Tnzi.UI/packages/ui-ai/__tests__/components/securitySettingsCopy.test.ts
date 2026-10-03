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
 * Most SFCs have no unit coverage in this package (only `TAuthPage.mount.test.ts` mounts one), so the
 * contract is checked on the source: whichever wording the button uses, it
 * must agree with the argument it passes. The wording lives in the package
 * catalogue, so the check follows the button's catalogue keys to their English
 * entries (the reference language the translations are made from).
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { en } from '../../src/locales/en';

const file = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../src/components/settings/TSecuritySettings.vue',
);
const source = readFileSync(file, 'utf8')
  .replace(/<!--[\s\S]*?-->/g, '')
  .replace(/\/\*[\s\S]*?\*\//g, '')
  .replace(/(^|[^:'"`])\/\/[^\n]*/g, '$1');

// The bulk button's label: `{{ confirmingAllSessions ? t.securitySettings.A : t.securitySettings.B }}`.
const labelKeys = /\{\{\s*confirmingAllSessions\s*\?\s*t\.securitySettings\.(\w+)\s*:\s*t\.securitySettings\.(\w+)\s*\}\}/.exec(source);
const catalogue = en.securitySettings as Record<string, string>;
const buttonCopy = labelKeys ? `${catalogue[labelKeys[1]] ?? ''}\n${catalogue[labelKeys[2]] ?? ''}` : '';

describe('TSecuritySettings sign-out copy', () => {
  const callsWithoutCurrent = /revokeAllSessions\(\s*\)/.test(source);
  const callsWithCurrent = /revokeAllSessions\(\s*true\s*\)/.test(source);
  const promisesThisTab = /including this (tab|one|device|session)|everywhere/i.test(buttonCopy);
  const promisesOtherDevices = /other devices?/i.test(buttonCopy);

  it('finds the button label in the catalogue', () => {
    expect(labelKeys, 'bulk sign-out label is not read from t.securitySettings').not.toBeNull();
    expect(catalogue[labelKeys![1]]).toBeTruthy();
    expect(catalogue[labelKeys![2]]).toBeTruthy();
  });

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
