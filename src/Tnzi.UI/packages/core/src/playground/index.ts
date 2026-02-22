/**
 * @tnzi/core/playground
 *
 * 共享 playground 基础设施 - demo 数据、导航菜单、主题预设、主题工具。
 */

// Demo data
export {
  demoMenuItems,
  demoTabs,
  demoBreadcrumbs,
  demoTableData,
  demoTableColumns,
  demoTableActions,
  demoBasicForm,
  demoBasicFormRules,
  demoDynamicFormFields,
  demoSearchForm,
  demoUsers,
  demoStatCards,
  demoMockUser,
} from './demo-data';
export type { DemoProduct } from './demo-data';

// Demo menu
export {
  playgroundSections,
  getSectionsForPlatform,
} from './demo-menu';
export type { PlaygroundSection } from './demo-menu';

// Theme presets
export {
  themePresets,
  getPreset,
  fontFamilyOptions,
} from './theme-presets';
export type { ThemePreset } from './theme-presets';

// Theme utils
export {
  applyThemeConfigToDOM,
  clearThemeConfigFromDOM,
  resolveThemeMode,
  getDefaultColorsForMode,
  getDefaultThemeConfig,
  hexToRgb,
} from './theme-utils';
