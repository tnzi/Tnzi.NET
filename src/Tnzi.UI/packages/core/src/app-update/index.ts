/**
 * @tnzi/core/app-update
 *
 * 发版后旧页面的自愈：懒加载分块失败时整页加载目标地址，发现新版本后在下一次
 * 路由跳转时切到新版本。与 UI 框架无关，`@tnzi/ui-admin`、`@tnzi/ui-ai` 的
 * `defineChatApp` 与 `@tnzi/mobile` 的插件默认启用；其它应用在入口调用一次
 * `installAppUpdate({ router })` 即可。
 */
export {
  installAppUpdate,
  isChunkLoadError,
  readShellFingerprint,
  DEFAULT_APP_UPDATE_INTERVAL_MS,
  RECOVERY_WINDOW_MS,
} from './app-update';
export type {
  AppUpdateMode,
  AppUpdateOptions,
  AppUpdateHandle,
  AppUpdateRoute,
  AppUpdateRouter,
} from './app-update';
