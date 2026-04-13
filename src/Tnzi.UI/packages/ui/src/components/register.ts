/**
 * @tnzi/ui/components/register
 *
 * Global component registration for Tnzi UI (Naive UI).
 * Separated from plugin.ts to support tree-shaking:
 * when `registerComponents: false`, bundlers can eliminate all component imports.
 */

import type { App } from 'vue';

// Auth components
import TLoginForm from './auth/TLoginForm.vue';
import TRegisterForm from './auth/TRegisterForm.vue';
import TPasswordReset from './auth/TPasswordReset.vue';

// Data components
import TTable from './data/TTable.vue';

// List components
import TDataList from './list/TDataList.vue';

// Form components
import TForm from './form/TForm.vue';
import TDynamicForm from './form/TDynamicForm.vue';
import TSearchForm from './form/TSearchForm.vue';

// Card components
import TUserCard from './card/TUserCard.vue';
import TStatCard from './card/TStatCard.vue';

// Layout components
import TAdminLayout from './layout/TAdminLayout.vue';
import TAppHeader from './layout/TAppHeader.vue';
import TAppSidebar from './layout/TAppSidebar.vue';
import TBreadcrumb from './layout/TBreadcrumb.vue';

// Navigation components
import TMenu from './navigation/TMenu.vue';
import TNavBar from './navigation/TNavBar.vue';
import TTabBar from './navigation/TTabBar.vue';

// Icon components
import { TIcon } from './icon/index';

// CRUD components
import TCrudPage from './crud/TCrudPage.vue';

/**
 * Register all Tnzi UI components globally.
 * This function is only called when `registerComponents: true` in plugin options.
 * When not called, the bundler can tree-shake all component imports.
 */
export function registerAllComponents(app: App): void {
  app.component('TLoginForm', TLoginForm);
  app.component('TRegisterForm', TRegisterForm);
  app.component('TPasswordReset', TPasswordReset);
  app.component('TTable', TTable);
  app.component('TDataList', TDataList);
  app.component('TForm', TForm);
  app.component('TDynamicForm', TDynamicForm);
  app.component('TSearchForm', TSearchForm);
  app.component('TUserCard', TUserCard);
  app.component('TStatCard', TStatCard);
  app.component('TAdminLayout', TAdminLayout);
  app.component('TAppHeader', TAppHeader);
  app.component('TAppSidebar', TAppSidebar);
  app.component('TBreadcrumb', TBreadcrumb);
  app.component('TMenu', TMenu);
  app.component('TNavBar', TNavBar);
  app.component('TTabBar', TTabBar);
  app.component('TIcon', TIcon);
  app.component('TCrudPage', TCrudPage);
}
