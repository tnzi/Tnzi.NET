/**
 * @tnzi/mobile/components/register
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
import TSearchBar from './form/TSearchBar.vue';
import TDynamicForm from './form/TDynamicForm.vue';

// List components
import TDataList from './list/TDataList.vue';
import TSwipeCell from './list/TSwipeCell.vue';

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
  app.component('TSearchBar', TSearchBar);
  app.component('TDynamicForm', TDynamicForm);
  app.component('TDataList', TDataList);
  app.component('TSwipeCell', TSwipeCell);
  app.component('TStatCard', TStatCard);
  app.component('TUserCard', TUserCard);
  app.component('TMenu', TMenu);
  app.component('TNavBar', TNavBar);
  app.component('TTabBar', TTabBar);
}
