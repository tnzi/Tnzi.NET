// @vitest-environment happy-dom
/**
 * Drives the REAL `altcha` web component (3.x, pinned in devDependencies) through this
 * package's driver. The stubbed-element tests in captcha-widget.test.ts pin what the driver
 * does; this file pins what the widget does with it, because two rounds of green unit tests
 * shipped a login page that could not verify in a browser:
 *
 *   1. the driver set `challengeurl` (the 1.x / 2.x attribute); 3.x reads `challenge`, so its
 *      config stayed `""` and `fetch("")` fetched the page itself;
 *   2. the challenge endpoint answered the `ApiResult` envelope; the widget validates the body
 *      as an Altcha document (`"challenge" in json`) and reported "Challenge validation failed".
 *
 * What is observable in happy-dom: the widget's resolved configuration (`getConfiguration()`),
 * the URL it fetches, and how far `verify()` gets before the proof-of-work stage (which needs a
 * Web Worker and is not the thing under test). With `debug` on the widget logs through
 * `console.log("ALTCHA", "[name=…]", …)`: `"challenge", parsed` once the body passed validation,
 * `"verification failed", err` when it did not; the assertions read those lines. `verify()` also
 * insists on a secure context, which happy-dom leaves undefined, so the tests declare one.
 */
import { describe, it, expect, vi, beforeAll, afterEach } from 'vitest';
import { mountCaptchaWidget } from '../../src/services/captcha/widget';
import type { CaptchaClientConfigDto } from '../../src/services/captcha/types';

type AltchaElement = HTMLElement & {
  getConfiguration: () => { challenge: string };
  configure: (config: Record<string, unknown>) => void;
  verify: () => Promise<unknown>;
  getState: () => string;
};

const CHALLENGE_URL = '/api/captcha/altcha/challenge?purpose=login';

const config: CaptchaClientConfigDto = {
  enabled: true,
  provider: 'altcha',
  scriptUrl: 'https://cdn.jsdelivr.net/npm/altcha@3/dist/altcha.min.js',
  challengeUrl: 'captcha/altcha/challenge?purpose={purpose}',
};

/** Five minutes out, as the backend's `ExpiresSeconds` default; the widget arms a timer on it. */
const EXPIRES = Math.floor(Date.now() / 1000) + 300;

/** The bare document `GET /captcha/altcha/challenge` answers (field names from AltchaChallengeDto). */
const BARE_DOCUMENT = {
  algorithm: 'SHA-256',
  challenge: '9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08',
  maxnumber: 100000,
  salt: `0123456789abcdef01234567?expires=${EXPIRES}&purpose=login`,
  signature: 'deadbeef',
};

/** The framework's envelope, which the widget must never receive from that URL. */
const ENVELOPE = { code: 200, success: true, succeeded: true, message: 'Success', data: BARE_DOCUMENT };

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json; charset=utf-8' } });
}

/** Everything the widget logged, one string per call (Error objects by their message). */
function logged(log: { mock: { calls: unknown[][] } }): string {
  return log.mock.calls.map((c) => c.map((x) => (x instanceof Error ? x.message : String(x))).join(' ')).join('\n');
}

async function mountReal(): Promise<AltchaElement> {
  const container = document.createElement('div');
  document.body.appendChild(container);
  await mountCaptchaWidget(container, config, { purpose: 'login', challengeUrl: CHALLENGE_URL, onToken: () => undefined });
  const el = container.querySelector('altcha-widget') as AltchaElement;
  // The custom element instantiates its Svelte component a microtask after connection.
  await new Promise((r) => setTimeout(r, 0));
  return el;
}

beforeAll(async () => {
  // Defines <altcha-widget> (the driver then skips the script tag: the element already exists).
  await import('altcha');
  expect(customElements.get('altcha-widget')).toBeDefined();
  // `verify()` refuses outside a secure context; browsers treat localhost / https as one.
  Object.defineProperty(globalThis, 'isSecureContext', { value: true, configurable: true });
});

afterEach(() => {
  vi.restoreAllMocks();
  document.body.replaceChildren();
});

describe('real altcha 3.x widget through the driver', () => {
  it('★ reads the challenge URL from the attribute the driver sets (3.x: `challenge`, not `challengeurl`)', async () => {
    const el = await mountReal();
    expect(el.getAttribute('challenge')).toBe(CHALLENGE_URL);
    expect(el.getConfiguration().challenge).toBe(CHALLENGE_URL);
  });

  it('★ fetches that URL and accepts the bare Altcha document the endpoint answers', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(BARE_DOCUMENT));
    const log = vi.spyOn(console, 'log').mockImplementation(() => undefined);
    const el = await mountReal();
    el.configure({ debug: true });

    await el.verify();

    expect(fetchSpy).toHaveBeenCalled();
    expect(String(fetchSpy.mock.calls[0]![0])).toBe(CHALLENGE_URL);
    // The widget parsed the body as a v1 challenge: the document's `challenge` became the key
    // prefix and the `?expires=` in the salt became the expiry.
    const parsed = log.mock.calls.find((c) => c[2] === 'challenge')?.[3] as
      | { parameters?: { keyPrefix?: string; expiresAt?: number } }
      | undefined;
    expect(parsed?.parameters?.keyPrefix).toBe(BARE_DOCUMENT.challenge);
    expect(parsed?.parameters?.expiresAt).toBe(EXPIRES);
    // Whatever stops it afterwards (no Web Worker here) is not a challenge-shape failure.
    expect(logged(log)).not.toMatch(/Challenge validation failed|invalid content-type/);
  });

  it('★ rejects the ApiResult envelope from that URL (the shape the endpoint must not answer)', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(ENVELOPE));
    const log = vi.spyOn(console, 'log').mockImplementation(() => undefined);
    const el = await mountReal();
    el.configure({ debug: true });

    await el.verify();

    expect(logged(log)).toMatch(/verification failed[^\n]*Challenge validation failed/);
    expect(el.getState()).toBe('error');
  });
});
