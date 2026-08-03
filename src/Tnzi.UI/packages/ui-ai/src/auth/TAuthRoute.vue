<script setup lang="ts">
/**
 * The sign-in ROUTE - `TAuthPage` with everything already wired.
 *
 * `TAuthPage` is the surface; this is the page a router mounts. It owns the
 * three things every consumer otherwise re-derives:
 *
 *   1. **Feature gating** - which sign-in routes exist is the deployment's
 *      decision, so they come from `GET /auth/config` rather than from props.
 *   2. **Callbacks** - `@tnzi/ui`'s standard orchestration over the wired
 *      runtime, including the two-factor hand-off.
 *   3. **Where to go afterwards** - honours `?redirect=` so a deep link
 *      survives being bounced through sign-in.
 *
 * `defineChatApp()` mounts this automatically. Register it by hand only if you
 * assemble your own router.
 */
import { onMounted, ref } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import {
  buildDefaultLoginCallbacks,
  buildOAuthProviders,
  mapAuthConfig,
  DEFAULT_LOGIN_FEATURES,
  type AdminAuthRuntime,
  type LoginFeatures,
  type LoginThirdPartyProvider,
  type Translate,
} from '@tnzi/ui';
import TAuthPage from './TAuthPage.vue';

const props = withDefaults(
  defineProps<{
    /** `createTnziClient()`'s return value - `{ http, auth, authApi }`. */
    runtime: AdminAuthRuntime;
    brandName?: string;
    brandIcon?: string;
    heading?: string;
    subheading?: string;
    termsHref?: string;
    privacyHref?: string;
    footnote?: string;
    translate?: Translate;
    /** Where to land when there is no `?redirect=`. Default `/`. */
    homePath?: string;
    /** Query key carrying the post-login destination. Default `redirect`. */
    redirectQuery?: string;
  }>(),
  {
    brandName: '',
    brandIcon: '',
    heading: '',
    subheading: '',
    termsHref: '',
    privacyHref: '',
    footnote: '',
    translate: undefined,
    homePath: '/',
    redirectQuery: 'redirect',
  },
);

const router = useRouter();
const route = useRoute();

const features = ref<LoginFeatures>(DEFAULT_LOGIN_FEATURES);
const providers = ref<LoginThirdPartyProvider[]>([]);

/**
 * Built ONCE, not as a computed. `buildDefaultLoginCallbacks` closes over the
 * in-flight two-factor challenge (temp token + method) between the password
 * step and the verify step, so rebuilding it mid-flow would drop the challenge
 * the user is currently answering. The runtime is a stable singleton anyway.
 */
const callbacks = buildDefaultLoginCallbacks(props.runtime);

/**
 * A failed config fetch leaves the defaults in place rather than blanking the
 * page: the backend rejects anything it has actually disabled, so the worst
 * case is one wasted attempt - against a sign-in page that renders nothing.
 */
onMounted(async () => {
  try {
    const res = await props.runtime.authApi.getConfig();
    const config = res?.data;
    if (!config) return;
    features.value = mapAuthConfig(config);
    providers.value = buildOAuthProviders(config.oAuthProviders ?? [], props.runtime.http);
  } catch {
    // Keep the defaults.
  }
});

function onAuthenticated(): void {
  const target = route.query[props.redirectQuery];
  void router.replace(typeof target === 'string' && target ? target : props.homePath);
}

function onOauth(provider: LoginThirdPartyProvider): void {
  void provider.onClick();
}
</script>

<template>
  <TAuthPage
    :brand-name="brandName"
    :brand-icon="brandIcon"
    :heading="heading"
    :subheading="subheading"
    :features="features"
    :providers="providers"
    :callbacks="callbacks"
    :translate="translate"
    :terms-href="termsHref"
    :privacy-href="privacyHref"
    :footnote="footnote"
    @authenticated="onAuthenticated"
    @oauth="onOauth"
  >
    <template #brand><slot name="brand" /></template>
    <template #legal><slot name="legal" /></template>
  </TAuthPage>
</template>
