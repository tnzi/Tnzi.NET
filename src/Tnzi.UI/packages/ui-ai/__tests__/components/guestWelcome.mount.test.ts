/**
 * The signed-out welcome screen: the visitor types first; sending keeps the message for
 * after sign-in and opens the sign-in page with the conversation as the destination.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

vi.mock('@iconify/vue', async (importOriginal) => {
  const original = await importOriginal<typeof import('@iconify/vue')>();
  const { defineComponent, h: render } = await import('vue');
  return {
    ...original,
    Icon: defineComponent({
      props: { icon: { type: String, default: '' } },
      setup: (props) => () => render('i', { 'data-icon': props.icon }),
    }),
  };
});

const { default: TGuestWelcome } = await import('../../src/auth/TGuestWelcome.vue');
const { takeWelcomeMessage } = await import('../../src/headless/welcome-message');
const { en } = await import('../../src/locales/en');

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  sessionStorage.clear();
});

async function mountWelcome(loggedIn: boolean) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { render: () => h('div') } },
      { path: '/login', name: 'login', component: { render: () => h('div') } },
      { path: '/welcome', name: 'welcome', component: { render: () => h('div') } },
    ],
  });
  await router.push('/welcome');
  await router.isReady();
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({
    render: () => h(TGuestWelcome, {
      runtime: { auth: { isLoggedIn: loggedIn } } as never,
      brandName: 'Acme',
    }),
  });
  app.use(router);
  app.mount(host);
  await nextTick();
  return { el: host, router };
}

describe('TGuestWelcome', () => {
  it('sending keeps the message and opens sign-in, back to the conversation after', async () => {
    const { el, router } = await mountWelcome(false);
    expect(el.textContent).toContain('Acme');
    expect(el.textContent).toContain(en.welcome.hint);

    const input = el.querySelector('textarea') as HTMLTextAreaElement;
    input.value = 'plan my week';
    input.dispatchEvent(new Event('input'));
    await nextTick();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('login'));

    expect(router.currentRoute.value.query.redirect).toBe('/');
    expect(takeWelcomeMessage()).toBe('plan my week');
  });

  it('the sign-in button goes to sign-in without keeping anything', async () => {
    const { el, router } = await mountWelcome(false);
    (el.querySelector('.t-guest-welcome__sign-in') as HTMLButtonElement).click();
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('login'));
    expect(takeWelcomeMessage()).toBeNull();
  });

  it('a signed-in visitor is taken to the conversation', async () => {
    const { router } = await mountWelcome(true);
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
  });
});
