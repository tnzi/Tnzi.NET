/**
 * @tnzi/ui/components
 *
 * Component exports.
 */

// Auth components
export { default as TLoginForm } from './auth/TLoginForm.vue';
export { default as TRegisterForm } from './auth/TRegisterForm.vue';
export { default as TPasswordReset } from './auth/TPasswordReset.vue';

// Table components
export { default as TDataTable } from './table/TDataTable.vue';

// List components
export { default as TDataList } from './list/TDataList.vue';

// Form components
export { default as TForm } from './form/TForm.vue';
export { default as TDynamicForm } from './form/TDynamicForm.vue';
export { default as TSearchForm } from './form/TSearchForm.vue';

// Card components
export { default as TUserCard } from './card/TUserCard.vue';
export { default as TStatCard } from './card/TStatCard.vue';

// Layout components
export { default as TAdminLayout } from './layout/TAdminLayout.vue';
export { default as TSidebar } from './layout/TSidebar.vue';
export { default as THeader } from './layout/THeader.vue';
export { default as TBreadcrumb } from './layout/TBreadcrumb.vue';

// Navigation components
export { default as TMenu } from './navigation/TMenu.vue';
export { default as TNavBar } from './navigation/TNavBar.vue';
export { default as TTabBar } from './navigation/TTabBar.vue';

// Dialog provider (programmatic dialog support)
export { default as TDialogProvider } from './dialog/TDialogProvider.vue';

// Re-export primitive components (shadcn-vue)
export * from './primitive/ui';
