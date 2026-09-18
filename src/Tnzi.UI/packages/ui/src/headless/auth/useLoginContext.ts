/**
 * `useLoginContext` - communication channel between `TLoginPage` shell and the
 * five login modules (pwd-login / code-login / register / reset-pwd / bind-wechat).
 *
 * `TLoginPage` provides this context once. Each module injects it to:
 *   - look up the active translate function (locale-reactive),
 *   - switch to a sibling module via `toggleLoginModule('reset-pwd')`,
 *   - call the consumer-supplied auth callbacks (Phase I.7.2+).
 *
 * Phase I.7.1 only wires `translate` and `toggleLoginModule` (callbacks are
 * empty placeholders that the modules don't yet invoke).
 */
import { inject, provide, reactive, ref, type InjectionKey, type Ref } from 'vue'
import type { CaptchaClientConfigDto } from '@tnzi/core/services/captcha'

export type LoginModule =
  | 'pwd-login'
  | 'code-login'
  | 'register'
  | 'reset-pwd'
  | 'bind-wechat'
  | 'two-factor'
  | 'pending-actions'

/**
 * Outstanding 2FA challenge - populated by `pwdLogin` / `codeLogin` callbacks
 * via `helpers.setTwoFactorRequired(challenge)` when the backend returns
 * `requires2FA: true`. The `TwoFactorChallenge` module reads this to know
 * which user / method to verify against.
 */
/** A 2FA delivery channel. */
export type TwoFactorMethodName = 'totp' | 'sms' | 'email'

/**
 * A pending action the account owes before it can be used.
 *
 * Populated by the login callbacks via `helpers.setPendingActionRequired(...)`
 * when the backend answers 403 `IDENTITY_PENDING_ACTIONS_REQUIRED`. Credentials
 * already checked out and 2FA (if any) already passed - the person is who they
 * say they are, they just have to finish something first.
 *
 * ★ Without a module reading this, the backend's challenge surfaces as a plain
 * "login failed" and the user has nowhere to go, with a password that is correct.
 */
export interface PendingActionChallenge {
  /** Single-use token from the challenge, 10 minutes. Spend it on the completing call. */
  tempToken: string
  /** User-facing label so the form can say who is being changed. */
  userName?: string
  /** Outstanding action names, e.g. `['ChangePassword']`. */
  requiredActions: string[]
}

/** 完成强制改密时提交的内容。刻意不含账号：改谁由 tempToken 决定。 */
export interface CompletePasswordChangePayload {
  tempToken: string
  newPassword: string
}

/** 完成一件需要验证码的待办（绑验证器 / 确认邮箱）。 */
export interface CompletePendingActionCodePayload {
  tempToken: string
  code: string
}

/**
 * 办完一件待办之后的结果。
 *
 * `completed: false` 表示还欠着别的，**临时令牌仍然可用** —— 同一枚继续办下一件。
 */
export interface PendingActionOutcome {
  completed: boolean
  remainingActions: string[]
  /**
   * 后端没有直接签发会话，而是要求再过一道二次验证（账号还开着别的 2FA 方式，办完这件
   * 之后签发仍要过守卫链）。挑战已经经 helpers 交给 shell（切到 two-factor 模块），本页
   * 到此为止；此时 `completed` 为 false、`remainingActions` 为空，且这枚待办令牌已被消费。
   */
  challenged?: boolean
}

/** 还欠哪些事，以及办它们需要的材料。 */
export interface PendingActionDescription {
  requiredActions: string[]
  userName: string
  /** 欠着绑验证器时带上密钥与 otpauth 地址，否则前端没有二维码可扫。 */
  totpSetup?: { sharedKey: string; authenticatorUri: string } | null
  maskedEmail?: string | null
}

export interface TwoFactorChallenge {
  /** Challenge identifier returned by the backend (e.g. session token). */
  challengeId?: string
  /** User-facing label so the form can show "Verifying as Alice". */
  userName?: string
  /** Default / preferred delivery channel - drives the helper copy in the module. */
  method?: TwoFactorMethodName
  /**
   * ALL methods the user has enabled for this challenge. When more than one,
   * the challenge module renders a "use another method" switcher so the user
   * can verify with a fallback (e.g. switch from the authenticator app to an
   * emailed code). Omitted / single-entry → no switcher.
   */
  methods?: TwoFactorMethodName[]
  /**
   * Masked destination the code was delivered to (e.g. `j***@example.com`,
   * `•••••2671`) for SMS / email challenges. Surfaced in the prompt so the
   * user knows exactly where to look. Populated by the initial send (SMS/email
   * preferred) or by `resendTwoFactor`'s return value; absent for TOTP.
   */
  maskedAddress?: string
}

/** Result of a (re)send - carries the masked destination so the UI can show it. */
export interface ResendTwoFactorResult {
  /** Masked phone / email the code was sent to (e.g. `j***@example.com`). */
  maskedAddress?: string | null
}

/**
 * A captcha challenge the login page renders.
 *
 * `provider` says what to render: for `image` (the built-in picture) the
 * `captchaId` + `imageBase64` pair is present; for every other provider
 * (Turnstile, hCaptcha, reCAPTCHA, Altcha, ...) only `provider` is set and the
 * page renders that provider's widget from `LoginFeatures.captcha`.
 *
 * The flows differ in WHEN the challenge appears:
 *   - **login** is adaptive - the backend only demands a captcha after repeated
 *     failures (`Identity:Captcha:CaptchaFailThreshold`) and answers
 *     `IDENTITY_CAPTCHA_REQUIRED` with this shape in `errorDetails`, which
 *     `PwdLogin` renders inline.
 *   - **register / code-login / reset-pwd** show the captcha up-front and gate
 *     the send-code step (those endpoints spend a real SMS / email per call).
 */
export interface LoginCaptchaData {
  provider: string
  captchaId?: string | null
  /** Base64 PNG, no data-uri prefix (`image` provider only). */
  imageBase64?: string | null
  expirationSeconds?: number | null
}

/**
 * No `remember` flag here on purpose. The login pages used to carry a
 * "Remember me" checkbox whose value nothing ever read: the default callbacks
 * dropped it and `AuthStateManager` persists the token pair the same way
 * regardless, so unticking it changed nothing while looking like a choice.
 * Session-scoped persistence is a core feature (a session storage adapter
 * for the token pair); until it exists the payload does not pretend.
 */
export interface PwdLoginPayload {
  userName: string
  password: string
  /**
   * Captcha token from the unified widget (any provider) - set once the adaptive
   * login captcha is revealed and solved. The `image` provider's token is
   * `{captchaId}:{code}`; `TCaptcha` composes it.
   */
  captchaToken?: string
  /** @deprecated Legacy image-captcha id; prefer `captchaToken`. */
  captchaId?: string
  /** @deprecated Legacy image-captcha code; prefer `captchaToken`. */
  captchaCode?: string
}

export interface CodeLoginPayload {
  /** Phone number or email address (depends on `type`). */
  account: string
  code: string
  /** Verification target. Defaults to `'phone'`. */
  type?: 'phone' | 'email'
}

export interface RegisterPayload {
  account: string
  code: string
  password: string
  type?: 'phone' | 'email'
}

export interface ResetPwdPayload {
  account: string
  code: string
  password: string
  type?: 'phone' | 'email'
}

export interface SendCodePayload {
  account: string
  type?: 'phone' | 'email'
  /** Which flow the code is for. Drives endpoint selection. */
  purpose: 'code-login' | 'register' | 'reset-pwd'
  /**
   * Captcha token from the unified widget (any provider). Sent when the matching
   * backend switch is on (`captchaOnLogin` for code-login, `captchaOnRegister`
   * for register, `captchaOnPasswordRecovery` for reset-pwd) - those endpoints
   * spend a real SMS / email on every call.
   */
  captchaToken?: string
  /** @deprecated Legacy image-captcha id; prefer `captchaToken`. */
  captchaId?: string
  /** @deprecated Legacy image-captcha code; prefer `captchaToken`. */
  captchaCode?: string
}

export interface VerifyTwoFactorPayload {
  /** Challenge id returned by the original login response. */
  challengeId?: string
  /** The 6-digit (or longer) code the user typed. */
  code: string
  /** The method the user chose to verify with (when they switched methods).
   *  Omitted → the challenge's default/preferred method. */
  method?: TwoFactorMethodName
}

/**
 * Helpers passed as the second argument to `pwdLogin` / `codeLogin`
 * callbacks. Lets consumer code transition the login UI into the
 * `two-factor` module without reaching into Vue state directly.
 *
 * Usage in `defineAdminApp({ login: { callbacks: { pwdLogin } } })`:
 *
 * ```ts
 * pwdLogin: async ({ userName, password }, { setTwoFactorRequired }) => {
 *   const res = await api.login({ userName, password })
 *   if (res.requires2FA) {
 *     setTwoFactorRequired({ challengeId: res.challengeId, userName })
 *     return  // stay on /login, the shell switches to the 2FA module
 *   }
 *   await router.replace('/admin')
 * }
 * ```
 */
export interface LoginCallbackHelpers {
  setTwoFactorRequired: (challenge: TwoFactorChallenge) => void
  clearTwoFactor: () => void
  /**
   * Switch to the module that lets the user discharge a pending action
   * (currently: a forced password change).
   */
  setPendingActionRequired: (challenge: PendingActionChallenge) => void
  /** Dismiss the pending-action state (e.g. after it is discharged). */
  clearPendingAction: () => void
  /**
   * Reveal the adaptive login captcha with the challenge the backend returned
   * in its `IDENTITY_CAPTCHA_REQUIRED` response (a fresh picture for the
   * `image` provider, just the provider name otherwise). `PwdLogin` watches
   * `pendingCaptcha` and shows the captcha field seeded with it.
   */
  setCaptchaRequired: (captcha: LoginCaptchaData) => void
  /** Dismiss the captcha field (e.g. on a successful login). */
  clearCaptcha: () => void
}

/**
 * Auth callbacks supplied by the page consumer (the host application).
 *
 * All callbacks are optional - when a module's flow callback is missing the
 * module shows a "not configured" hint. Phase I.7.2+ wires each module to
 * the corresponding callback.
 */
export interface LoginCallbacks {
  pwdLogin?: (payload: PwdLoginPayload, helpers: LoginCallbackHelpers) => Promise<void>
  codeLogin?: (payload: CodeLoginPayload, helpers: LoginCallbackHelpers) => Promise<void>
  /**
   * 完成登录时被要求的密码修改。成功后直接建立会话 —— 用户不必再登录一次。
   * 由 `ChangePassword` 模块在拿到待办挑战后调用。
   */
  /**
   * 读还欠哪些事 + 材料。不消费令牌。
   */
  describePendingActions?: (tempToken: string) => Promise<PendingActionDescription | null>
  /**
   * 完成强制改密。全部办完则建立会话，否则返回还剩什么。
   *
   * ★ `helpers` 是可选的，但**强烈建议传**：办完之后签发会话仍要过守卫链，账号还开着别的
   * 2FA 方式时后端答 403 `2FA_REQUIRED` + 新临时令牌 —— 事情已经办完（密码已改、这枚待办
   * 令牌已消费），同页重试必再失败。有 helpers 才能把挑战交给 shell；不传则报错，与从前一致。
   */
  completePasswordChange?: (
    payload: CompletePasswordChangePayload,
    helpers?: LoginCallbackHelpers,
  ) => Promise<PendingActionOutcome>
  /** 完成绑定验证器。`helpers` 的意义同 `completePasswordChange`。 */
  completeTotpEnrollment?: (
    payload: CompletePendingActionCodePayload,
    helpers?: LoginCallbackHelpers,
  ) => Promise<PendingActionOutcome>
  /** 给「确认邮箱」发码。地址由服务端取，不接受指定。 */
  sendPendingActionEmailCode?: (tempToken: string) => Promise<void>
  /** 完成确认邮箱。`helpers` 的意义同 `completePasswordChange`。 */
  completeEmailConfirmation?: (
    payload: CompletePendingActionCodePayload,
    helpers?: LoginCallbackHelpers,
  ) => Promise<PendingActionOutcome>
  register?: (payload: RegisterPayload) => Promise<void>
  resetPwd?: (payload: ResetPwdPayload) => Promise<void>
  sendCode?: (payload: SendCodePayload) => Promise<void>
  /**
   * Submit the 2FA code the user typed after a `pwdLogin` / `codeLogin`
   * returned `requires2FA: true`. Maps to
   * `POST /auth/verify-2fa` (Tnzi.Identity.DefaultAuthController.VerifyTwoFactor).
   */
  /**
   * ★ `helpers` 是可选的，但**强烈建议传**：2FA 通过之后后端可能紧接着要求一件待办
   * （强制改密），而那个挑战只能经 helpers 交给 shell。不传的话用户会看到
   * 「验证失败」而他的验证码其实是对的。既有调用方不传仍可编译，行为与从前一致。
   */
  verifyTwoFactor?: (payload: VerifyTwoFactorPayload, helpers?: LoginCallbackHelpers) => Promise<void>
  /**
   * Optionally (re)send the 2FA code (SMS / email channels). Maps to
   * `POST /auth/send-2fa-code`. Also called when the user switches TO an
   * SMS/email method so the first code is delivered. Hidden when not provided.
   * May return the masked destination (`{ maskedAddress }`) so the challenge
   * module can show "Code sent to j***@example.com".
   */
  resendTwoFactor?: (payload: {
    challengeId?: string
    method?: TwoFactorMethodName
  }) => Promise<ResendTwoFactorResult | void>
  /**
   * Fetch a fresh built-in image captcha for the given purpose. Wired to
   * `GET /auth/captcha/{purpose}/json`. Only the `image` provider uses it: the
   * field (re)loads the picture through it; when omitted, the field can still
   * render a captcha the backend pushed inline (login's adaptive flow) but the
   * refresh button is hidden. Script providers (Turnstile, hCaptcha, ...) never
   * call it.
   */
  getCaptcha?: (purpose: string) => Promise<LoginCaptchaData>
}

/**
 * Visual style hints derived from the shell's `layout` (2026-06-11 redesign).
 * Modules read these instead of hardcoding form chrome so the same module
 * renders correctly in both layouts:
 *   - `wave`  → `{ labeled: false, pill: true }` - icon-less compact inputs,
 *     pill (round) primary buttons; the direct evolution of the legacy page.
 *   - `split` → `{ labeled: true, pill: false }` - careercompass-style
 *     stacked labels above each input, squared buttons.
 */
export interface LoginUiStyle {
  /** Render a stacked label above each form input. */
  labeled: boolean
  /** Render primary/secondary action buttons as pills (`round`). */
  pill: boolean
}

/**
 * Live form-interaction signals for decorative shell elements (the split
 * layout's animated characters watch these). `PwdLogin` writes; the brand
 * panel reads. All fields are plain reactive state on a `reactive({})`
 * object provided by the shell:
 *   - `typing` - the username field has focus (characters lean in to peek)
 *   - `passwordVisible` - the password is shown in clear text (characters
 *     politely look away… mostly)
 *   - `passwordLength` - characters of password typed so far
 */
export interface LoginSceneState {
  typing: boolean
  passwordVisible: boolean
  passwordLength: number
}

/**
 * Feature flags driving which login methods / entries the page shows and what
 * the password account field accepts. Sourced from the backend
 * `GET /auth/config` (mapped in the login route) and optionally overridden by
 * the consumer via `defineAdminApp({ login: { features } })`. The
 * everything-enabled fallback keeps isolated module mounts and config-fetch
 * failures behaving exactly as before this feature landed.
 */
export interface LoginFeatures {
  /** Show the password-login form (almost always true). */
  passwordLogin: boolean
  /** Show the code-login entry + module. */
  codeLogin: boolean
  /** Show the register entry + module. */
  register: boolean
  /** Show the forgot-password entry + module. */
  passwordRecovery: boolean
  /** Identifier types the password-login account field accepts. */
  identifiers: { userName: boolean; email: boolean; phone: boolean }
  /** Channels available for code-based flows (code-login / register / recovery). */
  codeChannels: { sms: boolean; email: boolean }
  /**
   * Captcha is enabled on the password-login flow. When on, the backend demands
   * a captcha adaptively (after repeated failures); `PwdLogin` only reveals the
   * field once the backend asks (`IDENTITY_CAPTCHA_REQUIRED`). The code-login
   * send-code step is gated unconditionally.
   */
  captchaOnLogin: boolean
  /** Captcha is enabled on the register flow - shown up-front (gates send-code). */
  captchaOnRegister: boolean
  /** Captcha is enabled on the password-recovery flow - shown up-front (gates send-code). */
  captchaOnPasswordRecovery: boolean
  /**
   * Which provider to render and with what (`GET /auth/config` → `captcha`).
   * `null` when the backend predates the provider layer; the field then falls
   * back to the built-in image captcha through `callbacks.getCaptcha`.
   */
  captcha: CaptchaClientConfigDto | null
}

/**
 * Everything-enabled defaults - the fallback when no backend config is loaded
 * (isolated module mounts, `/auth/config` fetch failures, or a backend too old
 * to ship the endpoint). Treated as read-only; callers that merge must clone.
 */
export const DEFAULT_LOGIN_FEATURES: LoginFeatures = Object.freeze({
  passwordLogin: true,
  codeLogin: true,
  register: true,
  passwordRecovery: true,
  identifiers: Object.freeze({ userName: true, email: true, phone: true }),
  codeChannels: Object.freeze({ sms: true, email: true }),
  // Captcha is an opt-in security add-on (backend default off). Defaulting it
  // off on config-failure avoids showing a captcha field the backend ignores.
  captchaOnLogin: false,
  captchaOnRegister: false,
  captchaOnPasswordRecovery: false,
  captcha: null,
}) as LoginFeatures

/**
 * Consumer-supplied feature overrides (merged on top of the backend config).
 * Nested groups are shallow-merged, so a consumer can flip a single channel
 * (e.g. `{ codeChannels: { sms: false } }`) without restating the rest.
 */
export interface PartialLoginFeatures {
  passwordLogin?: boolean
  codeLogin?: boolean
  register?: boolean
  passwordRecovery?: boolean
  identifiers?: Partial<LoginFeatures['identifiers']>
  codeChannels?: Partial<LoginFeatures['codeChannels']>
  captchaOnLogin?: boolean
  captchaOnRegister?: boolean
  captchaOnPasswordRecovery?: boolean
  captcha?: CaptchaClientConfigDto | null
}

/**
 * A third-party sign-in provider rendered by `PwdLogin` as a round icon
 * button under an "Or continue with" divider. Purely consumer-driven -
 * the shell ships no OAuth flow; `onClick` typically starts a redirect.
 */
export interface LoginThirdPartyProvider {
  /** Stable key - also the data-key attribute. */
  key: string
  /** Iconify icon name (e.g. `'mdi:wechat'`, `'mdi:github'`). */
  icon: string
  /** Accessible label / tooltip. Falls back to `key`. */
  label?: string
  /** Brand color for the icon (e.g. `'#07C160'` for WeChat). */
  color?: string
  /** Start the provider's sign-in flow (usually an OAuth redirect). */
  onClick: () => void | Promise<void>
}

/**
 * A demo account quick-fill button rendered under the password form. The
 * label is shown on the button, and clicking pre-fills the username +
 * password (and Phase I.7.2+ may also auto-submit, matching soybean's
 * `handleAccountLogin`).
 */
export interface LoginDemoAccount {
  /** Stable react key - also used as the data-key attribute. */
  key: string
  /** Display label on the button (e.g. "Admin", "Super", "User"). */
  label: string
  userName: string
  password: string
}

export interface LoginContext {
  /** Translate function - falls back to `fallback ?? key` when missing. */
  translate: (key: string, fallback?: string) => string
  /**
   * Switch to a sibling module. `pages/login/LoginView.vue` wires this to
   * `router.replace({ name: 'login', params: { module: name } })` so the URL
   * stays canonical for refreshes / direct links and follows any basePath /
   * deployment prefix (never a hardcoded '/login/...' path).
   */
  toggleLoginModule: (name: LoginModule) => void
  /** Consumer-supplied auth callbacks (see {@link LoginCallbacks}). */
  callbacks: LoginCallbacks
  /**
   * Demo account quick-fill buttons for the pwd-login module. Empty array
   * hides the demo section (matches soybean's behaviour when the constants
   * file ships no accounts).
   */
  demoAccounts: LoginDemoAccount[]
  /**
   * Layout-derived form chrome hints (labels / pill buttons). See
   * {@link LoginUiStyle}. Provided by `TLoginPage` from its `layout` prop.
   */
  ui: LoginUiStyle
  /**
   * Third-party sign-in providers rendered by `PwdLogin`. Empty array hides
   * the "Or continue with" section.
   */
  thirdParty: LoginThirdPartyProvider[]
  /**
   * Feature flags driving module/entry visibility + account field rules.
   * Defaults to {@link DEFAULT_LOGIN_FEATURES} (everything on).
   */
  features: LoginFeatures
  /**
   * Live form-interaction signals (see {@link LoginSceneState}). Reactive -
   * modules assign fields directly; decorative shell elements react.
   */
  scene: LoginSceneState
  /**
   * Outstanding 2FA challenge. `null` when no challenge is active.
   * Populated by `pwdLogin` / `codeLogin` callbacks via
   * `helpers.setTwoFactorRequired(...)`.
   */
  pendingTwoFactor: Ref<TwoFactorChallenge | null>
  pendingAction: Ref<PendingActionChallenge | null>
  /**
   * Outstanding adaptive-login captcha challenge. `null` when none is active.
   * Populated by the `pwdLogin` callback via `helpers.setCaptchaRequired(...)`
   * when the backend replies `IDENTITY_CAPTCHA_REQUIRED`; `PwdLogin` reads it to
   * reveal + seed the captcha field.
   */
  pendingCaptcha: Ref<LoginCaptchaData | null>
  /**
   * Helpers PwdLogin / CodeLogin pass into their callbacks. Defined on
   * the context so test mounts (no callback) and the real shell share
   * a single implementation.
   */
  helpers: LoginCallbackHelpers
  /**
   * Resolves an API-relative path against the API base (the `HttpClient`'s
   * `resolveUrl`). The modules hand it to `<TCaptcha>` for providers whose
   * widget fetches its own challenge (Altcha): `features.captcha.challengeUrl`
   * is relative to the API root, and a widget left to resolve it against the
   * page fetches `/admin/captcha/...`, receives the SPA's `index.html`, and
   * every user is locked out from the moment the captcha is demanded. The
   * shell fills this from the app's injected client; a hand-mounted shell
   * passes it explicitly. Absent → `<TCaptcha>` reports the missing client
   * instead of guessing.
   */
  resolveUrl?: (url: string) => string
}

export const LOGIN_CONTEXT_KEY: InjectionKey<LoginContext> = Symbol('tnzi-login-context')

export function provideLoginContext(context: LoginContext): void {
  provide(LOGIN_CONTEXT_KEY, context)
}

/**
 * Consume the login context from inside a login module. When called outside
 * `TLoginPage` (e.g. unit-test mounting a module in isolation), returns a
 * fallback that mirrors the prod shape so the module doesn't crash.
 */
export function useLoginContext(): LoginContext {
  const ctx = inject(LOGIN_CONTEXT_KEY, null)
  if (ctx) return ctx
  const pendingTwoFactor = ref<TwoFactorChallenge | null>(null)
  const pendingAction = ref<PendingActionChallenge | null>(null)
  const pendingCaptcha = ref<LoginCaptchaData | null>(null)
  return {
    translate: (key, fallback) => fallback ?? key,
    toggleLoginModule: () => undefined,
    callbacks: {},
    demoAccounts: [],
    ui: { labeled: false, pill: true },
    thirdParty: [],
    features: DEFAULT_LOGIN_FEATURES,
    scene: reactive({ typing: false, passwordVisible: false, passwordLength: 0 }),
    pendingTwoFactor,
    pendingAction,
    pendingCaptcha,
    helpers: {
      setTwoFactorRequired: (c) => {
        pendingTwoFactor.value = c
      },
      clearTwoFactor: () => {
        pendingTwoFactor.value = null
      },
      setPendingActionRequired: (c) => {
        pendingAction.value = c
      },
      clearPendingAction: () => {
        pendingAction.value = null
      },
      setCaptchaRequired: (c) => {
        pendingCaptcha.value = c
      },
      clearCaptcha: () => {
        pendingCaptcha.value = null
      },
    },
  }
}
