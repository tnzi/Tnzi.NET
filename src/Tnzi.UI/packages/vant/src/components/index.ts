/**
 * @tnzi/vant/components
 *
 * Vant-based component exports.
 */

// Core contract components (mobile implementations)
export { default as TLoginForm } from './auth/TLoginForm.vue';
export { default as TRegisterForm } from './auth/TRegisterForm.vue';
export { default as TPasswordReset } from './auth/TPasswordReset.vue';
export { default as TForm } from './form/TForm.vue';
export { default as TSearchForm } from './form/TSearchForm.vue';
export { default as TDynamicForm } from './form/TDynamicForm.vue';
export { default as TUserCard } from './card/TUserCard.vue';
export { default as TStatCard } from './card/TStatCard.vue';
export { default as TDataTable } from './table/TDataTable.vue';
export { default as TDataList } from './list/TDataList.vue';
export { default as TMenu } from './navigation/TMenu.vue';
export { default as TNavBar } from './navigation/TNavBar.vue';
export { default as TTabBar } from './navigation/TTabBar.vue';

// Representative raw Vant components (for advanced use cases)
export {
  Button as VButton,
  Cell as VCell,
  CellGroup as VCellGroup,
  Card as VCard,
  NavBar as VNavBar,
  Tabbar as VTabbar,
  TabbarItem as VTabbarItem,
} from 'vant';
