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
  useAuthApi,
  withStepUp,
  readSessionIdClaim,
  registerPasskey,
  isPasskeySupported,
  StepUpPromptController,
  TwoFactorType,
  profileAvatarUrl,
  AVATAR_ACCEPTED_TYPES,
  DEFAULT_AVATAR_MAX_BYTES,
} from '@tnzi/core/services/identity';
import type {
  PasskeyCredentialDto,
  UserDto,
  TwoFactorStatusDto,
  TotpSetupDto,
  UserSessionDto,
  StepUpGrantDto,
  UpdateProfileDto,
} from '@tnzi/core/services/identity';
import { useStorageApi } from '@tnzi/core/services/storage';

/** Why a profile photo was not taken. `server` carries the backend's own sentence. */
export interface AvatarFailure {
  reason: 'type' | 'size' | 'server';
  message?: string;
}

/** Why deleting the account did not go through. */
export interface AccountDeletionFailure {
  /** `mismatch` = the typed confirmation is not the account's address. */
  reason: 'mismatch' | 'server';
  /** Backend error code (`IDENTITY_USER_IN_USE`, `IDENTITY_STEP_UP_REQUIRED`, ...). */
  errorCode?: string;
  message?: string;
}

/** Longest edge of the stored photo after the square crop. */
export const AVATAR_OUTPUT_SIZE = 512;

/**
 * Check a picked file against the avatar rules before anything is uploaded.
 * The backend checks the same rules by file signature; this copy only saves
 * the user a round trip and gives them a sentence in their own language.
 */
export function validateAvatarFile(
  file: Pick<File, 'type' | 'size'>,
  maxBytes: number = DEFAULT_AVATAR_MAX_BYTES,
): AvatarFailure | null {
  if (!AVATAR_ACCEPTED_TYPES.includes(file.type)) return { reason: 'type' };
  if (file.size > maxBytes) return { reason: 'size' };
  return null;
}

/**
 * Centre-crop an image to a square and scale it down to `size` pixels.
 *
 * A profile photo is shown in a circle everywhere, so a portrait or landscape
 * picture would otherwise be cropped by CSS on every render, differently in
 * each place. Doing it once here also shrinks a 10 MB phone photo to a few
 * dozen kilobytes. Resolves the original file when the browser cannot decode
 * or re-encode it (no canvas): the backend still enforces type and size.
 */
export async function cropAvatarSquare(file: File, size: number = AVATAR_OUTPUT_SIZE): Promise<Blob> {
  if (typeof document === 'undefined' || typeof createImageBitmap !== 'function') return file;
  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file);
  } catch {
    return file;
  }
  try {
    const side = Math.min(bitmap.width, bitmap.height);
    if (side <= 0) return file;
    const target = Math.min(size, side);
    const canvas = document.createElement('canvas');
    canvas.width = target;
    canvas.height = target;
    const ctx = canvas.getContext('2d');
    if (!ctx) return file;
    ctx.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, target, target);
    // JPEG stays JPEG (photos); everything else becomes PNG, which keeps
    // transparency and is encodable in every browser (WebP encoding is not).
    const type = file.type === 'image/jpeg' ? 'image/jpeg' : 'image/png';
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, 0.9));
    return blob ?? file;
  } finally {
    bitmap.close?.();
  }
}

/**
 * The whole self-service profile, for `PUT /users/profile`.
 *
 * ★ That endpoint REPLACES the profile: a field left out is cleared. Saving
 * only the display name used to wipe the first and last name, the photo, the
 * bio and the rest. Every profile write starts from what the server last said.
 */
function profilePayload(user: UserDto | null, changes: Partial<UpdateProfileDto>): UpdateProfileDto {
  return {
    firstName: user?.firstName ?? null,
    lastName: user?.lastName ?? null,
    nickname: user?.nickname ?? null,
    avatarUrl: user?.avatar ?? null,
    avatarId: user?.avatarId ?? null,
    gender: user?.gender ?? 0,
    birthday: user?.birthday ?? null,
    bio: user?.bio ?? null,
    address: user?.address ?? null,
    website: user?.website ?? null,
    ...changes,
  };
}

export interface UseAccountSettingsOptions {
  client?: HttpClient | null;
  /** Avatar size limit checked before uploading. Default 10 MB (the backend default). */
  avatarMaxBytes?: number;
  /**
   * Turns the picked file into what is uploaded. Default `cropAvatarSquare`
   * (centre square, 512 px). Pass `null` to upload the file unchanged.
   */
  prepareAvatar?: ((file: File) => Promise<Blob>) | null;
  /**
   * Stores the (prepared) photo and resolves the stored file's id, which is
   * then written into the profile's `avatarId`. Default: the storage
   * module's generic `POST files/upload`. Give one when the deployment does
   * not expose that route (or replaces it with its own upload): the id must
   * still be a storage file the user can read, because the backend checks
   * ownership, type and size against it before accepting it as an avatar.
   * Throw to refuse; the error's message is shown as the photo error.
   */
  uploadAvatar?: (file: File) => Promise<string>;
  /**
   * Called with the fresh profile after any change to it (display name,
   * photo). The shell's own copy of the user - the account button in
   * `TChatApp`, a header avatar - lives outside this controller, so this is
   * where a consumer refreshes it (e.g. `auth.fetchUserProfile()`).
   */
  onProfileChange?: (profile: UserDto) => void | Promise<void>;
  /**
   * Called once the account is deleted. The backend has already revoked every
   * session, so this is where the consumer clears local auth and leaves
   * (e.g. `auth.clearAuth()` then route to the sign-in page). Without it the
   * page stays up on a session that no longer exists.
   */
  onAccountDeleted?: () => void | Promise<void>;
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
  /** The signed-in user's registered passkeys / security keys (see `loadPasskeys`). */
  readonly passkeys: Ref<readonly PasskeyCredentialDto[]>;
  /**
   * Whether this browser can run the passkey ceremony. Passkey rows are
   * offered only where it is true; a deployment-level switch shows up as the
   * absence of the Passkey entry in `twoFactor.methods`.
   */
  readonly passkeySupported: boolean;
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
  /** Turn the emailed verification code on or off as a second factor. */
  setEmailTwoFactor: (enabled: boolean) => Promise<boolean>;
  /** Allow (or stop allowing) the registered passkeys as a second factor. */
  setPasskeyTwoFactor: (enabled: boolean) => Promise<boolean>;
  /**
   * Run the browser ceremony and store a new passkey; when the passkey method
   * is not yet a second factor it is switched on afterwards. Resolves false
   * when the user dismissed the system dialog (no error is set) or on failure.
   */
  addPasskey: () => Promise<boolean>;
  /** Refresh `passkeys`. Fail-safe: an unavailable listing reads as no keys. */
  loadPasskeys: () => Promise<void>;
  removePasskey: (credentialId: string) => Promise<boolean>;

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

  /**
   * The profile photo to render, already a URL (the uploaded file ahead of an
   * imported link); null when there is none and the initials should show.
   */
  readonly avatarUrl: ComputedRef<string | null>;
  /** True while a photo is being prepared, uploaded or saved. */
  readonly avatarBusy: Ref<boolean>;
  readonly avatarError: Ref<AvatarFailure | null>;
  /** Byte limit the picker enforces (`avatarMaxBytes`). */
  readonly avatarMaxBytes: number;
  /** Validate, crop, upload and save a new photo. */
  setAvatar: (file: File) => Promise<boolean>;
  /** Clear the photo (both the uploaded file and any imported link). */
  removeAvatar: () => Promise<boolean>;

  /**
   * What the user must type to confirm deleting the account: the account's
   * email, or its user name when there is no email. Empty until loaded.
   */
  readonly deleteConfirmationTarget: ComputedRef<string>;
  readonly deleteBusy: Ref<boolean>;
  readonly deleteError: Ref<AccountDeletionFailure | null>;
  /**
   * Delete the account (`DELETE /users/profile/account`). Refuses locally when
   * `confirmation` is not `deleteConfirmationTarget` (case-insensitive). A
   * step-up challenge opens the re-authentication prompt like any other
   * protected write. On success calls `onAccountDeleted`.
   */
  deleteAccount: (confirmation: string) => Promise<boolean>;
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
  const storageApi = client ? useStorageApi(client) : null;
  const avatarMaxBytes = options.avatarMaxBytes ?? DEFAULT_AVATAR_MAX_BYTES;
  const prepareAvatar = options.prepareAvatar === undefined ? cropAvatarSquare : options.prepareAvatar;
  const customUpload = options.uploadAvatar ?? null;
  const authApi = client ? useAuthApi(client) : null;
  const passkeySupported = isPasskeySupported();
  const stepUpPrompt = client && !options.stepUp ? new StepUpPromptController({ client }) : null;
  const verifyStepUp =
    options.stepUp ?? (stepUpPrompt ? (scope: string) => stepUpPrompt.verify(scope) : null);

  const profile = ref<UserDto | null>(null);
  const draft = ref<AccountDraft>({ ...EMPTY });
  const twoFactor = ref<TwoFactorStatusDto | null>(null);
  const totpSetup = ref<TotpSetupDto | null>(null);
  const sessions = ref<readonly UserSessionDto[]>([]);
  const passkeys = ref<readonly PasskeyCredentialDto[]>([]);
  const loading = ref(false);
  const busy = ref(false);
  const error = ref<string | null>(null);
  const avatarBusy = ref(false);
  const avatarError = ref<AvatarFailure | null>(null);
  const deleteBusy = ref(false);
  const deleteError = ref<AccountDeletionFailure | null>(null);

  const avatarUrl = computed(() => profileAvatarUrl(client, profile.value));
  const deleteConfirmationTarget = computed(
    () => profile.value?.email?.trim() || profile.value?.userName?.trim() || '',
  );

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
  async function write(
    run: () => Promise<{ succeeded?: boolean; message?: string; errorCode?: string } | void>,
    onFail?: (failure: { message: string; errorCode?: string }) => void,
  ): Promise<boolean> {
    if (!api) return false;
    busy.value = true;
    if (!onFail) error.value = null;
    const fail = (failure: { message: string; errorCode?: string }): false => {
      if (onFail) onFail(failure);
      else error.value = failure.message;
      return false;
    };
    try {
      const result = verifyStepUp ? await withStepUp(run, verifyStepUp) : await run();
      if (result && result.succeeded === false) {
        return fail({ message: result.message || 'Request failed', errorCode: result.errorCode });
      }
      return true;
    } catch (err) {
      const errorCode = (err as { errorCode?: unknown } | null)?.errorCode;
      return fail({
        message: err instanceof Error ? err.message : 'Request failed',
        errorCode: typeof errorCode === 'string' ? errorCode : undefined,
      });
    } finally {
      busy.value = false;
    }
  }

  async function notifyProfileChange(): Promise<void> {
    if (!profile.value || !options.onProfileChange) return;
    try {
      await options.onProfileChange(profile.value);
    } catch {
      /* the save went through; a consumer's refresh failing is theirs to report */
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
      return api!.update(profilePayload(profile.value, { nickname: draft.value.nickname.trim() || null }));
    });
    if (ok) {
      await load();
      await notifyProfileChange();
    }
    return ok;
  }

  /* Every profile write replaces the whole profile, so one that has not been
     read yet would clear it. Load first rather than write blind. */
  async function ensureProfile(): Promise<boolean> {
    if (!profile.value) await load();
    return profile.value !== null;
  }

  async function writeAvatar(changes: Pick<UpdateProfileDto, 'avatarId' | 'avatarUrl'>): Promise<boolean> {
    const ok = await write(
      () => api!.update(profilePayload(profile.value, changes)),
      (failure) => {
        avatarError.value = { reason: 'server', message: failure.message };
      },
    );
    if (ok) {
      await load();
      await notifyProfileChange();
    }
    return ok;
  }

  async function uploadAvatarFile(file: File): Promise<string | null> {
    let prepared: Blob = file;
    if (prepareAvatar) {
      try {
        prepared = await prepareAvatar(file);
      } catch {
        prepared = file;
      }
    }
    const type = prepared.type || file.type;
    const upload = prepared instanceof File ? prepared : new File([prepared], renameForType(file.name, type), { type });
    if (customUpload) {
      try {
        const id = (await customUpload(upload))?.trim();
        if (!id) {
          avatarError.value = { reason: 'server', message: 'Upload failed' };
          return null;
        }
        return id;
      } catch (err) {
        avatarError.value = { reason: 'server', message: errorMessage(err) || 'Upload failed' };
        return null;
      }
    }
    try {
      const uploaded = await storageApi!.upload(upload);
      if (uploaded.succeeded === false || !uploaded.data?.id) {
        avatarError.value = { reason: 'server', message: uploaded.message || 'Upload failed' };
        return null;
      }
      return uploaded.data.id;
    } catch (err) {
      avatarError.value = { reason: 'server', message: err instanceof Error ? err.message : 'Upload failed' };
      return null;
    }
  }

  async function setAvatar(file: File): Promise<boolean> {
    if (!api || (!customUpload && !storageApi)) return false;
    avatarError.value = null;
    const invalid = validateAvatarFile(file, avatarMaxBytes);
    if (invalid) {
      avatarError.value = invalid;
      return false;
    }
    avatarBusy.value = true;
    try {
      if (!(await ensureProfile())) {
        avatarError.value = { reason: 'server', message: 'Your profile could not be loaded.' };
        return false;
      }
      const fileId = await uploadAvatarFile(file);
      if (!fileId) return false;
      // The id replaces any imported link: the uploaded photo is the user's choice.
      return await writeAvatar({ avatarId: fileId, avatarUrl: null });
    } finally {
      avatarBusy.value = false;
    }
  }

  async function removeAvatar(): Promise<boolean> {
    if (!api) return false;
    avatarError.value = null;
    avatarBusy.value = true;
    try {
      if (!(await ensureProfile())) return false;
      return await writeAvatar({ avatarId: null, avatarUrl: null });
    } finally {
      avatarBusy.value = false;
    }
  }

  async function deleteAccount(confirmation: string): Promise<boolean> {
    if (!api) return false;
    deleteError.value = null;
    const target = deleteConfirmationTarget.value;
    if (!target || confirmation.trim().toLowerCase() !== target.toLowerCase()) {
      deleteError.value = { reason: 'mismatch' };
      return false;
    }
    deleteBusy.value = true;
    try {
      const ok = await write(
        () => api!.deleteAccount(),
        (failure) => {
          deleteError.value = { reason: 'server', errorCode: failure.errorCode, message: failure.message };
        },
      );
      if (!ok) return false;
      if (options.onAccountDeleted) {
        try {
          await options.onAccountDeleted();
        } catch {
          /* the account is gone either way; the consumer's sign-out is theirs to report */
        }
      }
      return true;
    } finally {
      deleteBusy.value = false;
    }
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

  async function setEmailTwoFactor(enabled: boolean): Promise<boolean> {
    const ok = await write(() =>
      enabled
        ? api!.enableTwoFactor({ type: TwoFactorType.Email })
        : api!.disableTwoFactorMethod({ type: TwoFactorType.Email }),
    );
    if (ok) await refreshTwoFactor();
    return ok;
  }

  async function setPasskeyTwoFactor(enabled: boolean): Promise<boolean> {
    const ok = await write(() =>
      enabled
        ? api!.enableTwoFactor({ type: TwoFactorType.Passkey })
        : api!.disableTwoFactorMethod({ type: TwoFactorType.Passkey }),
    );
    if (ok) await refreshTwoFactor();
    return ok;
  }

  async function loadPasskeys(): Promise<void> {
    if (!authApi) return;
    try {
      const result = await authApi.getPasskeyCredentials();
      passkeys.value = result?.data ?? [];
    } catch {
      passkeys.value = [];
    }
  }

  /* Two writes, not one: a step-up challenge replays the write that was
     challenged, and replaying a combined "register, then enable" would run the
     browser ceremony (and store a second key) when only the enable was refused. */
  async function addPasskey(): Promise<boolean> {
    if (!client || !api) return false;
    let created = false;
    const registered = await write(async () => {
      created = (await registerPasskey(client)) !== null;
    });
    if (!registered || !created) return false;
    await loadPasskeys();
    const alreadyAllowed = twoFactor.value?.methods?.some(
      (m) => m.type === TwoFactorType.Passkey && m.enabled,
    );
    if (alreadyAllowed) {
      await refreshTwoFactor();
      return true;
    }
    return setPasskeyTwoFactor(true);
  }

  async function removePasskey(credentialId: string): Promise<boolean> {
    const ok = await write(() => authApi!.deletePasskeyCredential(credentialId));
    if (ok) {
      await loadPasskeys();
      await refreshTwoFactor();
    }
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
    passkeys,
    passkeySupported,
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
    setEmailTwoFactor,
    setPasskeyTwoFactor,
    addPasskey,
    loadPasskeys,
    removePasskey,
    isCurrentSession,
    revokeSession,
    revokeAllSessions,
    sendEmailChangeCode,
    confirmEmailChange,
    sendPhoneChangeCode,
    confirmPhoneChange,
    avatarUrl,
    avatarBusy,
    avatarError,
    avatarMaxBytes,
    setAvatar,
    removeAvatar,
    deleteConfirmationTarget,
    deleteBusy,
    deleteError,
    deleteAccount,
  };
}

/** The message of whatever a consumer's upload threw (an Error, a string, `{ message }`). */
function errorMessage(err: unknown): string {
  if (err instanceof Error) return err.message;
  if (typeof err === 'string') return err;
  const message = (err as { message?: unknown } | null)?.message;
  return typeof message === 'string' ? message : '';
}

/** `photo.webp` cropped to PNG uploads as `photo.png`, so the stored name matches the bytes. */
function renameForType(name: string, type: string): string {
  const ext = type === 'image/jpeg' ? 'jpg' : type === 'image/png' ? 'png' : type === 'image/webp' ? 'webp' : null;
  if (!ext) return name;
  const base = name.replace(/\.[^.]+$/, '') || 'avatar';
  return `${base}.${ext}`;
}
