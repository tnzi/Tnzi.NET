/**
 * `useCaptcha` - countdown + loading state for verification-code buttons.
 *
 * Mirrors `soybean-admin-example/src/hooks/business/captcha.ts`. Owns the
 * 60-second cooldown timer and the in-flight "sending" flag so the consumer
 * can bind the result straight to an `NButton :disabled :loading`.
 *
 * The actual "send code" call is supplied by the consumer (via a callback
 * passed to `getCaptcha`) - `useCaptcha` stays HTTP-agnostic.
 *
 * @example
 * ```ts
 * const { label, isCounting, loading, getCaptcha } = useCaptcha()
 *
 * function send() {
 *   getCaptcha(async () => {
 *     await callbacks.sendCode?.({ account: model.account, purpose: 'code-login' })
 *   })
 * }
 * ```
 */
import { computed, onScopeDispose, ref, type ComputedRef, type Ref } from 'vue'

export interface UseCaptchaOptions {
  /** Countdown seconds. Default 60 (soybean's signature). */
  seconds?: number
  /** Translator. Falls back to English literals when missing. */
  translate?: (key: string, fallback?: string) => string
}

export interface UseCaptchaReturn {
  /**
   * Reactive label for the send-code button:
   *   - normal: "Get Code"
   *   - counting: "60s"
   *   - loading: "" (button shows its spinner instead)
   */
  label: ComputedRef<string>
  /** Whether the countdown is currently running. */
  isCounting: Ref<boolean>
  /** Whether a `sendCode` callback is currently in flight. */
  loading: Ref<boolean>
  /** Current count value (seconds remaining). 0 when not counting. */
  count: Ref<number>
  /**
   * Trigger a send-code request. The `sender` callback is invoked once
   * (`await`ed). On success the countdown starts; on rejection it doesn't,
   * so the button stays clickable.
   */
  getCaptcha: (sender: () => Promise<void> | void) => Promise<void>
  /** Force-stop the countdown (e.g. on form reset). */
  stop: () => void
}

export function useCaptcha(opts: UseCaptchaOptions = {}): UseCaptchaReturn {
  const seconds = opts.seconds ?? 60
  const translate = opts.translate

  const count = ref(0)
  const isCounting = ref(false)
  const loading = ref(false)
  let timer: ReturnType<typeof setInterval> | null = null

  function t(key: string, fallback: string): string {
    if (!translate) return fallback
    return translate(key, fallback)
  }

  function stop(): void {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
    isCounting.value = false
    count.value = 0
  }

  function start(): void {
    count.value = seconds
    isCounting.value = true
    timer = setInterval(() => {
      count.value -= 1
      if (count.value <= 0) stop()
    }, 1000)
  }

  async function getCaptcha(sender: () => Promise<void> | void): Promise<void> {
    if (loading.value || isCounting.value) return
    loading.value = true
    try {
      await sender()
      start()
    } finally {
      loading.value = false
    }
  }

  const label = computed<string>(() => {
    if (loading.value) return ''
    if (isCounting.value) {
      return t('admin.login.code.reGetCode', `${count.value}s`)
        .replace('{time}', String(count.value))
    }
    return t('admin.login.code.getCode', 'Get Code')
  })

  onScopeDispose(stop)

  return { label, isCounting, loading, count, getCaptcha, stop }
}
