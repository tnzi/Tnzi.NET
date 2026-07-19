<template>
  <header
    class="t-page-header"
    :class="{
      't-page-header--bordered': bordered,
      't-page-header--surface': surface,
      't-page-header--inline-actions': inlineActions,
    }"
  >
    <div class="t-page-header__bar">
      <div class="t-page-header__left">
        <!-- Back lives OUTSIDE the title slot: a rich custom #title (avatar + name + tags)
             must not swallow the back affordance the page asked for via `back`. -->
        <button
          v-if="showBack"
          type="button"
          class="t-page-header__back"
          :aria-label="t('admin.common.back')"
          @click="onBack"
        >
          <TSvgIcon icon="mdi:arrow-left" :size="18" />
        </button>
        <slot name="title">
          <TSvgIcon v-if="resolvedIcon" :icon="resolvedIcon" :size="20" class="t-page-header__icon" />
          <span class="t-page-header__title">{{ resolvedTitle }}</span>
          <NPopover v-if="resolvedHelp" trigger="hover" placement="bottom-start">
            <template #trigger>
              <button type="button" class="t-page-header__help" :aria-label="resolvedHelpTitle">
                <TSvgIcon icon="mdi:information-outline" :size="16" />
              </button>
            </template>
            <div class="t-page-header__help-content">
              <div v-if="resolvedHelpTitle" class="t-page-header__help-title">{{ resolvedHelpTitle }}</div>
              <div class="t-page-header__help-body">{{ resolvedHelp }}</div>
            </div>
          </NPopover>
        </slot>
      </div>
      <div v-if="$slots.actions" class="t-page-header__actions">
        <slot name="actions" />
      </div>
    </div>
    <div v-if="$slots.extra" class="t-page-header__extra"><slot name="extra" /></div>
  </header>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'
import { NPopover } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { DEFAULT_ROUTE_ICONS } from '../../router/routeIcons'
import { maybeTranslateKey } from '../../pages/_shared/translate'
import { runBack } from './backTarget'

interface Props {
  title?: string
  icon?: string
  help?: string
  helpTitle?: string
  /**
   * Show a back affordance.
   *  - `true`   → `router.back()` (browser history).
   *  - `string` → `router.push(path)` (a static parent).
   *  - `{ fallback }` → SMART back: in-app history when present (restores the
   *    origin WITH its `?section=…` deep-link), else push `fallback`. Preferred
   *    for a drilled-into detail page. See {@link BackTarget}.
   */
  back?: boolean | string | { fallback?: string }
  bordered?: boolean
  /** Render the header as a white surface card (bg + radius + soft shadow). */
  surface?: boolean
  /**
   * Keep the title and the `#actions` slot on the SAME row on phones instead
   * of stacking them. Use when the actions are a compact icon cluster (e.g.
   * the list-page search 🔍/Advanced toggles) that fits beside the title.
   * The default (false) stacks actions below the title on narrow screens so
   * wide button groups don't crowd the title.
   */
  inlineActions?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  icon: undefined,
  help: undefined,
  helpTitle: undefined,
  back: undefined,
  bordered: false,
  surface: false,
  inlineActions: false,
  translate: undefined,
})

defineSlots<{
  title?: () => unknown
  actions?: () => unknown
  extra?: () => unknown
}>()

// useRoute/useRouter return undefined when no router is installed (e.g. unit
// tests that don't mount a router) — guard every access.
const route = useRoute() as RouteLocationNormalizedLoaded | undefined
const router = useRouter() as Router | undefined

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

const resolvedTitle = computed(() => {
  if (props.title) return maybeTranslateKey(props.translate, props.title, props.title)
  const metaTitle = route?.meta?.title as string | undefined
  return metaTitle ? maybeTranslateKey(props.translate, metaTitle, metaTitle) : ''
})

const resolvedIcon = computed<string | undefined>(() => {
  if (props.icon) return props.icon
  const metaIcon = route?.meta?.icon as string | undefined
  if (metaIcon) return metaIcon
  const name = route?.name as string | undefined
  return name ? DEFAULT_ROUTE_ICONS[name] : undefined
})

const resolvedHelp = computed(() => (props.help ? maybeTranslateKey(props.translate, props.help, props.help) : ''))
const resolvedHelpTitle = computed(() =>
  props.helpTitle ? maybeTranslateKey(props.translate, props.helpTitle, props.helpTitle) : t('admin.common.tip') || 'Tip',
)

const showBack = computed(() => props.back !== undefined && props.back !== false)
function onBack(): void {
  runBack(props.back, router)
}
</script>

<style scoped>
.t-page-header { display: flex; flex-direction: column; gap: 8px; flex-shrink: 0; }
.t-page-header--bordered { border-bottom: 1px solid var(--tnzi-border); padding-bottom: 12px; }
.t-page-header--surface {
  background: var(--tnzi-container-bg, #fff);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  padding: 12px 16px;
}
.t-page-header__bar {
  display: flex; align-items: center; justify-content: space-between; gap: 16px; min-height: 32px; flex-wrap: wrap;
}
.t-page-header__left { display: flex; align-items: center; gap: 8px; min-width: 0; flex: 1; }
.t-page-header__icon { color: var(--tnzi-primary); flex-shrink: 0; }
.t-page-header__title {
  font-size: 18px; font-weight: 600; color: var(--tnzi-base-text);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.t-page-header__back {
  display: inline-flex; align-items: center; justify-content: center;
  width: 28px; height: 28px; border-radius: var(--tnzi-admin-radius, 6px);
  border: none; background: transparent; color: var(--tnzi-base-text-muted, #888);
  cursor: pointer; padding: 0; flex-shrink: 0; transition: background 0.15s ease, color 0.15s ease;
}
.t-page-header__back:hover { color: var(--tnzi-primary); background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08); }
.t-page-header__help {
  display: inline-flex; align-items: center; justify-content: center;
  width: 22px; height: 22px; border-radius: 50%; border: none; background: transparent;
  color: var(--tnzi-base-text-muted, #888); cursor: pointer; padding: 0;
  transition: color 0.15s ease, background 0.15s ease;
}
.t-page-header__help:hover { color: var(--tnzi-primary); background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08); }
.t-page-header__help-content { max-width: 320px; }
.t-page-header__help-title { font-weight: 600; margin-bottom: 6px; color: var(--tnzi-base-text); }
.t-page-header__help-body { font-size: 13px; line-height: 1.5; color: var(--tnzi-base-text-muted, #888); }
.t-page-header__actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; justify-content: flex-end; flex-shrink: 0; }
.t-page-header__extra { min-width: 0; }
/* Phone: trim the title so it doesn't dominate the narrow bar. */
@media (max-width: 767px) {
  .t-page-header__title { font-size: 16px; }
}
/* Phone: stack wide action groups below the title — UNLESS the consumer
   opted into inline actions (compact icon cluster fits beside the title). */
@media (max-width: 640px) {
  .t-page-header:not(.t-page-header--inline-actions) .t-page-header__left { flex-basis: 100%; }
  .t-page-header:not(.t-page-header--inline-actions) .t-page-header__actions {
    flex-basis: 100%;
    justify-content: flex-start;
  }
}
</style>
