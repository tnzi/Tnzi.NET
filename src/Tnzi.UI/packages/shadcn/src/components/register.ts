/**
 * @tnzi/shadcn/components/register
 *
 * Global component registration for Tnzi UI (shadcn).
 * Separated from plugin.ts to support tree-shaking:
 * when `registerComponents: false`, bundlers can eliminate all component imports.
 */

import type { App } from 'vue';

// Auth components
import TLoginForm from './auth/TLoginForm.vue';
import TRegisterForm from './auth/TRegisterForm.vue';
import TPasswordReset from './auth/TPasswordReset.vue';

// Table components
import TDataTable from './table/TDataTable.vue';

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
import TSidebar from './layout/TSidebar.vue';
import THeader from './layout/THeader.vue';
import TBreadcrumb from './layout/TBreadcrumb.vue';

// Navigation components
import TMenu from './navigation/TMenu.vue';
import TNavBar from './navigation/TNavBar.vue';
import TTabBar from './navigation/TTabBar.vue';

// Dialog provider
import TDialogProvider from './dialog/TDialogProvider.vue';

/**
 * Register all Tnzi UI components globally.
 * This function is only called when `registerComponents: true` in plugin options.
 * When not called, the bundler can tree-shake all component imports.
 */
export function registerAllComponents(app: App): void {
  app.component('TLoginForm', TLoginForm);
  app.component('TRegisterForm', TRegisterForm);
  app.component('TPasswordReset', TPasswordReset);
  app.component('TDataTable', TDataTable);
  app.component('TDataList', TDataList);
  app.component('TForm', TForm);
  app.component('TDynamicForm', TDynamicForm);
  app.component('TSearchForm', TSearchForm);
  app.component('TUserCard', TUserCard);
  app.component('TStatCard', TStatCard);
  app.component('TAdminLayout', TAdminLayout);
  app.component('TSidebar', TSidebar);
  app.component('THeader', THeader);
  app.component('TBreadcrumb', TBreadcrumb);
  app.component('TMenu', TMenu);
  app.component('TNavBar', TNavBar);
  app.component('TTabBar', TTabBar);
  app.component('TDialogProvider', TDialogProvider);
}
