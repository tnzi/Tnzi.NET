/**
 * The step-up prompt: the interactive half of `withStepUp`.
 *
 * `withStepUp(action, verify)` needs a `verify(scope)` that asks the person in
 * front of the screen to prove they are still there - touch a passkey, or type
 * a code - and resolves only once they have. That is a small state machine
 * (which methods to offer, which one is in progress, where the code went, what
 * went wrong) and it is the same in every product, so it lives here once and
 * each UI package renders it. Nothing in this file touches the DOM beyond the
 * WebAuthn ceremony `stepUpWithPasskey` already owns.
 *
 * Usage (a bridge or hook):
 * ```ts
 * const prompt = new StepUpPromptController({ client });
 * await withStepUp(() => api.disableTotp(), (scope) => prompt.verify(scope));
 * // meanwhile a component renders `prompt.open / stage / methods / error`
 * // and calls `prompt.choose(...)`, `prompt.submitCode(...)`, `prompt.cancel()`.
 * ```
 */

import { reactive } from 'vue';
import type { HttpClient } from '../../http/http';
import { useAuthApi, useProfileApi } from './api';
import { codeLengthForMethod, DEFAULT_OTP_CODE_LENGTH, resolveOtpCodeLength } from './code-length';
import { TwoFactorType } from './metadata';
import { isPasskeySupported } from './passkey';
import { sendStepUpCode, stepUpWithCode, stepUpWithPasskey } from './step-up';
import type { StepUpGrantDto, TwoFactorMethodDto } from './types';

/** A way the signed-in user can prove presence for one action. */
export type StepUpMethod = 'passkey' | 'totp' | 'sms' | 'email';

/**
 * Where the prompt is.
 *
 * - `idle`: closed.
 * - `loading`: open, still discovering which methods to offer.
 * - `choose`: waiting for the user to pick a method.
 * - `code`: a code method was picked; waiting for the code.
 * - `busy`: a request (send / verify / passkey ceremony) is in flight.
 */
export type StepUpStage = 'idle' | 'loading' | 'choose' | 'code' | 'busy';

export interface StepUpPromptOptions {
  client: HttpClient;
  /**
   * Which methods to offer for this account. Defaults to
   * {@link discoverStepUpMethods}; override when the app already knows (a
   * product that only allows passkeys for destructive actions, say).
   */
  discoverMethods?: () => Promise<StepUpMethod[]>;
  /**
   * Digits in an SMS / email code on this deployment. Defaults to reading
   * `otpCodeLength` from `GET /auth/config` (anonymous, fetched alongside
   * method discovery); a failed probe keeps the last known value (6 at
   * first). Pass it when the app already holds the config.
   */
  loadOtpCodeLength?: () => Promise<number>;
}

const FALLBACK_MESSAGE = 'Step-up verification failed';
const EMPTY_CODE_MESSAGE = 'Enter the verification code';

/**
 * Which step-up methods this account can complete right now.
 *
 * - `sms` / `email`: the address is confirmed and the channel is on
 *   (`TwoFactorMethodDto.available`) - exactly the precondition the backend's
 *   send-code applies.
 * - `totp`: an authenticator is enrolled (`enabled`). `available` is not enough
 *   here: for TOTP it only says the deployment allows enrolment, and a code
 *   cannot be typed against an app that was never set up.
 * - `passkey`: this browser can run the ceremony AND the account holds at least
 *   one credential. Offering the button on the first condition alone opens the
 *   system dialog only for it to report "no passkeys".
 *
 * Every probe is independent: one failing removes that method, it does not
 * fail the prompt. The order is the order a UI should list them in.
 */
export async function discoverStepUpMethods(client: HttpClient): Promise<StepUpMethod[]> {
  const profileApi = useProfileApi(client);
  const authApi = useAuthApi(client);

  const [status, passkeys] = await Promise.allSettled([
    profileApi.getTwoFactorStatus(),
    isPasskeySupported() ? authApi.getPasskeyCredentials() : Promise.resolve(null),
  ]);

  const methods: StepUpMethod[] = [];

  if (passkeys.status === 'fulfilled') {
    const list = passkeys.value?.succeeded ? passkeys.value.data : null;
    if (Array.isArray(list) && list.length > 0) methods.push('passkey');
  }

  if (status.status === 'fulfilled' && status.value?.succeeded) {
    const list: TwoFactorMethodDto[] = status.value.data?.methods ?? [];
    for (const method of list) {
      if (method.type === TwoFactorType.Totp && method.enabled) methods.push('totp');
      if (method.type === TwoFactorType.Sms && method.available) methods.push('sms');
      if (method.type === TwoFactorType.Email && method.available) methods.push('email');
    }
  }

  return methods;
}

/** The deployment's SMS / email code length from `GET /auth/config` (6 when the field is absent). */
async function fetchOtpCodeLength(client: HttpClient): Promise<number> {
  const res = await useAuthApi(client).getConfig();
  if (!res?.succeeded) throw new Error('auth config unavailable');
  return resolveOtpCodeLength(res.data);
}

/** The two-factor channel a code method maps to. */
function channelOf(method: StepUpMethod): TwoFactorType | null {
  switch (method) {
    case 'totp':
      return TwoFactorType.Totp;
    case 'sms':
      return TwoFactorType.Sms;
    case 'email':
      return TwoFactorType.Email;
    default:
      return null;
  }
}

/**
 * Reactive step-up prompt state + actions. Construct once per screen (or once
 * per app) and hand `(scope) => prompt.verify(scope)` to `withStepUp`.
 *
 * Built on `vue`'s `reactive` like the state managers, so a component can bind
 * to the fields directly.
 */
export class StepUpPromptController {
  /** Whether a prompt is showing. */
  open = false;
  /** The scope the server asked for; shown so the user knows what they confirm. */
  scope: string | null = null;
  stage: StepUpStage = 'idle';
  /** Methods this account can use, in display order. Empty = nothing to offer. */
  methods: StepUpMethod[] = [];
  /** The code method in progress (`stage === 'code'`). */
  method: StepUpMethod | null = null;
  /** Masked address a code was sent to (`sms` / `email`), for the prompt copy. */
  sentTo: string | null = null;
  /** The last failure (server message), cleared on the next action. */
  error: string | null = null;
  /**
   * Digits in an SMS / email code on this deployment (`GET /auth/config` ->
   * `otpCodeLength`). Refreshed every time a prompt opens, because it is a
   * runtime setting; kept when a refresh fails.
   */
  otpCodeLength = DEFAULT_OTP_CODE_LENGTH;

  private _resolve: ((grant: StepUpGrantDto | null) => void) | null = null;
  /** Serialises overlapping `verify()` calls: one prompt at a time. */
  private _queue: Promise<unknown> = Promise.resolve();
  /** Prompts opened or queued and not yet settled. */
  private _waiting = 0;
  /**
   * Bumped every time a prompt opens or closes. A request that was in flight
   * when the prompt was cancelled (or that belongs to a previous prompt) must
   * not write into the one that is open now.
   */
  private _generation = 0;

  constructor(private readonly options: StepUpPromptOptions) {
    return reactive(this) as this;
  }

  /** True while a request or ceremony is in flight. */
  get busy(): boolean {
    return this.stage === 'busy' || this.stage === 'loading';
  }

  /**
   * Digits the code entry should take for the method in progress: 6 for the
   * authenticator app (fixed by the standard), `otpCodeLength` for a code
   * that was sent. Size the input from this, never from a constant.
   */
  get codeLength(): number {
    return codeLengthForMethod(this.method, this.otpCodeLength);
  }

  /** Whether the account has any way to complete the prompt. */
  get canVerify(): boolean {
    return this.methods.length > 0;
  }

  /**
   * Open the prompt for `scope` and resolve with the grant once the user has
   * verified, or `null` once they cancel. This is the `verify` callback
   * `withStepUp` expects.
   *
   * A second call while a prompt is open waits for the first to settle rather
   * than replacing it - two actions challenged back to back must each get
   * their own answer.
   */
  verify(scope: string): Promise<StepUpGrantDto | null> {
    const run = () =>
      new Promise<StepUpGrantDto | null>((resolve) => {
        this._generation += 1;
        this._resolve = resolve;
        this.open = true;
        this.scope = scope;
        this.stage = 'loading';
        this.methods = [];
        this.method = null;
        this.sentTo = null;
        this.error = null;
        void this._discover(this._generation);
      });
    // Open synchronously when idle so a component sees the prompt in the same
    // tick the action was challenged; only overlapping calls wait. `_waiting`
    // (not `open`) decides, so a call arriving between one prompt closing and
    // the next queued one starting still takes its place in the line.
    const idle = !this.open && this._waiting === 0;
    this._waiting += 1;
    const next = idle ? run() : this._queue.then(run, run);
    this._queue = next.then(
      () => undefined,
      () => undefined,
    );
    void next.finally(() => {
      this._waiting -= 1;
    });
    return next;
  }

  /**
   * Pick a method. Passkey runs the ceremony immediately; SMS / email send a
   * code first; TOTP goes straight to the code entry (the app generates it).
   */
  async choose(method: StepUpMethod): Promise<void> {
    if (!this.open || this.busy || this.scope === null) return;
    this.error = null;

    if (method === 'passkey') {
      await this._verifyWithPasskey();
      return;
    }

    if (method === 'totp') {
      this.method = method;
      this.sentTo = null;
      this.stage = 'code';
      return;
    }

    await this._sendCode(method);
  }

  /** Send the code again on the current channel (`stage === 'code'`). */
  async resendCode(): Promise<void> {
    if (!this.open || this.busy) return;
    if (this.method !== 'sms' && this.method !== 'email') return;
    this.error = null;
    await this._sendCode(this.method);
  }

  /** Verify the typed code for the current method and scope. */
  async submitCode(code: string): Promise<void> {
    if (!this.open || this.busy || this.method === null || this.scope === null) return;
    const channel = channelOf(this.method);
    if (channel === null) return;

    const trimmed = code.trim();
    if (!trimmed) {
      this.error = EMPTY_CODE_MESSAGE;
      return;
    }

    this.error = null;
    this.stage = 'busy';
    const generation = this._generation;
    try {
      const grant = await stepUpWithCode(this.options.client, this.scope, trimmed, channel);
      if (this._isStale(generation)) return;
      this._finish(grant);
    } catch (error) {
      if (this._isStale(generation)) return;
      this.error = messageOf(error);
      this.stage = 'code';
    }
  }

  /** Leave the code entry and pick another method. */
  back(): void {
    if (!this.open || this.busy) return;
    this.method = null;
    this.sentTo = null;
    this.error = null;
    this.stage = 'choose';
  }

  /** Close without verifying. `verify()` resolves `null`. */
  cancel(): void {
    if (!this.open) return;
    this._finish(null);
  }

  /** True when the prompt this request belonged to has since closed. */
  private _isStale(generation: number): boolean {
    return generation !== this._generation || !this.open;
  }

  private async _discover(generation: number): Promise<void> {
    const discover = this.options.discoverMethods ?? (() => discoverStepUpMethods(this.options.client));
    const loadLength = this.options.loadOtpCodeLength ?? (() => fetchOtpCodeLength(this.options.client));
    const [discovered, length] = await Promise.allSettled([discover(), loadLength()]);
    // The user may have cancelled (and a new prompt opened) while discovery
    // was in flight; a stale result must not land in the new one.
    if (this._isStale(generation) || this.stage !== 'loading') return;
    // A failed length probe keeps the last known value: the prompt must still
    // open, and the code is verified by the server either way.
    if (length.status === 'fulfilled') this.otpCodeLength = resolveOtpCodeLength(length.value);
    this.methods = discovered.status === 'fulfilled' ? discovered.value : [];
    this.stage = 'choose';
  }

  private async _sendCode(method: 'sms' | 'email'): Promise<void> {
    const channel = channelOf(method)!;
    this.stage = 'busy';
    const generation = this._generation;
    try {
      const masked = await sendStepUpCode(this.options.client, channel);
      if (this._isStale(generation)) return;
      this.method = method;
      this.sentTo = masked;
      this.stage = 'code';
    } catch (error) {
      if (this._isStale(generation)) return;
      this.error = messageOf(error);
      // A failed send from the chooser stays on the chooser; from a resend,
      // stay on the code entry (the previous code may still be valid).
      this.stage = this.method === method ? 'code' : 'choose';
    }
  }

  private async _verifyWithPasskey(): Promise<void> {
    this.stage = 'busy';
    const generation = this._generation;
    try {
      const grant = await stepUpWithPasskey(this.options.client, this.scope!);
      if (this._isStale(generation)) return;
      if (grant) {
        this._finish(grant);
        return;
      }
      // Dismissed the system dialog: a normal outcome, back to the chooser.
      this.stage = 'choose';
    } catch (error) {
      if (this._isStale(generation)) return;
      this.error = messageOf(error);
      this.stage = 'choose';
    }
  }

  private _finish(grant: StepUpGrantDto | null): void {
    this._generation += 1;
    const resolve = this._resolve;
    this._resolve = null;
    this.open = false;
    this.scope = null;
    this.stage = 'idle';
    this.methods = [];
    this.method = null;
    this.sentTo = null;
    this.error = null;
    resolve?.(grant);
  }
}

function messageOf(error: unknown): string {
  if (error instanceof Error && error.message) return error.message;
  const message = (error as { message?: unknown } | null)?.message;
  return typeof message === 'string' && message ? message : FALLBACK_MESSAGE;
}
