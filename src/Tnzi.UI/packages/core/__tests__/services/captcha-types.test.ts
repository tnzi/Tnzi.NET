import { describe, it, expect, vi } from 'vitest';
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
  it('hits the captcha controller routes', async () => {
    const get = vi.fn(() => Promise.resolve({ succeeded: true, data: null }));
    const api = useCaptchaApi({ get } as unknown as HttpClient);

    await api.getConfig();
    await api.getAltchaChallenge('login');

    expect(get).toHaveBeenNthCalledWith(1, '/captcha/config');
    expect(get).toHaveBeenNthCalledWith(2, '/captcha/altcha/challenge', { params: { purpose: 'login' } });
  });
});
