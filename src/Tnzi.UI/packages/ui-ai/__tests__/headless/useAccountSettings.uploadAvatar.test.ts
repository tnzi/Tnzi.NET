import { describe, it, expect, vi } from 'vitest';
import { useAccountSettings } from '../../src/headless/useAccountSettings';

const USER = { id: 'u1', userName: 'ada', nickname: 'Ada', email: 'ada@example.com', firstName: 'Ada', avatar: null, avatarId: null };

function makeClient() {
  let current: Record<string, unknown> = { ...USER };
  return {
    resolveUrl: (url: string) => `/api${url}`,
    get: vi.fn(async (url: string) =>
      url.endsWith('/two-factor/status')
        ? { succeeded: true, data: { isEnabled: false, supportedTypes: [], isTotpEnabled: false, methods: [] } }
        : { succeeded: true, data: current },
    ),
    put: vi.fn(async (_url: string, body: Record<string, unknown>) => {
      current = { ...current, avatarId: body.avatarId ?? null };
      return { succeeded: true, data: current };
    }),
    post: vi.fn(async () => ({ succeeded: true, data: null })),
    delete: vi.fn(async () => ({ succeeded: true, data: null })),
    upload: vi.fn(async () => ({ succeeded: true, data: { id: 'generic-id' } })),
  };
}

const png = () => new File([new Uint8Array(16)], 'me.png', { type: 'image/png' });

describe('useAccountSettings - uploadAvatar', () => {
  it('uses the consumer upload instead of files/upload and saves the id it returns', async () => {
    const client = makeClient();
    const uploadAvatar = vi.fn(async () => 'own-id');
    const a = useAccountSettings({ client: client as never, prepareAvatar: null, uploadAvatar });
    await a.load();

    expect(await a.setAvatar(png())).toBe(true);

    expect(uploadAvatar).toHaveBeenCalledWith(expect.any(File));
    expect(client.upload).not.toHaveBeenCalled();
    expect(client.put.mock.calls[0][1]).toMatchObject({ avatarId: 'own-id', avatarUrl: null, firstName: 'Ada' });
    expect(a.avatarUrl.value).toBe('/api/files/own-id/download');
  });

  it('hands it the prepared (cropped) file, not the original', async () => {
    const client = makeClient();
    const cropped = new Blob([new Uint8Array(4)], { type: 'image/png' });
    const uploadAvatar = vi.fn(async (_file: File) => 'own-id');
    const a = useAccountSettings({ client: client as never, prepareAvatar: async () => cropped, uploadAvatar });
    await a.load();

    await a.setAvatar(png());

    expect(uploadAvatar.mock.calls[0][0].size).toBe(4);
  });

  it('defaults to files/upload when no upload is given', async () => {
    const client = makeClient();
    const a = useAccountSettings({ client: client as never, prepareAvatar: null });
    await a.load();

    expect(await a.setAvatar(png())).toBe(true);
    expect(client.upload).toHaveBeenCalledWith('/files/upload', expect.any(File), expect.anything());
    expect(client.put.mock.calls[0][1]).toMatchObject({ avatarId: 'generic-id' });
  });

  it("shows the consumer's error message and does not touch the profile", async () => {
    const client = makeClient();
    const a = useAccountSettings({
      client: client as never,
      prepareAvatar: null,
      uploadAvatar: async () => {
        throw new Error('Photos are disabled for guest accounts.');
      },
    });
    await a.load();

    expect(await a.setAvatar(png())).toBe(false);
    expect(a.avatarError.value).toEqual({ reason: 'server', message: 'Photos are disabled for guest accounts.' });
    expect(client.put).not.toHaveBeenCalled();
    expect(a.avatarBusy.value).toBe(false);
  });

  it('treats an empty id as a failed upload', async () => {
    const client = makeClient();
    const a = useAccountSettings({ client: client as never, prepareAvatar: null, uploadAvatar: async () => '  ' });
    await a.load();

    expect(await a.setAvatar(png())).toBe(false);
    expect(a.avatarError.value?.reason).toBe('server');
    expect(client.put).not.toHaveBeenCalled();
  });

  it('still refuses a wrong type or size before calling the consumer upload', async () => {
    const client = makeClient();
    const uploadAvatar = vi.fn(async () => 'own-id');
    const a = useAccountSettings({ client: client as never, uploadAvatar, avatarMaxBytes: 8 });
    await a.load();

    expect(await a.setAvatar(png())).toBe(false);
    expect(a.avatarError.value?.reason).toBe('size');
    expect(uploadAvatar).not.toHaveBeenCalled();
  });
});
