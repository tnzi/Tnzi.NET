/**
 * `useLoginCaptcha` - owns the image-captcha state for one login flow.
 *
 * Fetches a fresh captcha through `useLoginContext().callbacks.getCaptcha(purpose)`
 * (wired to `GET /auth/captcha/{purpose}/json`) and tracks the current
 * `captchaId` + `imageBase64` + the `code` the user types. The two flows drive
 * it differently:
 *   - **register** (always-show) calls `load()` on mount and `load()` again
 *     after each send (the captcha is one-time-use - verifying consumes it).
 *   - **login** (adaptive) calls `seed()` with the captcha the backend returned
 *     in its `IDENTITY_CAPTCHA_REQUIRED` error, and `load()` for the manual
 *     refresh button.
 *
 * HTTP-agnostic: the consumer wires `getCaptcha`; the composable stays a thin
 * reactive holder so `PwdLogin` / `Register` bind it straight to `TLoginCaptcha`.
 */
import { ref, type Ref } from 'vue'
import { useLoginContext, type LoginCaptchaData } from '../pages/login/useLoginContext'

export interface UseLoginCaptchaReturn {
  /** Id of the currently displayed captcha (sent alongside the typed code). */
  captchaId: Ref<string>
  /** Base64 PNG of the current captcha (no data-uri prefix). */
  imageBase64: Ref<string>
  /** The code the user typed. */
  code: Ref<string>
  /** A fetch is in flight. */
  loading: Ref<boolean>
  /** Last fetch error (empty when none). */
  error: Ref<string>
  /** Fetch a fresh captcha from the backend (clears the typed code). */
  load: () => Promise<void>
  /** Seed from a captcha the backend pushed inline (clears the typed code). */
  seed: (captcha: LoginCaptchaData) => void
  /** Clear id + image + code + error (e.g. when hiding the field). */
  reset: () => void
  /** Whether a manual refresh is possible (the `getCaptcha` callback is wired). */
  canRefresh: boolean
}

export function useLoginCaptcha(purpose: 'login' | 'register'): UseLoginCaptchaReturn {
  const { callbacks, translate } = useLoginContext()
  const captchaId = ref('')
  const imageBase64 = ref('')
  const code = ref('')
  const loading = ref(false)
  const error = ref('')

  async function load(): Promise<void> {
    const getCaptcha = callbacks.getCaptcha
    if (!getCaptcha) return
    loading.value = true
    error.value = ''
    try {
      const c = await getCaptcha(purpose)
      captchaId.value = c.captchaId
      imageBase64.value = c.imageBase64
      code.value = ''
    } catch (e) {
      error.value =
        e instanceof Error ? e.message : translate('admin.login.captcha.loadFailed', 'Failed to load captcha')
    } finally {
      loading.value = false
    }
  }

  function seed(captcha: LoginCaptchaData): void {
    captchaId.value = captcha.captchaId
    imageBase64.value = captcha.imageBase64
    code.value = ''
    error.value = ''
  }

  function reset(): void {
    captchaId.value = ''
    imageBase64.value = ''
    code.value = ''
    error.value = ''
  }

  return {
    captchaId,
    imageBase64,
    code,
    loading,
    error,
    load,
    seed,
    reset,
    canRefresh: !!callbacks.getCaptcha,
  }
}
