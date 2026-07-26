<template>
  <section
    class="t-admin-content"
    :class="{
      't-admin-content--inverted': surface === 'dark',
      't-admin-content--surface-light': surface === 'light',
    }"
    :data-full-content="appStore.fullContent ? 'true' : undefined"
  >
    <!-- Content-scoped theme. The "Card / List" surface repaints the naive
         NCard + NDataTable here (not on the outer provider) so it reaches the
         page content while leaving the chrome + teleported modals neutral. When
         the card tone is DARK the whole content sub-tree switches to naive's
         dark base, so borders / inputs / buttons / selects auto-match the dark
         cards instead of staying light-styled and illegible. A light-tinted
         card forces the light base even under global dark mode. -->
    <!-- `abstract`: render NO wrapper element (renderless provider). A real
         wrapper div would break the flex-height chain (the list card / table
         scroll-body would collapse to 0). Descendant naive components still
         pick up this inner theme via inject and inline their own vars, so the
         dark base + card overrides scope to the page content without any DOM.
         No `inline-theme-disabled` - that shares one global sheet and would
         defeat per-subtree scoping. -->
    <NConfigProvider
      abstract
      :theme="innerTheme"
      :theme-overrides="innerOverrides"
    >
      <Transition :name="currentTransition" mode="out-in">
        <div v-if="appStore.reloadFlag" :key="routeKey" class="t-admin-content__page">
          <slot />
        </div>
      </Transition>
    </NConfigProvider>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NConfigProvider, darkTheme, type GlobalThemeOverrides } from 'naive-ui'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { useAdminThemeStore, type PageTransition } from '../../stores/useAdminThemeStore'

interface Props {
  /**
   * Page transition name. Maps to one of the keyframe families in
   * `styles/transition.css` (all prefixed with `tnzi-`). Pass `'none'` to disable.
   */
  transitionName?: PageTransition
  /**
   * Optional key to force transition on route changes. Defaults to a static
   * 'default' so the component works outside vue-router; wire it to
   * `$route.fullPath` in real usage.
   */
  routeKey?: string
  /**
   * Surface tone when the content canvas carries a custom background color.
   * `'dark'` → light foreground for any direct canvas text; `'light'` → dark
   * foreground (dark-mode only). Cards on the canvas keep their own surface.
   */
  surface?: 'dark' | 'light'
}

const props = withDefaults(defineProps<Props>(), {
  transitionName: 'fade-slide',
  routeKey: 'default',
  surface: undefined,
})

const appStore = useAdminAppStore()
const themeStore = useAdminThemeStore()

const currentTransition = computed(() => {
  if (props.transitionName === 'none') return ''
  return `tnzi-${props.transitionName}`
})

// Content-scoped naive theme, derived from the "Card / List" surface tone:
//  - dark card  → naive `darkTheme` base (inputs/buttons/borders auto-match)
//  - light card → force the light base (readable even under global dark mode)
//  - no card    → `undefined` = inherit the global mode
const innerTheme = computed(() => {
  const tone = themeStore.cardTone
  if (tone === 'dark') return darkTheme
  if (tone === 'light') return null
  return undefined
})

// Repaint the card material itself to the chosen color on top of the base
// theme (the base only supplies naive's default light/dark card color). A
// custom text color forces the card/table foreground; otherwise the base
// theme's tone-appropriate text applies.
const innerOverrides = computed<GlobalThemeOverrides | undefined>(() => {
  const cardBg = themeStore.cardBg
  if (!cardBg) return undefined
  const fg = themeStore.cardTextColor
  const card: NonNullable<GlobalThemeOverrides['Card']> = { color: cardBg, colorEmbedded: cardBg }
  const table: NonNullable<GlobalThemeOverrides['DataTable']> = { tdColor: cardBg, thColor: cardBg }
  if (fg) {
    card.textColor = fg
    card.titleTextColor = fg
    table.tdTextColor = fg
    table.thTextColor = fg
  }
  return { Card: card, DataTable: table }
})

defineExpose({ currentTransition })
</script>

<style scoped>
/* soybean parity: the content frame itself does NOT scroll. Each page
   is responsible for placing its own scroll boundary - CRUD pages let
   the NDataTable scroll internally (so the pagination footer stays
   pinned), long-form pages opt into outer scroll by wrapping their
   body in `.t-page-scroll` (utility class in styles/polish.css). The
   page wrapper gets `height: 100% + flex column` so flex children
   (TCrudPage's list-card) can claim a real `flex: 1` of vertical
   space. */
.t-admin-content {
  position: relative;
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
  background-color: var(--tnzi-admin-content-bg, var(--tnzi-layout-bg));
  padding: var(--tnzi-admin-content-padding, 16px);
}
.t-admin-content[data-full-content='true'] {
  padding: 0;
}
/* Adaptive surface - a custom canvas background flips foreground tokens for
   any text drawn directly on the canvas (page headers usually sit inside
   cards, which keep their own surface, so this mostly affects bare copy). */
.t-admin-content--inverted {
  --tnzi-base-text: var(--tnzi-admin-content-fg, var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92)));
  --tnzi-base-text-muted: var(--tnzi-admin-inverted-text-muted, rgba(255, 255, 255, 0.6));
  color: var(--tnzi-base-text);
}
.t-admin-content--surface-light {
  --tnzi-base-text: var(--tnzi-admin-content-fg, var(--tnzi-admin-surface-light-text, rgba(0, 0, 0, 0.88)));
  --tnzi-base-text-muted: var(--tnzi-admin-surface-light-text-muted, rgba(0, 0, 0, 0.5));
  color: var(--tnzi-base-text);
}
/* Phone gutter is driven entirely by the `--tnzi-admin-content-padding` token
   (variables.css steps it 12 → 10 <768px → 8 <480px). No hardcoded phone media
   query here - that used to pin 10px and defeat the 8px step on small phones. */
.t-admin-content__page {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
</style>
