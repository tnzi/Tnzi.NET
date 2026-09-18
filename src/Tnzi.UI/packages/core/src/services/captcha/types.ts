/**
 * Captcha (human verification) contracts.
 * Backend: Tnzi.AspNetCore `ICaptchaVerifier` / `DefaultCaptchaController` ([Route("captcha")]),
 * plus the `image` provider shipped by Tnzi.Identity (`GET /auth/captcha/{purpose}/json`).
 */

/**
 * Provider names the framework ships. Consumers can register their own on the
 * backend (`services.AddCaptchaProvider<T>()`), so the type stays open.
 */
export type CaptchaProviderName =
  | 'image'
  | 'sliding'
  | 'recaptcha'
  | 'recaptcha-v3'
  | 'hcaptcha'
  | 'turnstile'
  | 'altcha'
  | (string & {});

/**
 * What the browser needs to render the active provider's widget.
 * `GET /captcha/config`; also embedded as `captcha` in `GET /auth/config`.
 * Public values only: the site key is meant for the page, the challenge endpoint is anonymous.
 */
export interface CaptchaClientConfigDto {
  /** `false` = no provider configured; every other field is empty and nothing should render. */
  enabled: boolean;
  provider?: CaptchaProviderName | null;
  /** Public site key (hosted providers). */
  siteKey?: string | null;
  /** Widget script to load (hosted providers + Altcha). Deployments may point it at a self-hosted copy. */
  scriptUrl?: string | null;
  /**
   * Challenge endpoint template, relative to the API root (no leading slash, the part after `api/`)
   * and with a `{purpose}` placeholder (`auth/captcha/{purpose}/json` for `image`,
   * `captcha/altcha/challenge?purpose={purpose}` for `altcha`). Turn it into a path for the
   * `HttpClient` with {@link resolveChallengeUrl}; the client's `resolveUrl` adds the API base.
   */
  challengeUrl?: string | null;
}

/**
 * A challenge the backend hands the page. For the `image` provider it carries the picture;
 * for every other provider only `provider` is set and the page renders that provider's widget.
 * Shape of `CaptchaDto` on the backend (the `errorDetails` of `IDENTITY_CAPTCHA_REQUIRED`
 * and the body of `GET /auth/captcha/{purpose}/json`).
 */
export interface CaptchaChallengeDto {
  provider: CaptchaProviderName;
  captchaId?: string | null;
  /** Base64 PNG, no data-uri prefix. */
  imageBase64?: string | null;
  expirationSeconds?: number | null;
}

/** Altcha proof-of-work challenge, exactly as the `<altcha-widget>` expects it. */
export interface AltchaChallengeDto {
  algorithm: string;
  challenge: string;
  maxnumber: number;
  salt: string;
  signature: string;
}

/** Request header that carries the token for `[RequireCaptcha]` endpoints (body `captchaToken` also works). */
export const CAPTCHA_TOKEN_HEADER = 'X-Captcha-Token';

/** Providers whose widget is a third-party script mounted into a container element. */
const SCRIPT_PROVIDERS: ReadonlySet<string> = new Set(['recaptcha', 'recaptcha-v3', 'hcaptcha', 'turnstile', 'altcha']);

/** Whether the provider renders through a script-loaded widget (vs. the built-in image / sliding puzzles). */
export function isScriptCaptchaProvider(provider: string | null | undefined): boolean {
  return !!provider && SCRIPT_PROVIDERS.has(provider);
}

/** Whether the provider obtains its token on demand at submit time (no user interaction). */
export function isInvisibleCaptchaProvider(provider: string | null | undefined): boolean {
  return provider === 'recaptcha-v3';
}

/**
 * The `image` provider's token: `{captchaId}:{code}`. `undefined` when either half is missing,
 * so callers can send "no captcha" rather than a broken token.
 */
export function composeImageCaptchaToken(captchaId: string | null | undefined, code: string | null | undefined): string | undefined {
  const id = captchaId?.trim();
  const answer = code?.trim();
  if (!id || !answer) return undefined;
  return `${id}:${answer}`;
}

/** An absolute URL (`https://…`, `http://…`): the `HttpClient` passes these through untouched. */
export function isAbsoluteUrl(url: string): boolean {
  return /^https?:\/\//i.test(url);
}

/**
 * Turn a challenge template into the path to hand to the `HttpClient`: the `{purpose}` placeholder
 * filled in, and a leading slash added when the template is API-relative, so
 * `client.resolveUrl(resolveChallengeUrl(config, purpose))` yields `<apiBase>/captcha/...`
 * exactly like every other path this package sends (`/captcha/config`, `/auth/login`).
 *
 * The backend publishes the template **without** a leading slash (`captcha/altcha/challenge?…`);
 * `HttpClient.resolveUrl` is a plain `baseUrl + path` join, so handing it the raw template produced
 * `/apicaptcha/altcha/challenge` against a `/api` base. Absolute templates pass through untouched.
 * `undefined` when the provider has none.
 */
export function resolveChallengeUrl(config: Pick<CaptchaClientConfigDto, 'challengeUrl'> | null | undefined, purpose: string): string | undefined {
  const template = config?.challengeUrl;
  if (!template) return undefined;
  const filled = template.replace('{purpose}', encodeURIComponent(purpose));
  if (isAbsoluteUrl(filled) || filled.startsWith('/')) return filled;
  return `/${filled}`;
}
