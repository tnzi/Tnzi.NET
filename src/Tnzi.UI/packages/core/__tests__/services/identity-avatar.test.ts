import { describe, it, expect } from 'vitest';
import { profileAvatarUrl } from '../../src/services/identity/avatar';

const client = { resolveUrl: (url: string) => `/app/api${url}` };

describe('profileAvatarUrl', () => {
  it('renders the uploaded file through resolveUrl, ahead of an imported link', () => {
    expect(profileAvatarUrl(client, { avatarId: 'f1', avatar: 'https://cdn.example/a.png' })).toBe(
      '/app/api/files/f1/download',
    );
  });

  it('falls back to the external link when nothing was uploaded', () => {
    expect(profileAvatarUrl(client, { avatar: 'https://cdn.example/a.png' })).toBe('https://cdn.example/a.png');
    expect(profileAvatarUrl(client, { avatarUrl: 'https://cdn.example/b.png' })).toBe('https://cdn.example/b.png');
  });

  it('answers null when there is no picture, so the caller renders initials', () => {
    expect(profileAvatarUrl(client, { avatar: '  ', avatarId: null })).toBeNull();
    expect(profileAvatarUrl(client, null)).toBeNull();
  });

  it('encodes the id, so an id from a URL cannot reshape the path', () => {
    expect(profileAvatarUrl(client, { avatarId: 'a/b' })).toBe('/app/api/files/a%2Fb/download');
  });

  it('without a client it cannot build a file URL and uses the link instead', () => {
    expect(profileAvatarUrl(null, { avatarId: 'f1', avatar: 'https://cdn.example/a.png' })).toBe(
      'https://cdn.example/a.png',
    );
  });
});
