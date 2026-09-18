/**
 * The signed-in user's own account: profile fields, password, two-factor and
 * active sessions.
 *
 * Every call goes to `/users/profile/*` (`Tnzi.Identity`'s self-service
 * controller), which is **user-facing** - an ordinary signed-in user manages
 * their own account there, no admin permission involved. That is what makes
 * these built-in pages rather than operator screens, and it is why this hook
 * does not touch any `/admin/*` route.
 *
 * `@tnzi/ui-admin` covers the same surface in its User Center; the two share
 * the `useProfileApi` factory from `@tnzi/core` rather than the UI, because the
 * two products present it very differently (an operator console with tables vs
 * a settings pane) while the calls underneath are identical.
 *
 * Reads are fail-safe and writes are not - the same split as `useGlobalAiTheme`
 * and for the same reason: a page that failed to load is a nuisance, a write
 * that silently failed is a lie. Security writes are the sharpest case of it.
 */
import { ref, computed, type Ref, type ComputedRef } from 'vue';
import type { HttpClient } from '@tnzi/core/http';
import {
  useProfileApi,
  withStepUp,
  readSessionIdClaim,
  StepUpPromptController,
} from '@tnzi/core/services/identity';
import type {
  UserDto,
  TwoFactorStatusDto,
  TotpSetupDto,
  UserSessionDto,
  StepUpGrantDto,
} from '@tnzi/core/services/identity';

export interface UseAccountSettingsOptions {
  client?: HttpClient | null;
  /**
   * Step-up verifier for the writes the backend marks `[RequireStepUp]`
   * (two-factor changes, contact-change confirmation). Called with the scope
   * the server asked for; resolve a grant to replay the write once, `null` to
   * give up. Defaults to a built-in `StepUpPromptController` exposed as
   * `stepUp`, which `TStepUpPrompt` renders inline in the settings pages.
   */
  stepUp?: (scope: string) => Promise<StepUpGrantDto | null>;
}

/** Editable profile fields, held apart from server state so the form is free. */
export interface AccountDraft {
  nickname: string;
  email: string;
  phoneNumber: string;
}

export interface UseAccountSettingsReturn {
  readonly profile: Ref<UserDto | null>;
  readonly draft: Ref<AccountDraft>;
  readonly twoFactor: Ref<TwoFactorStatusDto | null>;
  readonly totpSetup: Ref<TotpSetupDto | null>;
  readonly sessions: Ref<readonly UserSessionDto[]>;
  /**
   * The id of the session this tab is signed in with, read off the access
   * token's `session_id` claim; null when it cannot be told (no token, an
   * opaque token, a deployment without session-bound tokens). The list from
   * the backend carries no such marker itself.
   */
  readonly currentSessionId: ComputedRef<string | null>;
  /** `sessions` minus the current one. Everything when the current one is unknown. */
  readonly otherSessions: ComputedRef<readonly UserSessionDto[]>;
  readonly loading: Ref<boolean>;
  readonly busy: Ref<boolean>;
  readonly error: Ref<string | null>;
  readonly available: ComputedRef<boolean>;
  readonly dirty: ComputedRef<boolean>;
  /**
   * The built-in re-authentication prompt (null without a client, or when the
   * caller supplied its own `stepUp` verifier). Render it with `TStepUpPrompt`;
   * a challenged write opens it and waits for the user.
   */
  readonly stepUp: StepUpPromptController | null;

  load: () => Promise<void>;
  loadSessions: () => Promise<void>;
  saveProfile: () => Promise<boolean>;
  resetDraft: () => void;
  changePassword: (currentPassword: string, newPassword: string) => Promise<boolean>;

  /** Fetch a fresh TOTP secret + otpauth URI to show as a QR code. */
  beginTotp: () => Promise<boolean>;
  /** Confirm the 6-digit code and turn TOTP on. */
  confirmTotp: (code: string) => Promise<boolean>;
  disableTotp: () => Promise<boolean>;
  /** Master switch off, keeping the configured methods for `resume`. */
  suspendTwoFactor: () => Promise<boolean>;
  resumeTwoFactor: () => Promise<boolean>;

  /**
   * Whether a listed session is the one this tab is using. False when it
   * cannot be told - a caller must then keep its own safeguards (a
   * confirmation) rather than treat "unknown" as "not mine".
   */
  isCurrentSession: (session: UserSessionDto) => boolean;
  revokeSession: (sessionId: string) => Promise<boolean>;
  /**
   * Signs out every OTHER device; this tab stays signed in.
   * Pass `true` for a true "sign out everywhere", which ends this session too.
   */
  revokeAllSessions: (includeCurrent?: boolean) => Promise<boolean>;

  /** Step 1 of changing the email: send a code to the NEW address. */
  sendEmailChangeCode: (newEmail: string) => Promise<boolean>;
  /** Step 2: confirm with the code that arrived there. */
  confirmEmailChange: (newEmail: string, code: string) => Promise<boolean>;
  sendPhoneChangeCode: (newPhoneNumber: string) => Promise<boolean>;
  confirmPhoneChange: (newPhoneNumber: string, code: string) => Promise<boolean>;
}

const EMPTY: AccountDraft = { nickname: '', email: '', phoneNumber: '' };

function toDraft(user: UserDto | null): AccountDraft {
  if (!user) return { ...EMPTY };
  return {
    nickname: user.nickname ?? '',
    email: user.email ?? '',
    phoneNumber: user.phoneNumber ?? '',
  };
}

export function useAccountSettings(
  options: UseAccountSettingsOptions = {},
): UseAccountSettingsReturn {
  const client = options.client ?? null;
  const api = client ? useProfileApi(client) : null;
  const stepUpPrompt = client && !options.stepUp ? new StepUpPromptController({ client }) : null;
  const verifyStepUp =
    options.stepUp ?? (stepUpPrompt ? (scope: string) => stepUpPrompt.verify(scope) : null);

  const profile = ref<UserDto | null>(null);
  const draft = ref<AccountDraft>({ ...EMPTY });
  const twoFactor = ref<TwoFactorStatusDto | null>(null);
  const totpSetup = ref<TotpSetupDto | null>(null);
  const sessions = ref<readonly UserSessionDto[]>([]);
  const loading = ref(false);
  const busy = ref(false);
  const error = ref<string | null>(null);

  const available = computed(() => api !== null);
  /* Re-read per evaluation rather than cached: a refresh rotates the token
     but keeps the session, and a re-login changes both. Depending on
     `sessions` makes it re-evaluate whenever the list does. */
  const currentSessionId = computed(() => {
    void sessions.value;
    return readSessionIdClaim(client?.getAccessToken());
  });
  function isCurrentSession(session: UserSessionDto): boolean {
    const current = currentSessionId.value;
    return current !== null && session.id.toLowerCase() === current.toLowerCase();
  }
  const otherSessions = computed(() => sessions.value.filter((s) => !isCurrentSession(s)));
  const dirty = computed(() => {
    const base = toDraft(profile.value);
    return (
      draft.value.nickname !== base.nickname ||
      draft.value.email !== base.email ||
      draft.value.phoneNumber !== base.phoneNumber
    );
  });

  /** Every write funnels through here so the busy flag and the "writes never
   *  swallow" rule are stated once instead of nine times - and so the step-up
   *  loop is too: a `[RequireStepUp]` challenge opens the prompt, and the
   *  write is replayed once the user has verified. Cancelling leaves the
   *  challenge as the visible failure rather than a silent no-op. */
  async function write(run: () => Promise<{ succeeded?: boolean; message?: string } | void>): Promise<boolean> {
    if (!api) return false;
    busy.value = true;
    error.value = null;
    try {
      const result = verifyStepUp ? await withStepUp(run, verifyStepUp) : await run();
      if (result && result.succeeded === false) {
        error.value = result.message || 'Request failed';
        return false;
      }
      return true;
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Request failed';
      return false;
    } finally {
      busy.value = false;
    }
  }

  async function load(): Promise<void> {
    if (!api) return;
    loading.value = true;
    try {
      /* Both in flight together, and independently tolerant: a deployment with
         2FA switched off should still show the profile. */
      const [profileResult, twoFactorResult] = await Promise.allSettled([
        api.get(),
        api.getTwoFactorStatus(),
      ]);

      if (profileResult.status === 'fulfilled') {
        const user = profileResult.value?.data ?? null;
        profile.value = user;
        draft.value = toDraft(user);
      }
      if (twoFactorResult.status === 'fulfilled') {
        twoFactor.value = twoFactorResult.value?.data ?? null;
      }
    } finally {
      loading.value = false;
    }
  }

  async function refreshTwoFactor(): Promise<void> {
    if (!api) return;
    try {
      const result = await api.getTwoFactorStatus();
      twoFactor.value = result?.data ?? null;
    } catch {
      /* leave the last known status rather than blanking the panel */
    }
  }

  async function loadSessions(): Promise<void> {
    if (!api) return;
    try {
      const result = await api.getSessions();
      sessions.value = result?.data ?? [];
    } catch {
      sessions.value = [];
    }
  }

  async function saveProfile(): Promise<boolean> {
    const ok = await write(async () => {
      /* `nickname` only. Email and phone are shown read-only because changing
         either is a verify-code flow (`/change-email/send-code` +
         `/confirm`), not a field edit - putting them in this form would let a
         user type a new address and believe it took effect. */
      return api!.update({ nickname: draft.value.nickname.trim() || null });
    });
    if (ok) {
      await load();
    }
    return ok;
  }

  function resetDraft(): void {
    draft.value = toDraft(profile.value);
    error.value = null;
  }

  async function changePassword(currentPassword: string, newPassword: string): Promise<boolean> {
    return write(() => api!.changePassword({ currentPassword, newPassword }));
  }

  async function beginTotp(): Promise<boolean> {
    const ok = await write(async () => {
      const result = await api!.getTotpSetup();
      totpSetup.value = result?.data ?? null;
      return result;
    });
    if (!ok) totpSetup.value = null;
    return ok;
  }

  async function confirmTotp(verificationCode: string): Promise<boolean> {
    const ok = await write(() => api!.enableTotp({ verificationCode }));
    if (ok) {
      /* Drop the secret the moment it is no longer needed - it stays on screen
         as a QR code otherwise, and it is a credential. */
      totpSetup.value = null;
      await refreshTwoFactor();
    }
    return ok;
  }

  async function disableTotp(): Promise<boolean> {
    const ok = await write(() => api!.disableTotp());
    if (ok) await refreshTwoFactor();
    return ok;
  }

  async function suspendTwoFactor(): Promise<boolean> {
    const ok = await write(() => api!.suspendTwoFactor());
    if (ok) await refreshTwoFactor();
    return ok;
  }

  async function resumeTwoFactor(): Promise<boolean> {
    const ok = await write(() => api!.resumeTwoFactor());
    if (ok) await refreshTwoFactor();
    return ok;
  }

  async function revokeSession(sessionId: string): Promise<boolean> {
    const ok = await write(() => api!.revokeSession(sessionId));
    if (ok) await loadSessions();
    return ok;
  }

  /**
   * Default: every OTHER device; the backend identifies the caller's session
   * from the token's session claim and excludes it. `includeCurrent = true`
   * ends this session too - the caller must then clear local auth themselves,
   * because nothing here knows about routing or the auth store.
   */
  async function revokeAllSessions(includeCurrent = false): Promise<boolean> {
    const ok = await write(() => api!.revokeAllSessions(includeCurrent));
    if (ok) await loadSessions();
    return ok;
  }

  /* Two steps, deliberately not collapsed into one "save" - the code goes to
     the NEW address, which is what proves the user owns it. A single-field
     edit next to a Save button would let someone type an address they do not
     control and be told it worked. */
  async function sendEmailChangeCode(newEmail: string): Promise<boolean> {
    return write(() => api!.sendChangeEmailCode({ newAddress: newEmail.trim() }));
  }

  async function confirmEmailChange(newEmail: string, code: string): Promise<boolean> {
    const ok = await write(() =>
      api!.confirmChangeEmail({ newEmail: newEmail.trim(), code: code.trim() }),
    );
    if (ok) await load();
    return ok;
  }

  async function sendPhoneChangeCode(newPhoneNumber: string): Promise<boolean> {
    return write(() => api!.sendChangePhoneCode({ newAddress: newPhoneNumber.trim() }));
  }

  async function confirmPhoneChange(newPhoneNumber: string, code: string): Promise<boolean> {
    const ok = await write(() =>
      api!.confirmChangePhone({ newPhoneNumber: newPhoneNumber.trim(), code: code.trim() }),
    );
    if (ok) await load();
    return ok;
  }

  return {
    profile,
    draft,
    twoFactor,
    totpSetup,
    sessions,
    currentSessionId,
    otherSessions,
    loading,
    busy,
    error,
    available,
    dirty,
    stepUp: stepUpPrompt,
    load,
    loadSessions,
    saveProfile,
    resetDraft,
    changePassword,
    beginTotp,
    confirmTotp,
    disableTotp,
    suspendTwoFactor,
    resumeTwoFactor,
    isCurrentSession,
    revokeSession,
    revokeAllSessions,
    sendEmailChangeCode,
    confirmEmailChange,
    sendPhoneChangeCode,
    confirmPhoneChange,
  };
}
