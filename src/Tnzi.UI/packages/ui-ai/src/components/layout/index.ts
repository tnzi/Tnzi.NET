/**
 * Layout components - structural chrome that frames a screen.
 *
 * A *structural* domain, not a business one: these components carry no AI
 * semantics, they only divide space. They sit here rather than in a feature
 * folder so that a consumer assembling a custom shell can find them without
 * knowing which feature happened to introduce them first.
 */

export { default as TWorkspaceTopbar } from './TWorkspaceTopbar.vue';
export { default as TResourcePage } from './TResourcePage.vue';
export { default as TResourceCard } from './TResourceCard.vue';
export { default as TResourceEmpty } from './TResourceEmpty.vue';
export type { ResourceSuggestion } from './TResourceEmpty.vue';
export { default as TSettingRow } from './TSettingRow.vue';
export { default as TSettingGroup } from './TSettingGroup.vue';
export { default as TAppearanceAdmin } from './TAppearanceAdmin.vue';

/* Region frames, folded in from the former `src/shell/` on 2026-08-02. That
   directory's stated rule ("frames a region of an app shell") could not be told
   apart from this one's ("frames a screen") - which is why the settings surface
   ended up split, TSettingRow/TSettingGroup here and TSettingsDialog there. */
export { default as TCollapsibleSidebar } from './TCollapsibleSidebar.vue';
export { default as TSidebarNav } from './TSidebarNav.vue';
export type { NavItem, NavGroup, NavGroupAction } from './TSidebarNav.vue';
export { default as TChatRail } from './TChatRail.vue';
