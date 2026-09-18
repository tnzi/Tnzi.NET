import { describe, it, expect, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { createIdentityBridge } from '../../../src/services/bridges/identity-bridge'

/**
 * Step-up on the self-service `me.*` writes.
 *
 * With `Identity:StepUp:Enabled=true` the backend answers ten
 * `/users/profile/*` endpoints with `IDENTITY_STEP_UP_REQUIRED` + a scope.
 * `ensureOk` turns that into a thrown `HttpError`, and until 2026-09-12 every
 * account section caught it and toasted "This action requires
 * re-authentication" - a handled error with no way forward, identical on every
 * click. The loop is closed here, once, in the bridge: when the caller wires a
 * `stepUp` verifier, each of those calls runs through core's `withStepUp`
 * (challenge -> verify -> replay once). Pages stay untouched.
 */

const SCOPE = 'identity.two-factor.manage'

function challengeEnvelope(scope = SCOPE) {
  return {
    succeeded: false,
    success: false,
    code: 401,
    data: null,
    message: 'This action requires re-authentication',
    errorCode: 'IDENTITY_STEP_UP_REQUIRED',
    errorDetails: { scope },
  }
}

const okEnvelope = { succeeded: true, success: true, code: 200, data: null }
const GRANT = { scope: SCOPE, expiresAt: '2030-01-01T00:00:00Z', singleUse: false }

function minimalApis() {
  const paged = async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20, totalPages: 0, hasPreviousPage: false, hasNextPage: false })
  return {
    userApi: { getList: paged } as never,
    roleApi: { getPagedList: paged } as never,
    tenantApi: { getPagedList: paged } as never,
    loginLogApi: { getList: paged } as never,
  }
}

/** A profile api whose first call to `method` is challenged and whose second succeeds. */
function challengedOnce(method: string, scope = SCOPE) {
  let calls = 0
  const fn = vi.fn(async () => {
    calls += 1
    return calls === 1 ? challengeEnvelope(scope) : okEnvelope
  })
  return { [method]: fn } as Record<string, ReturnType<typeof vi.fn>>
}

describe('identity-bridge step-up loop', () => {
  it('★ challenge -> verify(scope) -> replay once, then resolves', async () => {
    const profileApi = challengedOnce('disableTotp')
    const stepUp = vi.fn(async () => GRANT)
    const bridge = createIdentityBridge({ ...minimalApis(), profileApi: profileApi as never, stepUp })

    await expect(bridge.me.disableTotp()).resolves.toBeUndefined()

    expect(stepUp).toHaveBeenCalledTimes(1)
    expect(stepUp).toHaveBeenCalledWith(SCOPE)
    expect(profileApi.disableTotp).toHaveBeenCalledTimes(2)
  })

  it('cancelling the prompt rethrows the original challenge and does not replay', async () => {
    const profileApi = challengedOnce('deleteAccount', 'identity.account.destroy')
    const stepUp = vi.fn(async () => null)
    const bridge = createIdentityBridge({ ...minimalApis(), profileApi: profileApi as never, stepUp })

    await expect(bridge.me.deleteAccount()).rejects.toMatchObject({
      errorCode: 'IDENTITY_STEP_UP_REQUIRED',
    })
    expect(stepUp).toHaveBeenCalledWith('identity.account.destroy')
    expect(profileApi.deleteAccount).toHaveBeenCalledTimes(1)
  })

  it('a non-challenge failure never opens the prompt', async () => {
    const profileApi = {
      suspendTwoFactor: vi.fn(async () => ({
        succeeded: false, success: false, code: 400, data: null, message: 'Two-factor is not enabled',
      })),
    }
    const stepUp = vi.fn(async () => GRANT)
    const bridge = createIdentityBridge({ ...minimalApis(), profileApi: profileApi as never, stepUp })

    await expect(bridge.me.suspendTwoFactor()).rejects.toThrow('Two-factor is not enabled')
    expect(stepUp).not.toHaveBeenCalled()
    expect(profileApi.suspendTwoFactor).toHaveBeenCalledTimes(1)
  })

  it('without a verifier the challenge surfaces as before (no silent retry, no crash)', async () => {
    const profileApi = challengedOnce('disableTwoFactor')
    const bridge = createIdentityBridge({ ...minimalApis(), profileApi: profileApi as never })

    await expect(bridge.me.disableTwoFactor()).rejects.toThrow('This action requires re-authentication')
    expect(profileApi.disableTwoFactor).toHaveBeenCalledTimes(1)
  })

  it('a value-returning write (getTotpSetup) replays and hands back the payload', async () => {
    let calls = 0
    const profileApi = {
      getTotpSetup: vi.fn(async () => {
        calls += 1
        return calls === 1
          ? challengeEnvelope()
          : { succeeded: true, success: true, code: 200, data: { sharedKey: 'K', authenticatorUri: 'otpauth://x' } }
      }),
    }
    const stepUp = vi.fn(async () => GRANT)
    const bridge = createIdentityBridge({ ...minimalApis(), profileApi: profileApi as never, stepUp })

    expect(await bridge.me.getTotpSetup()).toEqual({ sharedKey: 'K', authenticatorUri: 'otpauth://x' })
    expect(stepUp).toHaveBeenCalledTimes(1)
  })

  it('confirmChangeEmail replays with the same payload, so the code just typed is not wasted', async () => {
    const profileApi = challengedOnce('confirmChangeEmail', 'identity.contact.change')
    const stepUp = vi.fn(async () => GRANT)
    const bridge = createIdentityBridge({ ...minimalApis(), profileApi: profileApi as never, stepUp })
    const payload = { newEmail: 'new@example.com', code: '123456' }

    await bridge.me.confirmChangeEmail(payload)

    expect(profileApi.confirmChangeEmail).toHaveBeenNthCalledWith(1, payload)
    expect(profileApi.confirmChangeEmail).toHaveBeenNthCalledWith(2, payload)
  })
})

/**
 * Convention gate: every `me.*` method that maps to a `[RequireStepUp]`
 * endpoint on `DefaultUserProfileController` must route through the guard.
 * A new gated endpoint added to the backend without its bridge method being
 * listed here is the exact regression this claim was about, so the list is
 * explicit rather than derived.
 */
describe('identity-bridge step-up coverage (source scan)', () => {
  const source = readFileSync(
    resolve(__dirname, '../../../src/services/bridges/identity-bridge.ts'),
    'utf8',
  )

  const GATED_ME_METHODS = [
    'disableTwoFactor', // POST two-factor/disable
    'suspendTwoFactor', // POST two-factor/suspend
    'getTotpSetup', // POST two-factor/totp/setup
    'enableTotp', // POST two-factor/totp/enable
    'disableTotp', // POST two-factor/totp/disable
    'disableTwoFactorMethod', // POST two-factor/method/disable
    'deactivate', // POST deactivate
    'deleteAccount', // DELETE account
    'confirmChangeEmail', // POST change-email/confirm
    'confirmChangePhone', // POST change-phone/confirm
    'issueOAuthLinkToken', // POST linked-accounts/{provider}/link-token
  ]

  function isGuarded(text: string, method: string): boolean {
    // `method: async (...) =>` followed by `stepUpGuarded(` before the next
    // property definition of the same shape.
    const re = new RegExp(
      `\\b${method}:\\s*(?:async\\s*)?\\([^)]*\\)\\s*=>\\s*(?:\\{\\s*)?(?:return\\s+|await\\s+)?stepUpGuarded\\(`,
    )
    return re.test(text)
  }

  it.each(GATED_ME_METHODS)('me.%s runs through the step-up guard', (method) => {
    expect(isGuarded(source, method), `me.${method} is [RequireStepUp] on the backend but bypasses stepUpGuarded()`).toBe(true)
  })

  it('positive / negative controls on the matcher', () => {
    expect(isGuarded('disableTotp: async () => {\n  await stepUpGuarded(() => x())\n},', 'disableTotp')).toBe(true)
    expect(isGuarded('getTotpSetup: async () => stepUpGuarded(() => x()),', 'getTotpSetup')).toBe(true)
    expect(isGuarded('disableTotp: async () => {\n  ensureOk(await profileApi.disableTotp())\n},', 'disableTotp')).toBe(false)
  })

  it('a plain read is not guarded', () => {
    expect(isGuarded(source, 'getProfile')).toBe(false)
  })
})
