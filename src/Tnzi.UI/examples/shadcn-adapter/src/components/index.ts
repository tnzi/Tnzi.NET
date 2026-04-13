/**
 * @tnzi/ui/components
 *
 * Component exports.
 */

// Auth components
export { default as LoginForm } from './auth/LoginForm.vue';
export { default as RegisterForm } from './auth/RegisterForm.vue';
export { default as PasswordReset } from './auth/PasswordReset.vue';

// Table components
export { default as DataTable } from './table/DataTable.vue';

// List components
export { default as DataList } from './list/DataList.vue';

// Form components
export { default as Form } from './form/Form.vue';
export { default as DynamicForm } from './form/DynamicForm.vue';
export { default as SearchBar } from './form/SearchBar.vue';

// Card components
export { default as UserCard } from './card/UserCard.vue';
export { default as StatCard } from './card/StatCard.vue';

// Layout components
export { default as AdminLayout } from './layout/AdminLayout.vue';
export { default as AppSidebar } from './layout/AppSidebar.vue';
export { default as AppHeader } from './layout/AppHeader.vue';
export { default as Breadcrumb } from './layout/Breadcrumb.vue';

// Navigation components
export { default as Menu } from './navigation/Menu.vue';
export { default as MobileNavBar } from './navigation/MobileNavBar.vue';
export { default as MobileTabBar } from './navigation/MobileTabBar.vue';

// Dialog provider (programmatic dialog support)
export { default as DialogProvider } from './dialog/DialogProvider.vue';

// Re-export primitive components
export * from './primitive/ui';
