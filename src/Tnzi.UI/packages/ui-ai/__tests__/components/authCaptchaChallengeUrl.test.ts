// @vitest-environment node
/**
 * Lock: the sign-in route resolves the captcha challenge URL against the API base.
 *
 * Altcha's widget fetches its own challenge. The backend publishes the endpoint as
 * a template relative to the API root (`captcha/altcha/challenge?purpose={purpose}`);
 * a widget left to resolve that against the page asks `/login/captcha/...`, receives
 * the SPA's `index.html`, and locks every user out from the moment the captcha is
 * demanded. `TAuthRoute` must therefore hand `runtime.http.resolveUrl` to `TAuthPage`,
 * and `TAuthPage` must pass it to `useCaptchaWidget` as the `client`.
 *
 * Most SFCs have no mount coverage in this package (only `TAuthPage.mount.test.ts` mounts one), so the
 * contract is checked on the source, as `authSessionEndNotice.test.ts` does.
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

describe('TAuthRoute resolves the captcha challenge URL through the runtime client', () => {
  it('passes runtime.http.resolveUrl to TAuthPage', () => {
    expect(route).toMatch(/:resolve-url="[^"]*runtime\.http\.resolveUrl\(/);
  });
});

describe('TAuthPage hands the resolver to the captcha widget', () => {
  it('declares the prop', () => {
    expect(page).toMatch(/resolveUrl\?:\s*\(url: string\) => string/);
  });

  it('wires it as the useCaptchaWidget client', () => {
    expect(page).toMatch(/useCaptchaWidget\(\{[\s\S]*?client:\s*props\.resolveUrl\s*\?\s*\{\s*resolveUrl:\s*props\.resolveUrl\s*\}/);
  });
});
