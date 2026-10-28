/**
 * `defineChatApp()` - the assembly entry for a conversational AI product.
 *
 * The counterpart to `@tnzi/ui-admin`'s `defineAdminApp()`, and deliberately
 * much smaller: an admin console's value is 130-odd preset pages, while a chat
 * product has essentially one screen. What it does share is the boilerplate
 * nobody should be writing per app - the sign-in route, the auth guard, and the
 * session restore that has to run before the first navigation.
 *
 * ```ts
 * const { routes, install } = defineChatApp({
 *   runtime: getMyApp(),                  // createTnziClient() result
 *   home: () => import('./pages/ChatPage.vue'),
 *   login: { brandName: 'Acme', subheading: 'Start creating with Acme' },
 * })
 * const router = createRouter({ history: createWebHistory(), routes })
 * const app = createApp(App)
 * install(app, router)
 * app.mount('#app')
 * ```
 *
 * ## What it deliberately does NOT do
 *
 * It does not own the conversation screen. `TChatApp` is a component with 60+
 * props precisely so a product can decide what its own chat looks like; making
 * this factory render it would trade that away for a few saved lines. The
 * `home` component is yours.
 */
import type { App, Component } from 'vue';
import { preventPageZoom } from '../headless/preventPageZoom';
import type { Router, RouteRecordRaw } from 'vue-router';
import { createTnziAuthGuard } from '@tnzi/core/guards';
import { installAppUpdate, type AppUpdateOptions } from '@tnzi/core/app-update';
import {
  THEME_CONTEXT_KEY,
  createThemeContext,
  mergeThemeSettings,
  type ThemeSettings,
} from '@tnzi/ui';
import type { AdminAuthRuntime } from '@tnzi/ui';
import TAuthRoute from '../auth/TAuthRoute.vue';
import TGuestWelcome from '../auth/TGuestWelcome.vue';
import { TOOL_RENDERERS_KEY, type ToolRendererMap } from '../components/chat/chat-render-context';

export interface ChatAppLoginConfig {
  /** Path the sign-in route is mounted at. Default `/login`. */
  path?: string;
  /** Route name, referenced by the guard. Default `login`. */
  name?: string;
  brandName?: string;
  brandIcon?: string;
  /**
   * The brand's logotype (an iconify name), drawn in place of the `brandName`
   * text on the sign-in page and the guest welcome screen. `brandName` stays
   * its accessible name, so set both.
   */
  brandWordmark?: string;
  heading?: string;
  subheading?: string;
  termsHref?: string;
  privacyHref?: string;
  footnote?: string;
  /** Replace the whole sign-in page. Rarely needed - `TAuthRoute` is themed
   *  through the same tokens as the rest of the product. */
  component?: Component;
}

/**
 * The welcome screen signed-out visitors see instead of the sign-in page: they type first,
 * sign in when they send, and the message is sent after sign-in.
 */
export interface ChatAppGuestWelcomeConfig {
  /** Path of the welcome screen. Default `/welcome`. */
  path?: string;
  /** Route name. Default `welcome`. */
  name?: string;
  /** Headline. Defaults to the locale's landing greeting. */
  greeting?: string;
  subline?: string;
  /** Composer placeholder. Defaults to the locale's. */
  placeholder?: string;
  /** Under the composer: what sending does. Defaults to the locale's; '' for none. */
  hint?: string;
  /** Replace the screen. It should keep the message (`stashWelcomeMessage`) and open sign-in. */
  component?: Component;
}

export interface DefineChatAppOptions {
  /** `createTnziClient()`'s return value - `{ http, auth, authApi }`. */
  runtime: AdminAuthRuntime;
  /** The conversation screen. Lazy (`() => import(...)`) keeps it out of the
   *  sign-in bundle, which is the one a signed-out visitor downloads. */
  home: Component | (() => Promise<unknown>);
  /** Path the conversation screen is mounted at. Default `/`. */
  homePath?: string;
  /** Route name for the conversation screen. Default `home`. */
  homeName?: string;
  login?: ChatAppLoginConfig;
  /** Extra routes appended after the built-ins. */
  routes?: RouteRecordRaw[];
  /** Query key carrying the post-login destination. Default `redirect`. */
  redirectQuery?: string;
  /**
   * Install the auth guard. Default true. Turn it off only if the app mounts
   * its own - two guards racing to redirect is worse than none.
   */
  guard?: boolean;
  /**
   * The `@tnzi/ui` theme this product runs on. A partial `ThemeSettings` (most
   * products set `colors.primary` and nothing else) is merged over the
   * defaults; `false` opts out entirely.
   *
   * Establishing it here is what makes the colour actually reach the UI: the
   * theme fans out to naive-ui overrides, `--tnzi-*` CSS variables and UnoCSS
   * atoms at once, so one value moves the naive controls and this package's
   * own painted surfaces together. Ignored when the host app already provided
   * a theme context of its own.
   */
  theme?: Partial<ThemeSettings> | false;
  /**
   * Recovery for tabs left open across a deployment. On by default: a lazy
   * route whose chunk the deployment removed loads the target in full instead
   * of failing, and a new build is picked up on the next route navigation, so
   * a half-typed message is never reloaded away. `false` turns it off. Inert on
   * the Vite dev server. See `@tnzi/core/app-update`.
   */
  appUpdate?: false | Omit<AppUpdateOptions, 'router'>;
  /**
   * Signed-out visitors land on a welcome screen instead of the sign-in page: they type
   * their first message, sign in when they send it, and the conversation screen sends it
   * once they are in (`useChatThreads` does this by itself). Off by default. Needs the
   * built-in guard (`guard` not false).
   */
  guestWelcome?: boolean | ChatAppGuestWelcomeConfig;
  /**
   * Tool renderers for the whole app, by tool name: a product's own view of its own tools.
   * Every other tool shows the generic card. A shell's `toolRenderers` prop wins on the
   * same name.
   */
  toolRenderers?: ToolRendererMap;
  /**
   * Turn off the browser's page zoom (pinch, double-tap, zoom on focusing a field)
   * so the app behaves like an app on a phone; see `preventPageZoom` in
   * `@tnzi/ui-ai/headless` for what it does per browser. Off by default: page zoom
   * is how some people read small text, so giving it up is the product's call.
   * A component that handles pinch itself keeps working.
   */
  preventPageZoom?: boolean;
}

export interface DefineChatAppResult {
  /** Feed straight to `createRouter({ routes })`. */
  routes: RouteRecordRaw[];
  /** Registers the auth guard. Call before `app.mount()`. */
  install: (app: App, router: Router) => void;
}

export function defineChatApp(options: DefineChatAppOptions): DefineChatAppResult {
  const loginPath = options.login?.path ?? '/login';
  const loginName = options.login?.name ?? 'login';
  const homePath = options.homePath ?? '/';
  const homeName = options.homeName ?? 'home';
  const redirectQuery = options.redirectQuery ?? 'redirect';
  const welcome = options.guestWelcome
    ? (typeof options.guestWelcome === 'object' ? options.guestWelcome : {})
    : null;
  const welcomePath = welcome?.path ?? '/welcome';
  const welcomeName = welcome?.name ?? 'welcome';
  /* Only when set: a `component` that replaces either page receives these route
     props, and one that does not declare this newer prop would otherwise get
     it as a fallthrough attribute (overriding what it passes to its own root). */
  const wordmarkProp = options.login?.brandWordmark
    ? { brandWordmark: options.login.brandWordmark }
    : {};

  const routes: RouteRecordRaw[] = [
    {
      path: loginPath,
      name: loginName,
      component: options.login?.component ?? TAuthRoute,
      // Forwarded as props so the same component serves any branding without a
      // per-app wrapper.
      props: {
        runtime: options.runtime,
        brandName: options.login?.brandName ?? '',
        brandIcon: options.login?.brandIcon ?? '',
        ...wordmarkProp,
        heading: options.login?.heading ?? '',
        subheading: options.login?.subheading ?? '',
        termsHref: options.login?.termsHref ?? '',
        privacyHref: options.login?.privacyHref ?? '',
        footnote: options.login?.footnote ?? '',
        homePath,
        redirectQuery,
      },
    },
    {
      path: homePath,
      name: homeName,
      component: options.home as Component,
      meta: { requiresAuth: true },
    },
    ...(welcome
      ? [{
          path: welcomePath,
          name: welcomeName,
          component: welcome.component ?? TGuestWelcome,
          props: {
            runtime: options.runtime,
            brandName: options.login?.brandName ?? '',
            brandIcon: options.login?.brandIcon ?? '',
            ...wordmarkProp,
            greeting: welcome.greeting ?? '',
            subline: welcome.subline ?? '',
            placeholder: welcome.placeholder ?? '',
            ...(welcome.hint !== undefined ? { hint: welcome.hint } : {}),
            loginName,
            homePath,
            redirectQuery,
          },
          meta: { requiresAuth: false },
        } satisfies RouteRecordRaw]
      : []),
    ...(options.routes ?? []),
  ];

  function install(app: App, router: Router): void {
    /* Establish the `@tnzi/ui` theme unless the host already did.
     *
     * This package is an application package built on `@tnzi/ui`, and that is
     * where the theme lives: one `ThemeSettings` fans out to naive-ui
     * overrides, `--tnzi-*` CSS variables and UnoCSS atoms. Chat apps were not
     * calling `createTnziUi()`, so none of those variables were ever on the
     * document - which made this package's own tokens fall back to their
     * literals and left "change the product's look" with nothing to change.
     *
     * Skipped when a host context already exists so an app that mounts
     * `createTnziUi()` itself keeps ownership; `provideTheme` here would
     * shadow it. */
    if (options.theme !== false && !app._context.provides[THEME_CONTEXT_KEY as symbol]) {
      const settings = mergeThemeSettings(
        typeof options.theme === 'object' ? options.theme : {},
      );
      /* The context writes the `--tnzi-*` variables and the `dark` class
         itself, now and on every change to its settings: the palette AND the
         light/dark half both live in that map, so a mode flip re-emits it.
         `TChatApp` drives the mode through this context (`autoApplyTheme`),
         which is what keeps the tokens in the mode the user picked. */
      app.provide(THEME_CONTEXT_KEY, createThemeContext(settings));
    }

    // Independent of the auth guard: a stale tab breaks the same way signed in or not.
    if (options.appUpdate !== false) {
      installAppUpdate({ ...options.appUpdate, router });
    }

    // App-wide, for the life of the page: an app does not stop being an app.
    if (options.preventPageZoom) preventPageZoom();

    // App-wide tool renderers: every shell's message renderers see them.
    if (options.toolRenderers) {
      app.provide(TOOL_RENDERERS_KEY, options.toolRenderers);
    }

    if (options.guard === false) return;

    // Session expiry. `createTnziClient` already clears the auth state when a
    // 401 survives the refresh attempt, but nothing moved the user off the
    // page - the next click just failed again. Mirror `@tnzi/ui-admin`: go to
    // the login route with the current location in the redirect query so a
    // re-login lands back where they were. Optional call keeps an older core
    // dist without the listener API working.
    options.runtime.http.addUnauthorizedListener?.(() => {
      const current = router.currentRoute.value;
      if (current.name === loginName) return;
      void router.replace({ name: loginName, query: { [redirectQuery]: current.fullPath } });
    });


    const { auth } = options.runtime;
    // `@tnzi/core`'s guard, not a local one. It already handles the parts worth
    // getting right - restore memoised across concurrent navigations, the
    // login route exempt, the destination carried in the redirect query - and
    // a second implementation here would be exactly the duplication this whole
    // factory exists to remove.
    router.beforeEach(
      createTnziAuthGuard({
        isLoggedIn: () => auth.isLoggedIn,
        restore: () => auth.restoreAuth(),
        loginRouteName: loginName,
        redirectQueryKey: redirectQuery,
        ...(welcome
          ? {
              publicRouteNames: [welcomeName],
              // A signed-out visit to the conversation screen lands on the welcome screen
              // (type first, sign in on send); everything else still goes to sign-in.
              resolveRedirect: ({ to, reason }) =>
                reason === 'unauthenticated' && (to.name === homeName || to.path === homePath)
                  ? { name: welcomeName }
                  : undefined,
            }
          : {}),
      }) as Parameters<Router['beforeEach']>[0],
    );
  }

  return { routes, install };
}
