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
import { useProfileApi } from '@tnzi/core/services/identity';
import type {
  UserDto,
  TwoFactorStatusDto,
  TotpSetupDto,
  UserSessionDto,
} from '@tnzi/core/services/identity';

export interface UseAccountSettingsOptions {
  client?: HttpClient | null;
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
  readonly loading: Ref<boolean>;
  readonly busy: Ref<boolean>;
  readonly error: Ref<string | null>;
  readonly available: ComputedRef<boolean>;
  readonly dirty: ComputedRef<boolean>;

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

  revokeSession: (sessionId: string) => Promise<boolean>;
  /** Signs out everywhere, including the current tab. */
  revokeAllSessions: () => Promise<boolean>;

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

  const profile = ref<UserDto | null>(null);
  const draft = ref<AccountDraft>({ ...EMPTY });
  const twoFactor = ref<TwoFactorStatusDto | null>(null);
  const totpSetup = ref<TotpSetupDto | null>(null);
  const sessions = ref<readonly UserSessionDto[]>([]);
  const loading = ref(false);
  const busy = ref(false);
  const error = ref<string | null>(null);

  const available = computed(() => api !== null);
  const dirty = computed(() => {
    const base = toDraft(profile.value);
    return (
      draft.value.nickname !== base.nickname ||
      draft.value.email !== base.email ||
      draft.value.phoneNumber !== base.phoneNumber
    );
  });

  /** Every write funnels through here so the busy flag and the "writes never
   *  swallow" rule are stated once instead of nine times. */
  async function write(run: () => Promise<{ succeeded?: boolean; message?: string } | void>): Promise<boolean> {
    if (!api) return false;
    busy.value = true;
    error.value = null;
    try {
      const result = await run();
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
   * Revokes every session **including this one** - the caller is signed out of
   * the tab they are sitting in.
   *
   * There is deliberately no "revoke the others" here: `UserSessionDto` carries
   * no marker for the current session, so the caller cannot be excluded, and
   * guessing at it (newest row, matching user agent) would sometimes kill the
   * wrong one and leave the attacker's session alive. The UI names this
   * "sign out everywhere" and warns, rather than promising a distinction the
   * data cannot make.
   */
  async function revokeAllSessions(): Promise<boolean> {
    const ok = await write(() => api!.revokeAllSessions());
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
    loading,
    busy,
    error,
    available,
    dirty,
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
    revokeSession,
    revokeAllSessions,
    sendEmailChangeCode,
    confirmEmailChange,
    sendPhoneChangeCode,
    confirmPhoneChange,
  };
}
