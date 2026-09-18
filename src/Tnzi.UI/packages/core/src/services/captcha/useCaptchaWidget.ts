/**
 * `useCaptchaWidget` - Vue wrapper around {@link mountCaptchaWidget}.
 *
 * Owns the token for one script-rendered captcha (reCAPTCHA v2 / v3, hCaptcha, Turnstile,
 * Altcha): mounts when the container element and a usable config are both present,
 * re-mounts when the provider changes, tears down on scope dispose. The `image` and
 * `sliding` providers are not scripts; `isScriptProvider` is `false` for them and the
 * UI package renders its own input instead.
 *
 * @example
 * ```ts
 * const { container, token, execute, reset, error } = useCaptchaWidget({
 *   config: () => features.captcha,
 *   purpose: 'login',
 *   client,
 * })
 * // <div ref="container" />  …  await execute() before submit (needed for reCAPTCHA v3)
 * ```
 */
import { computed, onScopeDispose, ref, shallowRef, watch, type ComputedRef, type Ref } from 'vue';
import type { HttpClient } from '../../http/http';
import type { CaptchaClientConfigDto } from './types';
import { isAbsoluteUrl, isInvisibleCaptchaProvider, isScriptCaptchaProvider, resolveChallengeUrl } from './types';
import { CaptchaWidgetError, mountCaptchaWidget, type CaptchaWidgetHandle, type CaptchaWidgetOptions } from './widget';

export interface UseCaptchaWidgetOptions {
  /** The client config (from `/captcha/config` or `/auth/config`'s `captcha`). Reactive getter or ref. */
  config: (() => CaptchaClientConfigDto | null | undefined) | Ref<CaptchaClientConfigDto | null | undefined>;
  /** The purpose the token is for (`login` / `register` / your endpoint's `[RequireCaptcha]` purpose). */
  purpose: string;
  /**
   * Resolves the config's API-relative challenge template against the API base (the `HttpClient`
   * the config was fetched with). **Required for Altcha** unless the deployment publishes an absolute
   * `challengeUrl`: the widget fetches the challenge itself, so without the base it asks the page's
   * origin and gets the SPA's `index.html` back. A missing client is reported through `error`
   * rather than silently producing a widget that can never verify.
   */
  client?: Pick<HttpClient, 'resolveUrl'>;
  /** Passed through to the widget. */
  theme?: CaptchaWidgetOptions['theme'];
  altchaAttributes?: CaptchaWidgetOptions['altchaAttributes'];
  /** Optional translator for the error messages (keys `auth.captchaLoadFailed` / `auth.captchaInvalid` from the core locales). */
  translate?: (key: string, fallback?: string) => string;
}

export interface UseCaptchaWidgetReturn {
  /** Bind to the element the widget renders into (`<div ref="container" />`). */
  container: Ref<HTMLElement | null>;
  /** The current token; empty until solved, cleared on reset / expiry. */
  token: Ref<string>;
  /** The widget is mounted and can produce tokens. */
  ready: Ref<boolean>;
  /** Last mount / widget error message (empty when none). */
  error: Ref<string>;
  /** The active provider renders through a script (vs. `image` / `sliding` / none). */
  isScriptProvider: ComputedRef<boolean>;
  /** The active provider produces its token on demand (reCAPTCHA v3): call `execute()` right before submit. */
  isInvisible: ComputedRef<boolean>;
  /** Obtain a token now (see {@link CaptchaWidgetHandle.execute}). */
  execute: () => Promise<string>;
  /** Clear the widget + token so the user solves it again. */
  reset: () => void;
}

/**
 * The URL the provider's widget can actually fetch: the challenge template with the purpose
 * filled in, resolved against the API base through the client. A provider without a challenge
 * template (the hosted ones) yields `undefined`; an absolute template needs no client.
 *
 * Refuses to guess when the template is API-relative and no client was given. The only other
 * option is to hand the widget a page-relative path, and that fails in the one way nobody
 * notices at build time or start-up: under `/admin/login` the widget fetches
 * `/admin/captcha/altcha/challenge`, receives the SPA's `index.html`, and every user is locked
 * out from the moment the captcha is demanded.
 */
export function toFetchableChallengeUrl(
  config: Pick<CaptchaClientConfigDto, 'challengeUrl'>,
  purpose: string,
  client: Pick<HttpClient, 'resolveUrl'> | undefined,
): string | undefined {
  const path = resolveChallengeUrl(config, purpose);
  if (!path || isAbsoluteUrl(path)) return path;
  if (!client) {
    throw new CaptchaWidgetError(
      `The captcha challenge URL "${path}" is relative to the API root; pass the HttpClient (\`client\`) so it can be resolved against the API base.`,
    );
  }
  return client.resolveUrl(path);
}

export function useCaptchaWidget(options: UseCaptchaWidgetOptions): UseCaptchaWidgetReturn {
  const container = ref<HTMLElement | null>(null);
  const token = ref('');
  const ready = ref(false);
  const error = ref('');
  const handle = shallowRef<CaptchaWidgetHandle | null>(null);
  let mountSeq = 0;

  const config = computed(() => (typeof options.config === 'function' ? options.config() : options.config.value) ?? null);
  const isScriptProvider = computed(() => !!config.value?.enabled && isScriptCaptchaProvider(config.value.provider));
  const isInvisible = computed(() => isScriptProvider.value && isInvisibleCaptchaProvider(config.value?.provider));

  function t(key: string, fallback: string): string {
    return options.translate ? options.translate(key, fallback) : fallback;
  }

  function teardown(): void {
    handle.value?.destroy();
    handle.value = null;
    ready.value = false;
    token.value = '';
  }

  async function mount(): Promise<void> {
    teardown();
    error.value = '';
    const cfg = config.value;
    const el = container.value;
    if (!cfg || !el || !isScriptProvider.value) return;

    const seq = ++mountSeq;
    try {
      const mounted = await mountCaptchaWidget(el, cfg, {
        purpose: options.purpose,
        challengeUrl: toFetchableChallengeUrl(cfg, options.purpose, options.client),
        theme: options.theme,
        altchaAttributes: options.altchaAttributes,
        onToken: (value) => {
          token.value = value;
        },
        onExpired: () => {
          token.value = '';
        },
        onError: (err) => {
          token.value = '';
          error.value = err instanceof Error ? err.message : t('auth.captchaInvalid', 'Captcha verification failed, please try again.');
        },
      });
      // A newer mount superseded this one while the script was loading.
      if (seq !== mountSeq) {
        mounted.destroy();
        return;
      }
      handle.value = mounted;
      ready.value = true;
    } catch (err) {
      if (seq !== mountSeq) return;
      error.value = err instanceof Error ? err.message : t('auth.captchaLoadFailed', 'Failed to load the captcha widget.');
    }
  }

  watch([container, () => config.value?.provider, () => config.value?.enabled, () => config.value?.siteKey], () => {
    void mount();
  }, { immediate: true });

  onScopeDispose(teardown);

  return {
    container,
    token,
    ready,
    error,
    isScriptProvider,
    isInvisible,
    execute: async () => {
      if (!handle.value) throw new Error(error.value || 'Captcha widget is not ready.');
      const value = await handle.value.execute();
      token.value = value;
      return value;
    },
    reset: () => {
      token.value = '';
      handle.value?.reset();
    },
  };
}
