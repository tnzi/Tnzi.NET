/**
 * 发版自愈在后台外壳里的接线。机制本身在 `@tnzi/core/app-update`，这里只补后台
 * 特有的两件事：路由器来自 `install()`，`prompt` 模式的询问框用后台的对话框与词典。
 */
import type { Router } from 'vue-router'
import { useDialog } from '@tnzi/core/adapters'
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

/** 后台内置的「有新版本」询问框。 */
export function confirmAdminAppUpdate(): Promise<boolean> {
  return useDialog().confirm(
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
