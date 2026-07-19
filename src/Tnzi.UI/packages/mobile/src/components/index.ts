/**
 * @tnzi/mobile/components
 *
 * Vant-based component exports.
 */

// 核心契约组件(移动端实现),业务组件统一 T 前缀
export { default as TLoginForm } from './auth/TLoginForm.vue';
export { default as TRegisterForm } from './auth/TRegisterForm.vue';
export { default as TPasswordReset } from './auth/TPasswordReset.vue';
export { default as TForm } from './form/TForm.vue';
export { default as TSearchBar } from './form/TSearchBar.vue';
export { default as TDynamicForm } from './form/TDynamicForm.vue';
export { default as TUserCard } from './card/TUserCard.vue';
export { default as TStatCard } from './card/TStatCard.vue';
export { default as TDataList } from './list/TDataList.vue';
export { default as TSwipeCell } from './list/TSwipeCell.vue';
export { default as TMenu } from './navigation/TMenu.vue';
export { default as TNavBar } from './navigation/TNavBar.vue';
export { default as TTabBar } from './navigation/TTabBar.vue';

// 有代表性的 Vant 原生组件别名(供高级用例直接使用,保留 V 前缀透传)
export {
  Button as VButton,
  Cell as VCell,
  CellGroup as VCellGroup,
  Card as VCard,
  NavBar as VNavBar,
  Tabbar as VTabbar,
  TabbarItem as VTabbarItem,
} from 'vant';
