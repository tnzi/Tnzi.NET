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
export { default as TSettingRow } from './TSettingRow.vue';
export { default as TSettingGroup } from './TSettingGroup.vue';
export { default as TAppearanceAdmin } from './TAppearanceAdmin.vue';

/* Region frames, folded in from the former `src/shell/` on 2026-08-02. That
   directory's stated rule ("frames a region of an app shell") could not be told
   apart from this one's ("frames a screen") - which is why the settings surface
   ended up split, TSettingRow/TSettingGroup here and TSettingsDialog there. */
export { default as TCollapsibleSidebar } from './TCollapsibleSidebar.vue';
export { default as TSidebarNav } from './TSidebarNav.vue';
export type { NavItem, NavGroup, NavGroupAction, NavItemAction, NavBadgeTone } from './TSidebarNav.vue';
export { default as TChatRail } from './TChatRail.vue';
/* The rail's own controls: an icon button with its tooltip, and a button
   that opens a panel beside the rail (hover / focus / click). */
export { default as TRailButton } from './TRailButton.vue';
export { default as TRailFlyout } from './TRailFlyout.vue';
export { default as TAsidePanel } from './TAsidePanel.vue';

/* The standard toolbar of a content page (`TResourcePage`'s `#toolbar`): tabs
   on the left; search, icon actions and the primary action on the right. Its
   control classes (`.t-page-toolbar-icon`, `.t-page-toolbar-button`) are the
   global `page-toolbar.css`. */
export { default as TPageToolbar } from './TPageToolbar.vue';
export { default as TToolbarSearch } from './TToolbarSearch.vue';
export { default as TSegmented } from './TSegmented.vue';
export type { ElementAttrs, PageToolbarAction, PageToolbarPrimary, SegmentedOption } from './page-toolbar';
/* The folded menu's id scheme (`<actionId>::<itemId>`), for a page's own selectors. */
export { FOLDED_ID_SEPARATOR, foldedItemId } from './page-toolbar';
