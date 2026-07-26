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

export type LoginModule =
  | 'pwd-login'
  | 'code-login'
  | 'register'
  | 'reset-pwd'
  | 'bind-wechat'
  | 'two-factor'

/**
 * Outstanding 2FA challenge - populated by `pwdLogin` / `codeLogin` callbacks
 * via `helpers.setTwoFactorRequired(challenge)` when the backend returns
 * `requires2FA: true`. The `TwoFactorChallenge` module reads this to know
 * which user / method to verify against.
 */
/** A 2FA delivery channel. */
export type TwoFactorMethodName = 'totp' | 'sms' | 'email'

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
 * A generated image captcha the login page renders (id + base64 PNG).
 *
 * The two captcha flows differ:
 *   - **login** is adaptive - the backend only demands a captcha after repeated
 *     failures (`Identity:Captcha:CaptchaFailThreshold`) and returns a fresh one
 *     in its `IDENTITY_CAPTCHA_REQUIRED` error, which `PwdLogin` renders inline.
 *   - **register** shows one up-front (fetched via `callbacks.getCaptcha`) and
 *     gates the send-code step.
 */
export interface LoginCaptchaData {
  captchaId: string
  imageBase64: string
  expirationSeconds?: number
}

export interface PwdLoginPayload {
  userName: string
  password: string
  remember: boolean
  /** Image-captcha id - set once the adaptive login captcha is revealed. */
  captchaId?: string
  /** Image-captcha code the user typed. */
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
  /** Image-captcha id - sent for `register` when the register captcha is on. */
  captchaId?: string
  /** Image-captcha code the user typed. */
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
   * Reveal the adaptive login captcha with the fresh picture the backend
   * returned in its `IDENTITY_CAPTCHA_REQUIRED` response. `PwdLogin` watches
   * `pendingCaptcha` and shows the captcha field seeded with it.
   */
  setCaptchaRequired: (captcha: LoginCaptchaData) => void
  /** Dismiss the captcha field (e.g. on a successful login). */
  clearCaptcha: () => void
}

/**
 * Auth callbacks supplied by the page consumer (Acme, music, webshop, …).
 *
 * All callbacks are optional - when a module's flow callback is missing the
 * module shows a "not configured" hint. Phase I.7.2+ wires each module to
 * the corresponding callback.
 */
export interface LoginCallbacks {
  pwdLogin?: (payload: PwdLoginPayload, helpers: LoginCallbackHelpers) => Promise<void>
  codeLogin?: (payload: CodeLoginPayload, helpers: LoginCallbackHelpers) => Promise<void>
  register?: (payload: RegisterPayload) => Promise<void>
  resetPwd?: (payload: ResetPwdPayload) => Promise<void>
  sendCode?: (payload: SendCodePayload) => Promise<void>
  /**
   * Submit the 2FA code the user typed after a `pwdLogin` / `codeLogin`
   * returned `requires2FA: true`. Maps to
   * `POST /auth/verify-2fa` (Tnzi.Identity.DefaultAuthController.VerifyTwoFactor).
   */
  verifyTwoFactor?: (payload: VerifyTwoFactorPayload) => Promise<void>
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
   * Fetch a fresh image captcha for the given flow. Wired to
   * `GET /auth/captcha/{purpose}/json`. The captcha field uses it to (re)load
   * the picture; when omitted, the field can still render a captcha the backend
   * pushed inline (login's adaptive flow) but the refresh button is hidden.
   */
  getCaptcha?: (purpose: 'login' | 'register') => Promise<LoginCaptchaData>
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
   * Image captcha is enabled on the password-login flow. When on, the backend
   * demands a captcha adaptively (after repeated failures); `PwdLogin` only
   * reveals the field once the backend asks (`IDENTITY_CAPTCHA_REQUIRED`).
   */
  captchaOnLogin: boolean
  /** Image captcha is enabled on the register flow - shown up-front (gates send-code). */
  captchaOnRegister: boolean
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
   * Switch to a sibling module. `pages/login/index.vue` wires this to
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
    pendingCaptcha,
    helpers: {
      setTwoFactorRequired: (c) => {
        pendingTwoFactor.value = c
      },
      clearTwoFactor: () => {
        pendingTwoFactor.value = null
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
