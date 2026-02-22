/**
 * @tnzi/vant/components/register
 *
 * Global component registration for Tnzi UI (Vant).
 * Separated from plugin.ts to support tree-shaking:
 * when `registerComponents: false`, bundlers can eliminate all component imports.
 */

import type { App } from 'vue';

// Auth components
import TLoginForm from './auth/TLoginForm.vue';
import TRegisterForm from './auth/TRegisterForm.vue';
import TPasswordReset from './auth/TPasswordReset.vue';

// Form components
import TForm from './form/TForm.vue';
import TSearchForm from './form/TSearchForm.vue';
import TDynamicForm from './form/TDynamicForm.vue';

// Table components
import TDataTable from './table/TDataTable.vue';

// List components
import TDataList from './list/TDataList.vue';

// Card components
import TStatCard from './card/TStatCard.vue';
import TUserCard from './card/TUserCard.vue';

// Navigation components
import TMenu from './navigation/TMenu.vue';
import TNavBar from './navigation/TNavBar.vue';
import TTabBar from './navigation/TTabBar.vue';

/**
 * Register all Tnzi UI components globally.
 * This function is only called when `registerComponents: true` in plugin options.
 * When not called, the bundler can tree-shake all component imports.
 */
export function registerAllComponents(app: App): void {
  app.component('TLoginForm', TLoginForm);
  app.component('TRegisterForm', TRegisterForm);
  app.component('TPasswordReset', TPasswordReset);
  app.component('TForm', TForm);
  app.component('TSearchForm', TSearchForm);
  app.component('TDynamicForm', TDynamicForm);
  app.component('TDataTable', TDataTable);
  app.component('TDataList', TDataList);
  app.component('TStatCard', TStatCard);
  app.component('TUserCard', TUserCard);
  app.component('TMenu', TMenu);
  app.component('TNavBar', TNavBar);
  app.component('TTabBar', TTabBar);
}
