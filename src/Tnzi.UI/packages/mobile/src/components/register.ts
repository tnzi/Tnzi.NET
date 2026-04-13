/**
 * @tnzi/mobile/components/register
 *
 * Global component registration for Tnzi UI (Vant).
 * Separated from plugin.ts to support tree-shaking:
 * when `registerComponents: false`, bundlers can eliminate all component imports.
 */

import type { App } from 'vue';

// Auth components
import LoginForm from './auth/LoginForm.vue';
import RegisterForm from './auth/RegisterForm.vue';
import PasswordReset from './auth/PasswordReset.vue';

// Form components
import Form from './form/Form.vue';
import SearchBar from './form/SearchBar.vue';
import DynamicForm from './form/DynamicForm.vue';

// List components
import DataList from './list/DataList.vue';
import SwipeCell from './list/SwipeCell.vue';

// Card components
import StatCard from './card/StatCard.vue';
import UserCard from './card/UserCard.vue';

// Navigation components
import Menu from './navigation/Menu.vue';
import MobileNavBar from './navigation/MobileNavBar.vue';
import MobileTabBar from './navigation/MobileTabBar.vue';

/**
 * Register all Tnzi UI components globally.
 * This function is only called when `registerComponents: true` in plugin options.
 * When not called, the bundler can tree-shake all component imports.
 */
export function registerAllComponents(app: App): void {
  app.component('LoginForm', LoginForm);
  app.component('RegisterForm', RegisterForm);
  app.component('PasswordReset', PasswordReset);
  app.component('Form', Form);
  app.component('SearchBar', SearchBar);
  app.component('DynamicForm', DynamicForm);
  app.component('DataList', DataList);
  app.component('SwipeCell', SwipeCell);
  app.component('StatCard', StatCard);
  app.component('UserCard', UserCard);
  app.component('Menu', Menu);
  app.component('MobileNavBar', MobileNavBar);
  app.component('MobileTabBar', MobileTabBar);
}
