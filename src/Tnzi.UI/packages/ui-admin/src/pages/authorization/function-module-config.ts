/**
 * Column shape only — the page (`FunctionModule.vue`) builds its column array
 * inline so it can inject the `parentId → parentName` lookup and the enabled
 * status renderer; this file is just the row type for now. Keep it small.
 */
export interface FunctionModuleRow {
  id?: string
  code?: string
  name?: string
  description?: string
  parentId?: string
  order?: number
  isEnabled?: boolean
  /**
   * Code-managed row (declared by an `IPermissionDefinitionProvider`).
   * UI gates: show "系统" badge in the columns list, disable Code/Name/
   * Description/ParentId in the edit form, hide Delete in More menu.
   * Only IsEnabled (+ Order) stays editable so ops can disable code-
   * declared permissions in an emergency without a redeploy.
   */
  isSystemManaged?: boolean
}
