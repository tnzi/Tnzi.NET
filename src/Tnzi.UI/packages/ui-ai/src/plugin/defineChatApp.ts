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
import { watchEffect, type App, type Component } from 'vue';
import type { Router, RouteRecordRaw } from 'vue-router';
import { createTnziAuthGuard } from '@tnzi/core';
import {
  THEME_CONTEXT_KEY,
  createThemeContext,
  mergeThemeSettings,
  buildCssVars,
  injectCssVars,
  type ThemeSettings,
} from '@tnzi/ui';
import type { AdminAuthRuntime } from '@tnzi/ui';
import TAuthRoute from '../auth/TAuthRoute.vue';

export interface ChatAppLoginConfig {
  /** Path the sign-in route is mounted at. Default `/login`. */
  path?: string;
  /** Route name, referenced by the guard. Default `login`. */
  name?: string;
  brandName?: string;
  brandIcon?: string;
  heading?: string;
  subheading?: string;
  termsHref?: string;
  privacyHref?: string;
  footnote?: string;
  /** Replace the whole sign-in page. Rarely needed - `TAuthRoute` is themed
   *  through the same tokens as the rest of the product. */
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
      const context = createThemeContext(settings);
      app.provide(THEME_CONTEXT_KEY, context);

      /* Write the variables and keep writing them: the palette AND the
         light/dark half both live in this map, so a mode flip has to re-emit
         it or the surfaces keep the previous mode's greys. */
      watchEffect(() => {
        injectCssVars(buildCssVars(context.settings.value.colors, context.resolvedMode.value));
      });
    }

    if (options.guard === false) return;

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
      }) as Parameters<Router['beforeEach']>[0],
    );
  }

  return { routes, install };
}
