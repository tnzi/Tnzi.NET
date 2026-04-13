/**
 * @tnzi/mobile/components
 *
 * Vant-based component exports.
 */

// Core contract components (mobile implementations)
export { default as LoginForm } from './auth/LoginForm.vue';
export { default as RegisterForm } from './auth/RegisterForm.vue';
export { default as PasswordReset } from './auth/PasswordReset.vue';
export { default as Form } from './form/Form.vue';
export { default as SearchBar } from './form/SearchBar.vue';
export { default as DynamicForm } from './form/DynamicForm.vue';
export { default as UserCard } from './card/UserCard.vue';
export { default as StatCard } from './card/StatCard.vue';
export { default as DataList } from './list/DataList.vue';
export { default as SwipeCell } from './list/SwipeCell.vue';
export { default as Menu } from './navigation/Menu.vue';
export { default as MobileNavBar } from './navigation/MobileNavBar.vue';
export { default as MobileTabBar } from './navigation/MobileTabBar.vue';

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
