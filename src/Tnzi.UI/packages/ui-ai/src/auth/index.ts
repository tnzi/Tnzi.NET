/**
 * `@tnzi/ui-ai/auth` - the pre-auth surface for a conversational AI product.
 *
 * A dedicated entry rather than part of the root barrel: this page renders
 * BEFORE the app shell exists, so a consumer should be able to load it without
 * pulling in `TChatApp` and the whole conversation tree behind it.
 *
 * The auth LOGIC is not here - it lives in `@tnzi/ui` (`LoginCallbacks`,
 * `mapAuthConfig`, `buildOAuthProviders`, `buildDefaultLoginCallbacks`, …), the
 * same stack `@tnzi/ui-admin`'s login page consumes. This module is one
 * arrangement of those contracts; the admin page is another.
 */
export { default as TAuthPage } from './TAuthPage.vue';
export { default as TAuthRoute } from './TAuthRoute.vue';
export { default as TAuthField } from './TAuthField.vue';
export { default as TAuthProviderButton } from './TAuthProviderButton.vue';
