/**
 * 发版自愈在后台外壳里的接线。机制本身在 `@tnzi/core/app-update`，这里只补后台
 * 特有的两件事：路由器来自 `install()`，`prompt` 模式的询问框用后台的对话框与词典。
 */
import type { Router } from 'vue-router'
import { createDialogAdapter } from '@tnzi/ui'
import {
  installAppUpdate,
  type AppUpdateHandle,
  type AppUpdateOptions,
} from '@tnzi/core/app-update'
import { translateChromeKey } from '../i18n/translate'

/**
 * `defineAdminApp({ appUpdate })` 的取值。默认启用：懒加载分块失败时整页加载目标地址，
 * 发现新版本后在下一次路由跳转时切过去。`false` 整个关闭。
 */
export type AdminAppUpdateConfig = false | Omit<AppUpdateOptions, 'router'>

/**
 * 后台内置的「有新版本」询问框。
 *
 * 直接用 `@tnzi/ui` 的对话框适配器而不是 core 的 `useDialog()`：后台外壳
 * （`createTnziUiAdmin` / `defineAdminApp` / `createAdminApp`）从不往 core 的对话框槽里
 * 装实现，那里在后台应用里通常是空的回退；而 `TAdminAppRoot` 总会发布
 * `window.$dialog`，这个适配器在调用时读它，没有时退回浏览器原生 `confirm`。
 * 要换成自己的询问方式，传 `appUpdate.prompt`。
 */
export function confirmAdminAppUpdate(): Promise<boolean> {
  return createDialogAdapter().confirm(
    translateChromeKey('admin.appUpdate.content', 'A new version of this application has been deployed. Reload now to use it?'),
    {
      title: translateChromeKey('admin.appUpdate.title', 'New version available'),
      confirmText: translateChromeKey('admin.appUpdate.reload', 'Reload'),
      cancelText: translateChromeKey('admin.appUpdate.later', 'Later'),
      type: 'info',
    },
  )
}

export function installAdminAppUpdate(config: AdminAppUpdateConfig | undefined, router?: Router): AppUpdateHandle | null {
  if (config === false) return null
  return installAppUpdate({
    prompt: confirmAdminAppUpdate,
    ...config,
    router,
  })
}
