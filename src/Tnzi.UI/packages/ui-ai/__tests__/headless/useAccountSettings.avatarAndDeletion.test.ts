import { describe, it, expect, vi } from 'vitest';
import { useAccountSettings, validateAvatarFile } from '../../src/headless/useAccountSettings';

const USER = {
  id: 'u1',
  userName: 'ada',
  nickname: 'Ada',
  email: 'Ada@Example.com',
  firstName: 'Ada',
  avatar: 'https://cdn.example/old.png',
  avatarId: null as string | null,
};
const TWO_FACTOR = { isEnabled: false, supportedTypes: [], isTotpEnabled: false, methods: [] };

function makeClient(over: Record<string, unknown> = {}) {
  let current = { ...USER };
  const client = {
    resolveUrl: (url: string) => `/api${url}`,
    get: vi.fn(async (url: string) => {
      if (url.endsWith('/two-factor/status')) return { succeeded: true, data: TWO_FACTOR };
      return { succeeded: true, data: current };
    }),
    put: vi.fn(async (_url: string, body: Record<string, unknown>) => {
      current = { ...current, avatarId: (body.avatarId as string | null) ?? null, avatar: (body.avatarUrl as string | null) ?? null };
      return { succeeded: true, data: current };
    }),
    post: vi.fn(async () => ({ succeeded: true, data: null })),
    delete: vi.fn(async () => ({ succeeded: true, data: null })),
    upload: vi.fn(async () => ({ succeeded: true, data: { id: 'file-9' } })),
    ...over,
  };
  return client;
}

const png = (size = 1024) => new File([new Uint8Array(size)], 'me.png', { type: 'image/png' });

describe('validateAvatarFile', () => {
  it('accepts PNG, JPEG and WebP within the limit', () => {
    for (const type of ['image/png', 'image/jpeg', 'image/webp']) {
      expect(validateAvatarFile({ type, size: 10 }, 100)).toBeNull();
    }
  });

  it('refuses other types (SVG executes script) and files over the limit', () => {
    expect(validateAvatarFile({ type: 'image/svg+xml', size: 10 })?.reason).toBe('type');
    expect(validateAvatarFile({ type: 'image/gif', size: 10 })?.reason).toBe('type');
    expect(validateAvatarFile({ type: 'image/png', size: 101 }, 100)?.reason).toBe('size');
  });
});

describe('useAccountSettings - profile photo', () => {
  it('uploads, then writes the new id over the imported link and keeps the rest of the profile', async () => {
    const client = makeClient();
    const onProfileChange = vi.fn();
    const a = useAccountSettings({ client: client as never, prepareAvatar: null, onProfileChange });
    await a.load();
    expect(a.avatarUrl.value).toBe('https://cdn.example/old.png');

    expect(await a.setAvatar(png())).toBe(true);

    expect(client.upload).toHaveBeenCalledWith('/files/upload', expect.any(File), expect.anything());
    const body = client.put.mock.calls[0][1];
    expect(body).toMatchObject({ avatarId: 'file-9', avatarUrl: null, firstName: 'Ada', nickname: 'Ada' });
    // The uploaded photo shows at once, through the deployment-aware URL.
    expect(a.avatarUrl.value).toBe('/api/files/file-9/download');
    expect(onProfileChange).toHaveBeenCalledWith(expect.objectContaining({ avatarId: 'file-9' }));
  });

  it('refuses a wrong type or an oversized file before uploading anything', async () => {
    const client = makeClient();
    const a = useAccountSettings({ client: client as never, avatarMaxBytes: 100, prepareAvatar: null });
    await a.load();

    expect(await a.setAvatar(new File(['<svg/>'], 'x.svg', { type: 'image/svg+xml' }))).toBe(false);
    expect(a.avatarError.value?.reason).toBe('type');
    expect(await a.setAvatar(png(101))).toBe(false);
    expect(a.avatarError.value?.reason).toBe('size');
    expect(client.upload).not.toHaveBeenCalled();
    expect(client.put).not.toHaveBeenCalled();
  });

  it('surfaces the server refusal of the photo and leaves the profile as it was', async () => {
    const client = makeClient({
      put: vi.fn(async () => ({
        succeeded: false,
        code: 400,
        errorCode: 'IDENTITY_AVATAR_INVALID',
        message: 'The avatar must be a PNG, JPEG, or WebP image.',
      })),
    });
    const a = useAccountSettings({ client: client as never, prepareAvatar: null });
    await a.load();

    expect(await a.setAvatar(png())).toBe(false);
    expect(a.avatarError.value).toEqual({ reason: 'server', message: 'The avatar must be a PNG, JPEG, or WebP image.' });
    // The profile form's own error line is not used for the photo.
    expect(a.error.value).toBeNull();
    expect(a.avatarUrl.value).toBe('https://cdn.example/old.png');
  });

  it('a failed upload never writes the profile', async () => {
    const client = makeClient({ upload: vi.fn(async () => ({ succeeded: false, message: 'Storage is not loaded' })) });
    const a = useAccountSettings({ client: client as never, prepareAvatar: null });
    await a.load();

    expect(await a.setAvatar(png())).toBe(false);
    expect(a.avatarError.value?.message).toBe('Storage is not loaded');
    expect(client.put).not.toHaveBeenCalled();
  });

  it('uploads what prepareAvatar returns (the square crop)', async () => {
    const client = makeClient();
    const cropped = new Blob([new Uint8Array(8)], { type: 'image/png' });
    const a = useAccountSettings({ client: client as never, prepareAvatar: async () => cropped });
    await a.load();

    await a.setAvatar(new File([new Uint8Array(64)], 'portrait.webp', { type: 'image/webp' }));

    const sent = client.upload.mock.calls[0][1] as File;
    expect(sent.size).toBe(8);
    expect(sent.name).toBe('portrait.png');
  });

  it('removing clears both the uploaded file and the imported link', async () => {
    const client = makeClient();
    const a = useAccountSettings({ client: client as never });
    await a.load();

    expect(await a.removeAvatar()).toBe(true);
    expect(client.put.mock.calls[0][1]).toMatchObject({ avatarId: null, avatarUrl: null, firstName: 'Ada' });
    expect(a.avatarUrl.value).toBeNull();
  });
});

describe('useAccountSettings - delete account', () => {
  it('asks for the account email, case-insensitively, and never calls the server on a mismatch', async () => {
    const client = makeClient();
    const a = useAccountSettings({ client: client as never });
    await a.load();
    expect(a.deleteConfirmationTarget.value).toBe('Ada@Example.com');

    expect(await a.deleteAccount('someone@else.com')).toBe(false);
    expect(a.deleteError.value?.reason).toBe('mismatch');
    expect(client.delete).not.toHaveBeenCalled();
  });

  it('deletes and hands over to the consumer to sign out', async () => {
    const client = makeClient();
    const onAccountDeleted = vi.fn();
    const a = useAccountSettings({ client: client as never, onAccountDeleted });
    await a.load();

    expect(await a.deleteAccount('  ada@example.com ')).toBe(true);
    expect(client.delete).toHaveBeenCalledWith('/users/profile/account');
    expect(onAccountDeleted).toHaveBeenCalledTimes(1);
  });

  it('keeps the server error code so the page can explain an account still in use', async () => {
    const client = makeClient({
      delete: vi.fn(async () => ({
        succeeded: false,
        code: 409,
        errorCode: 'IDENTITY_USER_IN_USE',
        message: 'This account is on the staff roster. Ask an administrator.',
      })),
    });
    const onAccountDeleted = vi.fn();
    const a = useAccountSettings({ client: client as never, onAccountDeleted });
    await a.load();

    expect(await a.deleteAccount('ada@example.com')).toBe(false);
    expect(a.deleteError.value).toEqual({
      reason: 'server',
      errorCode: 'IDENTITY_USER_IN_USE',
      message: 'This account is on the staff roster. Ask an administrator.',
    });
    expect(onAccountDeleted).not.toHaveBeenCalled();
  });

  it('a step-up challenge goes through the verifier and the delete is replayed', async () => {
    let calls = 0;
    const client = makeClient({
      delete: vi.fn(async () => {
        calls += 1;
        return calls === 1
          ? { succeeded: false, code: 401, errorCode: 'IDENTITY_STEP_UP_REQUIRED', errorDetails: { scope: 'identity.account.destroy' } }
          : { succeeded: true, data: null };
      }),
    });
    const stepUp = vi.fn(async () => ({ scope: 'identity.account.destroy' }) as never);
    const onAccountDeleted = vi.fn();
    const a = useAccountSettings({ client: client as never, stepUp, onAccountDeleted });
    await a.load();

    expect(await a.deleteAccount('ada@example.com')).toBe(true);
    expect(stepUp).toHaveBeenCalledWith('identity.account.destroy');
    expect(calls).toBe(2);
    expect(onAccountDeleted).toHaveBeenCalledTimes(1);
  });
});
