/**
 * @tnzi/ui/components/register
 *
 * Global component registration for Tnzi UI.
 * Separated from plugin.ts to support tree-shaking:
 * when `registerComponents: false`, bundlers can eliminate all component imports.
 */

import type { App } from 'vue';

// Auth components
import LoginForm from './auth/LoginForm.vue';
import RegisterForm from './auth/RegisterForm.vue';
import PasswordReset from './auth/PasswordReset.vue';

// Table components
import DataTable from './table/DataTable.vue';

// List components
import DataList from './list/DataList.vue';

// Form components
import Form from './form/Form.vue';
import DynamicForm from './form/DynamicForm.vue';
import SearchBar from './form/SearchBar.vue';

// Card components
import UserCard from './card/UserCard.vue';
import StatCard from './card/StatCard.vue';

// Layout components
import AdminLayout from './layout/AdminLayout.vue';
import AppSidebar from './layout/AppSidebar.vue';
import AppHeader from './layout/AppHeader.vue';
import Breadcrumb from './layout/Breadcrumb.vue';

// Navigation components
import Menu from './navigation/Menu.vue';
import MobileNavBar from './navigation/MobileNavBar.vue';
import MobileTabBar from './navigation/MobileTabBar.vue';

// Dialog provider
import DialogProvider from './dialog/DialogProvider.vue';

/**
 * Register all Tnzi UI components globally.
 * This function is only called when `registerComponents: true` in plugin options.
 * When not called, the bundler can tree-shake all component imports.
 */
export function registerAllComponents(app: App): void {
  app.component('LoginForm', LoginForm);
  app.component('RegisterForm', RegisterForm);
  app.component('PasswordReset', PasswordReset);
  app.component('DataTable', DataTable);
  app.component('DataList', DataList);
  app.component('Form', Form);
  app.component('DynamicForm', DynamicForm);
  app.component('SearchBar', SearchBar);
  app.component('UserCard', UserCard);
  app.component('StatCard', StatCard);
  app.component('AdminLayout', AdminLayout);
  app.component('AppSidebar', AppSidebar);
  app.component('AppHeader', AppHeader);
  app.component('Breadcrumb', Breadcrumb);
  app.component('Menu', Menu);
  app.component('MobileNavBar', MobileNavBar);
  app.component('MobileTabBar', MobileTabBar);
  app.component('DialogProvider', DialogProvider);
}
