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

import { TwoFactorType } from '@tnzi/core/services/identity'
import type { TnziClient } from '@tnzi/core/state'
import type { LoginCallbacks } from '../pages/login/useLoginContext'

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
function twoFactorMethod(v: unknown): 'totp' | 'sms' | 'email' {
  if (v === TwoFactorType.Email) return 'email'
  if (v === TwoFactorType.Sms) return 'sms'
  return 'totp'
}
/** Normalise a wire `TwoFactorType` value, defaulting to TOTP. */
function twoFactorType(v: unknown): TwoFactorType {
  if (v === TwoFactorType.Email) return TwoFactorType.Email
  if (v === TwoFactorType.Sms) return TwoFactorType.Sms
  return TwoFactorType.Totp
}
/** Map a challenge `method` string → the wire enum (null when absent). */
function typeFromMethod(m?: 'totp' | 'sms' | 'email'): TwoFactorType | null {
  if (m === 'email') return TwoFactorType.Email
  if (m === 'sms') return TwoFactorType.Sms
  if (m === 'totp') return TwoFactorType.Totp
  return null
}

export function buildDefaultLoginCallbacks(runtime: AdminAuthRuntime): LoginCallbacks {
  const { auth, authApi } = runtime

  // Remembers the in-flight 2FA challenge between the password step and the
  // code-verify step (the verify/resend payloads carry only the code + a
  // challengeId, not the method/type the backend needs).
  let pendingTwoFactor: { tempToken: string; type: TwoFactorType } | null = null

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

  return {
    // userName accepts username / email / phone - the backend resolves the
    // identifier (AuthService.FindUserByLoginInputAsync). On a 2FA-enabled
    // account the backend replies 403 `2FA_REQUIRED` with a temp token +
    // enabled method types; we hand that to the login shell (which switches to
    // the `two-factor` module) instead of failing.
    pwdLogin: async ({ userName, password, captchaId, captchaCode }, helpers) => {
      const res = await authApi.loginWithRefreshToken({ userName, password, captchaId, captchaCode })
      // Adaptive login captcha: after repeated failures the backend replies
      // `IDENTITY_CAPTCHA_REQUIRED` with a fresh picture in `errorDetails`. Reveal
      // the captcha field seeded with it (PwdLogin watches `pendingCaptcha`).
      if (!res.succeeded && res.errorCode === 'IDENTITY_CAPTCHA_REQUIRED') {
        const c = (res.errorDetails ?? {}) as {
          captchaId?: string
          imageBase64?: string
          expirationSeconds?: number
        }
        if (c.captchaId && c.imageBase64) {
          helpers.setCaptchaRequired({
            captchaId: c.captchaId,
            imageBase64: c.imageBase64,
            expirationSeconds: c.expirationSeconds,
          })
          return
        }
        // No inline captcha (cache unavailable / older backend) → surface the message.
        throw new Error(res.message ?? 'Captcha verification is required')
      }
      if (!res.succeeded && res.errorCode === '2FA_REQUIRED') {
        const details = (res.errorDetails ?? {}) as { tempToken?: string; supportedTypes?: unknown[] }
        const tempToken = details.tempToken ?? ''
        const types = (details.supportedTypes ?? []).map(twoFactorType)
        const first = types[0] ?? TwoFactorType.Totp
        pendingTwoFactor = { tempToken, type: first }
        // All enabled methods → the challenge module renders a switcher when >1.
        const methods = [...new Set(types.map(twoFactorMethod))]
        // SMS / email require a code to be delivered; TOTP is read from the app.
        // Capture the masked destination so the challenge prompt can show it.
        let maskedAddress: string | undefined
        if (first !== TwoFactorType.Totp) {
          const sent = await authApi.sendTwoFactorCode({ tempToken, type: first }).catch(() => undefined)
          maskedAddress = sent?.data?.maskedAddress ?? undefined
        }
        helpers.setTwoFactorRequired({
          challengeId: tempToken,
          userName,
          method: twoFactorMethod(first),
          methods,
          maskedAddress,
        })
        return
      }
      if (!res.succeeded || !res.data?.accessToken) {
        throw new Error(res.message ?? 'Login failed')
      }
      helpers.clearCaptcha()
      await establishSession(res.data)
    },
    // Fetch a fresh image captcha for the given flow (login / register). Used by
    // the register form up-front and by the login captcha field's refresh button.
    getCaptcha: async (purpose) => {
      const res = await authApi.getCaptchaJson(purpose)
      if (!res.succeeded || !res.data) throw new Error(res.message ?? 'Failed to load captcha')
      return {
        captchaId: res.data.captchaId,
        imageBase64: res.data.imageBase64,
        expirationSeconds: res.data.expirationSeconds,
      }
    },
    // Send a verification code for code-login / password-recovery / register.
    // The register flow carries the image-captcha (gates the send-code step when
    // the backend enables the register captcha).
    sendCode: async ({ account, type, purpose, captchaId, captchaCode }) => {
      const f = codeChannelFields(account, type)
      const res =
        purpose === 'code-login'
          ? await authApi.sendCodeLoginCode(f)
          : purpose === 'reset-pwd'
            ? await authApi.sendPasswordRecoveryCode(f)
            : await authApi.sendQuickRegisterCode({
                email: f.email,
                phoneNumber: f.phoneNumber,
                captchaId,
                captchaCode,
              })
      if (!res.succeeded) throw new Error(res.message ?? 'Failed to send the verification code')
    },
    // Verification-code login → establish a persisted session from the returned
    // tokens, then run the normal post-login flow (framework-wrapped `after()`).
    codeLogin: async ({ account, code, type }) => {
      const f = codeChannelFields(account, type)
      const res = await authApi.codeLogin({ email: f.email, phoneNumber: f.phoneNumber, code, type: f.type })
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
    verifyTwoFactor: async ({ challengeId, code, method }) => {
      const tempToken = challengeId ?? pendingTwoFactor?.tempToken ?? ''
      // The user may have switched methods in the UI → honour the payload method.
      const type = typeFromMethod(method) ?? pendingTwoFactor?.type ?? TwoFactorType.Totp
      const res = await authApi.verifyTwoFactor({ tempToken, code, type })
      if (!res.succeeded || !res.data?.accessToken) {
        throw new Error(res.message ?? 'Verification failed')
      }
      pendingTwoFactor = null
      await establishSession(res.data)
    },
    // (Re)deliver the code for SMS / email - also called when the user switches
    // TO an SMS/email method. TOTP has nothing to send. Returns the masked
    // destination so the challenge prompt can show "Code sent to j***@…".
    resendTwoFactor: async ({ challengeId, method }) => {
      const type = typeFromMethod(method) ?? pendingTwoFactor?.type ?? TwoFactorType.Totp
      if (type === TwoFactorType.Totp) return
      const tempToken = challengeId ?? pendingTwoFactor?.tempToken ?? ''
      const res = await authApi.sendTwoFactorCode({ tempToken, type })
      if (!res.succeeded) throw new Error(res.message ?? 'Failed to resend the verification code')
      return { maskedAddress: res.data?.maskedAddress }
    },
  }
}
