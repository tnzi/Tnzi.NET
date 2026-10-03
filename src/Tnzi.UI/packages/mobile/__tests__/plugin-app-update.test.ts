import { describe, it, expect, vi, beforeEach } from 'vitest';

const installAppUpdate = vi.fn();
vi.mock('@tnzi/core/app-update', () => ({ installAppUpdate: (o: unknown) => installAppUpdate(o) }));

import { createApp } from 'vue';
import { createTnziMobile } from '../src/plugin';

function install(options: Parameters<typeof createTnziMobile>[0]) {
  createApp({ render: () => null }).use(createTnziMobile({ registerComponents: false, registerAdapters: false, ...options }));
}

describe('createTnziMobile app update', () => {
  beforeEach(() => installAppUpdate.mockClear());

  it('is on by default and forwards a real vue-router', () => {
    const router = {
      push: vi.fn(), replace: vi.fn(), back: vi.fn(), currentRoute: { value: { fullPath: '/' } },
      beforeEach: vi.fn(), onError: vi.fn(), resolve: vi.fn(),
    };
    install({ router, appUpdate: { checkInterval: 0 } });
    expect(installAppUpdate).toHaveBeenCalledWith({ checkInterval: 0, router });
  });

  it('drops a router that cannot drive navigation instead of crashing', () => {
    const router = { push: vi.fn(), replace: vi.fn(), back: vi.fn(), currentRoute: { value: { fullPath: '/' } } };
    install({ router });
    expect(installAppUpdate).toHaveBeenCalledWith({ router: undefined });
  });

  it('can be switched off', () => {
    install({ appUpdate: false });
    expect(installAppUpdate).not.toHaveBeenCalled();
  });
});
