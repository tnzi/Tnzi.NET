/**
 * Built-in settings pages.
 *
 * These differ from the resource views (Critical Rule #12, "framework gives the
 * entry, data stays with the consumer") because the data is the framework's
 * own and the routes are user-facing: `/users/profile/*` in `Tnzi.Identity`
 * and `/user-profile` in `Tnzi.AI`. There is nothing for a consumer to fetch,
 * so they ship wired - the consumer supplies an HttpClient and nothing else.
 */
export { default as TAccountSettings } from './TAccountSettings.vue';
export { default as TSecuritySettings } from './TSecuritySettings.vue';
export { default as TPersonalizationSettings } from './TPersonalizationSettings.vue';
export { default as TUsageSettings } from './TUsageSettings.vue';
