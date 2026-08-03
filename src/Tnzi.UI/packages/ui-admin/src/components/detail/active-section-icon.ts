import type { InjectionKey, Ref } from 'vue'

/**
 * Active nav-section icon, provided by a `side`/`tabs` detail PAGE so any
 * `TDetailSection` rendered inside its panel mirrors the left-menu icon before
 * its title - with zero prop threading.
 *
 * Why the PAGE provides it (not `TDetailLayout`/`TDetailHost`): a panel's slot
 * content is owned by the page, so its `$parent` chain leads to the page, not to
 * the layout that renders the `<slot>`. A provide from the layout would never
 * reach the slotted `TDetailSection`; a provide from the page does.
 *
 * `TDetailSection` injects this as a fallback for its explicit `icon` prop
 * (`icon ?? injected`), so a consumer's custom Settings/User-Center section that
 * uses the shared `TDetailSection` chrome gets the section icon for free.
 * Built-in panels that already know their icon (schema groups, AgentDetail
 * sections) keep passing it explicitly - the prop always wins.
 */
export const DETAIL_ACTIVE_SECTION_ICON: InjectionKey<Ref<string | undefined>> = Symbol(
  'tnzi-detail-active-section-icon',
)
