import { describe, it, expect, beforeEach } from 'vitest';
import { effectScope, nextTick, ref } from 'vue';
import { useBodyScrollLock } from '../../src/composables/useBodyScrollLock';

beforeEach(() => {
  document.body.style.overflow = '';
});

describe('useBodyScrollLock', () => {
  it('locks while active and restores on release', async () => {
    const active = ref(false);
    const scope = effectScope();
    scope.run(() => useBodyScrollLock(active));

    expect(document.body.style.overflow).toBe('');

    active.value = true;
    await nextTick();
    expect(document.body.style.overflow).toBe('hidden');

    active.value = false;
    await nextTick();
    expect(document.body.style.overflow).toBe('');
    scope.stop();
  });

  it('restores the host page value rather than blanking it', async () => {
    document.body.style.overflow = 'scroll';
    const active = ref(true);
    const scope = effectScope();
    scope.run(() => useBodyScrollLock(active));

    expect(document.body.style.overflow).toBe('hidden');

    active.value = false;
    await nextTick();
    expect(document.body.style.overflow).toBe('scroll');
    scope.stop();
  });

  it('releases the lock when the owning scope is disposed', () => {
    const scope = effectScope();
    scope.run(() => useBodyScrollLock(() => true));
    expect(document.body.style.overflow).toBe('hidden');

    // This is the route-change case: unmounting mid-overlay used to leave the
    // page permanently unscrollable.
    scope.stop();
    expect(document.body.style.overflow).toBe('');
  });

  it('keeps the page locked until the last overlay closes', async () => {
    const first = ref(true);
    const second = ref(true);
    const scopeA = effectScope();
    const scopeB = effectScope();
    scopeA.run(() => useBodyScrollLock(first));
    scopeB.run(() => useBodyScrollLock(second));

    expect(document.body.style.overflow).toBe('hidden');

    first.value = false;
    await nextTick();
    expect(document.body.style.overflow).toBe('hidden');

    second.value = false;
    await nextTick();
    expect(document.body.style.overflow).toBe('');

    scopeA.stop();
    scopeB.stop();
  });
});
