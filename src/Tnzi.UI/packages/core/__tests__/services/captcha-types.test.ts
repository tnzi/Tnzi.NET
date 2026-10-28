import { describe, it, expect, vi, afterEach } from 'vitest';
import {
  composeImageCaptchaToken,
  isAbsoluteUrl,
  isInvisibleCaptchaProvider,
  isScriptCaptchaProvider,
  resolveChallengeUrl,
  CAPTCHA_TOKEN_HEADER,
} from '../../src/services/captcha/types';
import { useCaptchaApi } from '../../src/services/captcha/api';
import { HttpClient } from '../../src/http/http';

describe('captcha types', () => {
  it('composes the image token as id:code and refuses half a pair', () => {
    expect(composeImageCaptchaToken('abc', '1234')).toBe('abc:1234');
    expect(composeImageCaptchaToken(' abc ', ' 1234 ')).toBe('abc:1234');
    expect(composeImageCaptchaToken('abc', '')).toBeUndefined();
    expect(composeImageCaptchaToken('', '1234')).toBeUndefined();
    expect(composeImageCaptchaToken(undefined, null)).toBeUndefined();
  });

  it('classifies providers', () => {
    for (const p of ['recaptcha', 'recaptcha-v3', 'hcaptcha', 'turnstile', 'altcha']) {
      expect(isScriptCaptchaProvider(p)).toBe(true);
    }
    expect(isScriptCaptchaProvider('image')).toBe(false);
    expect(isScriptCaptchaProvider('sliding')).toBe(false);
    expect(isScriptCaptchaProvider(null)).toBe(false);
    expect(isInvisibleCaptchaProvider('recaptcha-v3')).toBe(true);
    expect(isInvisibleCaptchaProvider('recaptcha')).toBe(false);
  });

  it('turns the challenge template into a client path: purpose filled in, rooted like every other API path', () => {
    // The backend publishes the template without a leading slash. `HttpClient.resolveUrl` is a
    // plain `baseUrl + path` join, so the raw template would become `/apiauth/captcha/...`.
    expect(resolveChallengeUrl({ challengeUrl: 'auth/captcha/{purpose}/json' }, 'login')).toBe('/auth/captcha/login/json');
    expect(resolveChallengeUrl({ challengeUrl: 'captcha/altcha/challenge?purpose={purpose}' }, 'a b')).toBe(
      '/captcha/altcha/challenge?purpose=a%20b',
    );
    expect(resolveChallengeUrl({ challengeUrl: null }, 'login')).toBeUndefined();
    expect(resolveChallengeUrl(null, 'login')).toBeUndefined();
  });

  it('leaves an already-rooted or absolute challenge template alone', () => {
    expect(resolveChallengeUrl({ challengeUrl: '/captcha/altcha/challenge?purpose={purpose}' }, 'login')).toBe(
      '/captcha/altcha/challenge?purpose=login',
    );
    expect(resolveChallengeUrl({ challengeUrl: 'https://api.example/api/captcha/altcha/challenge?purpose={purpose}' }, 'login')).toBe(
      'https://api.example/api/captcha/altcha/challenge?purpose=login',
    );
    expect(isAbsoluteUrl('https://api.example/x')).toBe(true);
    expect(isAbsoluteUrl('HTTP://api.example/x')).toBe(true);
    expect(isAbsoluteUrl('/captcha/x')).toBe(false);
    expect(isAbsoluteUrl('captcha/x')).toBe(false);
  });

  it('★ a real HttpClient resolves the client path to <apiBase>/captcha/... (the join that used to produce /apicaptcha)', () => {
    const client = new HttpClient({ baseUrl: '/api' });
    expect(client.resolveUrl(resolveChallengeUrl({ challengeUrl: 'captcha/altcha/challenge?purpose={purpose}' }, 'login')!)).toBe(
      '/api/captcha/altcha/challenge?purpose=login',
    );
    const absolute = new HttpClient({ baseUrl: 'https://api.example/api' });
    expect(absolute.resolveUrl(resolveChallengeUrl({ challengeUrl: 'captcha/altcha/challenge?purpose={purpose}' }, 'login')!)).toBe(
      'https://api.example/api/captcha/altcha/challenge?purpose=login',
    );
  });

  it('names the header the RequireCaptcha filter reads', () => {
    expect(CAPTCHA_TOKEN_HEADER).toBe('X-Captcha-Token');
  });
});

describe('useCaptchaApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('hits the captcha config route', async () => {
    const get = vi.fn(() => Promise.resolve({ succeeded: true, data: null }));
    const api = useCaptchaApi({ get } as unknown as HttpClient);

    await api.getConfig();

    expect(get).toHaveBeenCalledWith('/captcha/config');
  });

  // The endpoint answers the bare Altcha document (the widget validates a top-level `challenge`
  // key), not an ApiResult envelope. Read through `client.get`, the envelope normaliser put
  // `raw.data` (undefined) into `data`: a successful result with no challenge in it.
  it('returns the bare Altcha document the endpoint answers with', async () => {
    const doc = { algorithm: 'SHA-256', challenge: 'c0ffee', maxnumber: 100000, salt: 's?expires=1', signature: 'sig' };
    const fetchMock = vi.fn(() => Promise.resolve(new Response(JSON.stringify(doc), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })));
    vi.stubGlobal('fetch', fetchMock);
    const api = useCaptchaApi(new HttpClient({ baseUrl: '/api' }));

    const result = await api.getAltchaChallenge('contact');

    expect(result.succeeded).toBe(true);
    expect(result.data).toEqual(doc);
    expect(fetchMock).toHaveBeenCalledWith('/api/captcha/altcha/challenge?purpose=contact', expect.anything());
  });

  it('keeps a failed envelope a failed result', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(
      JSON.stringify({ code: 404, success: false, message: 'Altcha is not the active captcha provider.' }),
      { status: 404, headers: { 'Content-Type': 'application/json' } },
    ))));
    const api = useCaptchaApi(new HttpClient({ baseUrl: '/api' }));

    const result = await api.getAltchaChallenge('contact');

    expect(result.succeeded).toBe(false);
    expect(result.code).toBe(404);
    expect(result.data).toBeUndefined();
  });

  it('reports a body that is not a challenge document as a failure', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('<html></html>', {
      status: 200,
      headers: { 'Content-Type': 'text/html' },
    }))));
    const api = useCaptchaApi(new HttpClient({ baseUrl: '/api' }));

    const result = await api.getAltchaChallenge('contact');

    expect(result.succeeded).toBe(false);
  });
});
