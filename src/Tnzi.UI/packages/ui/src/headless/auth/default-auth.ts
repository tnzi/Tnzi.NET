/**
 * Default auth orchestration for `defineAdminApp({ runtime })`.
 *
 * Every admin consumer used to hand-write the SAME login callbacks in its
 * `main.ts`: map the login page's `{ account, type }` to the backend's split
 * `email` / `phoneNumber` / `TwoFactorType` fields, then call the framework's
 * own `authApi` + `AuthStateManager`. That is pure framework glue - both the
 * login-page contract and the backend DTOs are framework-owned, so the
 * consumer had no real choice to make. This module generates the whole set
 * from a wired {@link TnziClient} runtime; the consumer only overrides a
 * callback when it genuinely diverges from the standard flow.
 */

import { TwoFactorType, runTwoFactorPasskeyCeremony } from '@tnzi/core/services/identity'
import type { PendingActionResultDto } from '@tnzi/core/services/identity'
import type { TnziClient } from '@tnzi/core/state'
import type {
  LoginCallbackHelpers,
  LoginCallbacks,
  LoginCaptchaData,
  PendingActionOutcome,
  TwoFactorMethodName,
} from './useLoginContext'

/**
 * The wired core runtime the framework drives the default auth flow from. This
 * is exactly the object `createTnziClient()` returns - pass it straight to
 * `defineAdminApp({ runtime })`.
 */
export type AdminAuthRuntime = TnziClient

/**
 * Map the login module's auto-detected account type (email / phone) to the
 * backend's split `email` / `phoneNumber` fields + the `TwoFactorType` channel.
 * The login page emits `type: 'email' | 'phone'`; the backend code/recovery
 * DTOs split it into separate email / phoneNumber fields plus a channel
 * discriminator. Both ends are framework contracts - hence framework-owned.
 */
export function codeChannelFields(account: string, type?: 'phone' | 'email') {
  const isEmail = type === 'email'
  return {
    email: isEmail ? account : null,
    phoneNumber: isEmail ? null : account,
    type: isEmail ? TwoFactorType.Email : TwoFactorType.Sms,
  }
}

/**
 * Build the standard login callbacks from a wired runtime. Each callback just
 * calls the framework's own `auth` state manager / `authApi` with the
 * framework's own DTOs - the exact wiring consumers copied by hand. Which of
 * these the login page actually surfaces is decided independently by the
 * backend `GET /auth/config` feature gating, so shipping all five here is
 * safe: unused ones are simply never invoked.
 */
/**
 * Map a wire `TwoFactorType` → challenge method.
 *
 * The double comparison this used to carry (`v === TwoFactorType.Email ||
 * v === 'Email'`) existed because core declared these as NUMERIC enums while
 * the backend serialises them as PascalCase strings, so the enum comparison was
 * always false and only the literal ever matched. Core's enum is a string enum
 * now, matching the wire, so one comparison is enough.
 */
/**
 * Read the captcha challenge out of an `IDENTITY_CAPTCHA_REQUIRED` envelope.
 * Returns null when the details carry nothing renderable (older backend).
 */
export function readCaptchaChallenge(details: unknown): LoginCaptchaData | null {
  const c = (details ?? {}) as {
    provider?: string | null
    captchaId?: string | null
    imageBase64?: string | null
    expirationSeconds?: number | null
  }
  if (c.provider) {
    return { provider: c.provider, captchaId: c.captchaId, imageBase64: c.imageBase64, expirationSeconds: c.expirationSeconds }
  }
  // Pre-provider backends: an inline picture without a provider name is the image captcha.
  if (c.captchaId && c.imageBase64) {
    return { provider: 'image', captchaId: c.captchaId, imageBase64: c.imageBase64, expirationSeconds: c.expirationSeconds }
  }
  return null
}

function twoFactorMethod(v: unknown): TwoFactorMethodName {
  if (v === TwoFactorType.Email) return 'email'
  if (v === TwoFactorType.Sms) return 'sms'
  if (v === TwoFactorType.Passkey) return 'passkey'
  return 'totp'
}
/** Normalise a wire `TwoFactorType` value, defaulting to TOTP. */
function twoFactorType(v: unknown): TwoFactorType {
  if (v === TwoFactorType.Email) return TwoFactorType.Email
  if (v === TwoFactorType.Sms) return TwoFactorType.Sms
  if (v === TwoFactorType.Passkey) return TwoFactorType.Passkey
  return TwoFactorType.Totp
}
/** Map a challenge `method` string → the wire enum (null when absent). */
function typeFromMethod(m?: TwoFactorMethodName): TwoFactorType | null {
  if (m === 'email') return TwoFactorType.Email
  if (m === 'sms') return TwoFactorType.Sms
  if (m === 'totp') return TwoFactorType.Totp
  if (m === 'passkey') return TwoFactorType.Passkey
  return null
}
/** Code methods deliver something; TOTP and passkey have nothing to send. */
function isCodeDelivered(t: TwoFactorType): boolean {
  return t === TwoFactorType.Sms || t === TwoFactorType.Email
}

export function buildDefaultLoginCallbacks(runtime: AdminAuthRuntime): LoginCallbacks {
  const { auth, authApi } = runtime

  // Remembers the in-flight 2FA challenge between the password step and the
  // code-verify step (the verify/resend payloads carry only the code + a
  // challengeId, not the method/type the backend needs).
  // userName 一并记下：2FA 之后若还欠着待办，那个挑战的表单要显示「正在为谁改密」，
  // 而到那一步时原始的登录输入早已不在作用域里。
  let pendingTwoFactor: { tempToken: string; type: TwoFactorType; userName: string } | null = null
  // The account a pending-action challenge was issued for. Discharging an action
  // can itself be answered with a two-factor challenge, and that challenge needs
  // the account name the shell shows; the completion payloads only carry tokens.
  let pendingActionAccount = ''

  async function establishSession(data: {
    accessToken?: string | null
    refreshToken?: string | null
    expiresIn?: number | null
  }): Promise<void> {
    if (!data.accessToken) throw new Error('Login did not return an access token')
    await auth.applyTokenSession({
      accessToken: data.accessToken,
      refreshToken: data.refreshToken ?? undefined,
      expiresIn: data.expiresIn ?? undefined,
    })
  }

  /**
   * Turn a 403 `2FA_REQUIRED` envelope into a challenge the login shell can
   * render, and report whether that happened.
   *
   * Shared by password login and code login: **both** can be challenged now.
   * Code login used to sign people in without ever asking for the second
   * factor, so an account protected by TOTP could be entered with nothing but
   * access to its mailbox. Keeping one copy of this matters for the same
   * reason - the envelope will grow a field one day, and a hand-copied second
   * reader would quietly stop understanding it.
   */
  async function offerTwoFactorChallenge(
    res: { succeeded?: boolean; errorCode?: string | null; errorDetails?: unknown },
    account: string,
    helpers: LoginCallbackHelpers,
  ): Promise<boolean> {
    if (res.succeeded || res.errorCode !== '2FA_REQUIRED') return false

    const details = (res.errorDetails ?? {}) as { tempToken?: string; supportedTypes?: unknown[] }
    const tempToken = details.tempToken ?? ''
    const types = (details.supportedTypes ?? []).map(twoFactorType)
    const first = types[0] ?? TwoFactorType.Totp
    pendingTwoFactor = { tempToken, type: first, userName: account }
    // All enabled methods → the challenge module renders a switcher when >1.
    const methods = [...new Set(types.map(twoFactorMethod))]
    // SMS / email require a code to be delivered; TOTP is read from the app and
    // a passkey is a ceremony. Capture the masked destination so the challenge
    // prompt can show it.
    let maskedAddress: string | undefined
    let codeSendError: string | undefined
    if (isCodeDelivered(first)) {
      // A failed delivery must reach the challenge: the password was right and
      // the challenge is real, but telling the user "a code has been sent" when
      // none was leaves them waiting for a message that never comes. The
      // challenge still opens (another method or a resend can complete it).
      try {
        const sent = await authApi.sendTwoFactorCode({ tempToken, type: first })
        if (sent.succeeded) {
          maskedAddress = sent.data?.maskedAddress ?? undefined
        } else {
          codeSendError = sent.message || 'Failed to send the verification code'
        }
      } catch (err) {
        codeSendError = err instanceof Error && err.message ? err.message : 'Failed to send the verification code'
      }
    }
    helpers.setTwoFactorRequired({
      challengeId: tempToken,
      userName: account,
      method: twoFactorMethod(first),
      methods,
      maskedAddress,
      ...(codeSendError ? { codeSendError } : {}),
    })
    return true
  }

  /**
   * Turn a 403 `IDENTITY_PENDING_ACTIONS_REQUIRED` envelope into a challenge the
   * login shell can render, and report whether that happened.
   *
   * ★ Without this the backend's challenge surfaces as a plain "login failed"
   * and the user is stuck holding a password that is actually correct. Same
   * shape and same reason as `offerTwoFactorChallenge`: one reader, shared by
   * every login flow, because a hand-copied second one stops understanding the
   * envelope the day it grows a field.
   */
  function offerPendingActionChallenge(
    res: { succeeded?: boolean; errorCode?: string | null; errorDetails?: unknown },
    account: string,
    helpers: LoginCallbackHelpers,
  ): boolean {
    if (res.succeeded || res.errorCode !== 'IDENTITY_PENDING_ACTIONS_REQUIRED') return false

    const details = (res.errorDetails ?? {}) as { tempToken?: string; requiredActions?: unknown }
    const actions = Array.isArray(details.requiredActions)
      ? details.requiredActions.map((a) => String(a))
      : []

    // No temp token means the challenge cannot be discharged - fall through to
    // the normal error path rather than parking the user on a form that will
    // always be rejected.
    if (!details.tempToken) return false

    pendingActionAccount = account
    helpers.setPendingActionRequired({
      tempToken: details.tempToken,
      userName: account,
      requiredActions: actions,
    })
    return true
  }

  /**
   * One reader for every pending-action response.
   *
   * Keeping it in one place matters for the same reason the challenge reader
   * does: the interesting case is `completed: false`, where there are no tokens
   * and the caller must NOT treat the absence of an access token as a failure.
   * A hand-copied second reader gets that backwards and tells the user their
   * correct code was rejected.
   */
  async function settlePendingAction(
    call: () => Promise<{
      succeeded?: boolean
      message?: string | null
      errorCode?: string | null
      errorDetails?: unknown
      data?: PendingActionResultDto | null
    }>,
    helpers?: LoginCallbackHelpers,
  ): Promise<PendingActionOutcome> {
    const res = await call()
    // Discharging the last action issues a session, and issuing still runs the
    // guard chain: an account with another second factor enabled gets a 403
    // `2FA_REQUIRED` with a fresh temp token here. That is not a failure - the
    // action is done (password changed, this pending token consumed) and a
    // retry on the same page can only fail. Hand the challenge to the shell
    // when we can; without helpers the message is all we have, as before.
    if (helpers && (await offerTwoFactorChallenge(res, pendingActionAccount, helpers))) {
      return { completed: false, remainingActions: [], challenged: true }
    }
    if (!res.succeeded || !res.data) {
      throw new Error(res.message ?? 'Could not complete the required action')
    }

    const { completed, remainingActions, token } = res.data
    if (completed) {
      if (!token?.accessToken) throw new Error('Completing the action did not return an access token')
      await establishSession(token)
    }

    return { completed, remainingActions: remainingActions ?? [] }
  }

  return {
    // userName accepts username / email / phone - the backend resolves the
    // identifier (AuthService.FindUserByLoginInputAsync). On a 2FA-enabled
    // account the backend replies 403 `2FA_REQUIRED` with a temp token +
    // enabled method types; we hand that to the login shell (which switches to
    // the `two-factor` module) instead of failing.
    pwdLogin: async ({ userName, password, captchaToken, captchaId, captchaCode }, helpers) => {
      const res = await authApi.loginWithRefreshToken({ userName, password, captchaToken, captchaId, captchaCode })
      // Adaptive login captcha: after repeated failures the backend replies
      // `IDENTITY_CAPTCHA_REQUIRED` with the challenge in `errorDetails` - a fresh
      // picture for the `image` provider, just the provider name for the others.
      // Reveal the captcha field seeded with it (PwdLogin watches `pendingCaptcha`).
      if (!res.succeeded && res.errorCode === 'IDENTITY_CAPTCHA_REQUIRED') {
        const challenge = readCaptchaChallenge(res.errorDetails)
        if (challenge) {
          helpers.setCaptchaRequired(challenge)
          return
        }
        // No usable challenge (older backend) → surface the message.
        throw new Error(res.message ?? 'Captcha verification is required')
      }
      // A second-factor or pending-action challenge means the password (and any
      // captcha sent with it) was accepted: the captcha is spent and must not
      // stay on screen, where the form would read it as rejected.
      if (await offerTwoFactorChallenge(res, userName, helpers)) {
        helpers.clearCaptcha()
        return
      }
      if (offerPendingActionChallenge(res, userName, helpers)) {
        helpers.clearCaptcha()
        return
      }
      if (!res.succeeded || !res.data?.accessToken) {
        throw new Error(res.message ?? 'Login failed')
      }
      helpers.clearCaptcha()
      await establishSession(res.data)
    },
    // Fetch a fresh built-in image captcha for the given purpose. Used by the
    // always-shown fields up-front and by the refresh button; script providers
    // never call it.
    getCaptcha: async (purpose) => {
      const res = await authApi.getCaptchaJson(purpose)
      if (!res.succeeded || !res.data) throw new Error(res.message ?? 'Failed to load captcha')
      return {
        provider: res.data.provider ?? 'image',
        captchaId: res.data.captchaId,
        imageBase64: res.data.imageBase64,
        expirationSeconds: res.data.expirationSeconds,
      }
    },
    // Send a verification code for code-login / password-recovery / register.
    // All three carry the captcha token: each endpoint spends a real SMS / email
    // per call, and the backend gates them on `EnableCaptchaOnLogin` /
    // `EnableCaptchaOnPasswordRecovery` / `EnableCaptchaOnRegister`.
    sendCode: async ({ account, type, purpose, captchaToken, captchaId, captchaCode }) => {
      const f = codeChannelFields(account, type)
      const res =
        purpose === 'code-login'
          ? await authApi.sendCodeLoginCode({ ...f, captchaToken, captchaId, captchaCode })
          : purpose === 'reset-pwd'
            ? await authApi.sendPasswordRecoveryCode({ ...f, captchaToken, captchaId, captchaCode })
            : await authApi.sendQuickRegisterCode({
                email: f.email,
                phoneNumber: f.phoneNumber,
                captchaToken,
                captchaId,
                captchaCode,
              })
      if (!res.succeeded) throw new Error(res.message ?? 'Failed to send the verification code')
    },
    // Verification-code login → establish a persisted session from the returned
    // tokens, then run the normal post-login flow (framework-wrapped `after()`).
    //
    // ★ This can be challenged too. The code proves the user can receive that
    // address, so the backend drops the matching factor from the challenge -
    // an email-2FA account is not asked for a second email code - but any other
    // enabled method (TOTP, SMS) still comes back here as `2FA_REQUIRED`.
    codeLogin: async ({ account, code, type }, helpers) => {
      const f = codeChannelFields(account, type)
      const res = await authApi.codeLogin({ email: f.email, phoneNumber: f.phoneNumber, code, type: f.type })
      if (await offerTwoFactorChallenge(res, account, helpers)) return
      if (offerPendingActionChallenge(res, account, helpers)) return
      const accessToken = res.data?.accessToken
      if (!res.succeeded || !accessToken) {
        throw new Error(res.message ?? 'Verification code login failed')
      }
      await auth.applyTokenSession({
        accessToken,
        refreshToken: res.data?.refreshToken,
        expiresIn: res.data?.expiresIn,
      })
    },
    // Reset the password via a verification code; the login shell bounces back
    // to the password form on success so the user signs in with the new one.
    resetPwd: async ({ account, code, password, type }) => {
      const f = codeChannelFields(account, type)
      const res = await authApi.resetPasswordByCode({
        email: f.email,
        phoneNumber: f.phoneNumber,
        code,
        newPassword: password,
        type: f.type,
      })
      if (!res.succeeded) throw new Error(res.message ?? 'Password reset failed')
    },
    // Quick register: account + code → passwordless account → set the chosen
    // password. Requires the backend's quick-register flow to be enabled (the
    // register entry only shows when `GET /auth/config` reports it on).
    register: async ({ account, code, password, type }) => {
      const f = codeChannelFields(account, type)
      const qr = await authApi.quickRegister({ email: f.email, phoneNumber: f.phoneNumber, code })
      if (!qr.succeeded || !qr.data) throw new Error(qr.message ?? 'Registration failed')
      if (qr.data.requirePasswordSetup && qr.data.setPasswordToken) {
        const sp = await authApi.setPassword({
          userId: qr.data.userId,
          token: qr.data.setPasswordToken,
          password,
        })
        if (!sp.succeeded) throw new Error(sp.message ?? 'Failed to set the password')
      }
      // The login shell returns to pwd-login on success.
    },
    // Submit the 2FA code from the challenge → verify-2fa → establish the
    // session. The wrapped `after()` (defineAdminApp) then loads permissions
    // and redirects, same as a normal password login.
    verifyTwoFactor: async ({ challengeId, code, method }, helpers) => {
      const tempToken = challengeId ?? pendingTwoFactor?.tempToken ?? ''
      // The user may have switched methods in the UI → honour the payload method.
      const type = typeFromMethod(method) ?? pendingTwoFactor?.type ?? TwoFactorType.Totp
      const res = await authApi.verifyTwoFactor({ tempToken, code, type })
      // ★ This is the path that most needs it: the backend asks for obligations
      // AFTER 2FA, so a forced password change surfaces exactly here. Without
      // handling it the user is told "verification failed" while their code was
      // in fact correct.
      if (helpers && offerPendingActionChallenge(res, pendingTwoFactor?.userName ?? '', helpers)) {
        pendingTwoFactor = null
        return
      }
      if (!res.succeeded || !res.data?.accessToken) {
        throw new Error(res.message ?? 'Verification failed')
      }
      pendingTwoFactor = null
      await establishSession(res.data)
    },
    // The passkey leg of the same challenge: the ceremony runs against the
    // account the temp token names, then the session is established exactly
    // as after a code. A dismissed system dialog resolves false and leaves the
    // challenge open.
    verifyTwoFactorWithPasskey: async ({ challengeId }, helpers) => {
      const tempToken = challengeId ?? pendingTwoFactor?.tempToken ?? ''
      const data = await runTwoFactorPasskeyCeremony(authApi, tempToken).catch((err: unknown) => {
        // The complete leg can answer with an obligation challenge instead of
        // tokens; the helper throws it as an HttpError. Everything else is a
        // real failure.
        // (`HttpError` carries the envelope as `errorCode` + `details`.)
        const e = (err ?? {}) as { errorCode?: string | null; details?: unknown }
        const envelope = { succeeded: false, errorCode: e.errorCode, errorDetails: e.details }
        if (helpers && offerPendingActionChallenge(envelope, pendingTwoFactor?.userName ?? '', helpers)) {
          return undefined
        }
        throw err
      })
      if (data === undefined) {
        pendingTwoFactor = null
        return true
      }
      if (data === null) return false
      pendingTwoFactor = null
      await establishSession(data)
      return true
    },
    // Read what is owed, plus the material to discharge it (the TOTP key).
    describePendingActions: async (tempToken) => {
      const res = await authApi.describePendingActions(tempToken)
      return res.succeeded ? (res.data ?? null) : null
    },
    // Discharge one action. When everything is settled the backend answers with
    // tokens directly, so there is no second sign-in - and it still runs the
    // guard chain, so an account disabled in the meantime is rejected here.
    // When something else is still owed there are no tokens and the same temp
    // token stays valid for the next step.
    completePasswordChange: ({ tempToken, newPassword }, helpers) =>
      settlePendingAction(() => authApi.completePendingPasswordChange({ tempToken, newPassword }), helpers),
    completeTotpEnrollment: ({ tempToken, code }, helpers) =>
      settlePendingAction(() => authApi.completePendingTotpEnrollment({ tempToken, code }), helpers),
    sendPendingActionEmailCode: async (tempToken) => {
      const res = await authApi.sendPendingActionEmailCode(tempToken)
      if (!res.succeeded) throw new Error(res.message ?? 'Could not send the code')
    },
    completeEmailConfirmation: ({ tempToken, code }, helpers) =>
      settlePendingAction(() => authApi.completePendingEmailConfirmation({ tempToken, code }), helpers),
    // (Re)deliver the code for SMS / email - also called when the user switches
    // TO an SMS/email method. TOTP has nothing to send. Returns the masked
    // destination so the challenge prompt can show "Code sent to j***@…".
    resendTwoFactor: async ({ challengeId, method }) => {
      const type = typeFromMethod(method) ?? pendingTwoFactor?.type ?? TwoFactorType.Totp
      if (!isCodeDelivered(type)) return
      const tempToken = challengeId ?? pendingTwoFactor?.tempToken ?? ''
      const res = await authApi.sendTwoFactorCode({ tempToken, type })
      if (!res.succeeded) throw new Error(res.message ?? 'Failed to resend the verification code')
      return { maskedAddress: res.data?.maskedAddress }
    },
  }
}
