import type { FormSchemaItem } from '../_shared/form-schema'

/**
 * Column shape only - the page (`FunctionModules.vue`) builds its column array
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

/**
 * Create/edit form schema. The text/parent fields use page-supplied custom
 * renderers (`fm-text` / `fm-textarea` / `fm-parent` - see
 * `FunctionModules.vue` `fieldRenderers`) so they can: (a) lock when the row is
 * code-managed (`isSystemManaged`), and (b) carry the runtime-built, indented
 * parent-module options the static schema can't express. `order` is a plain
 * builtin number field.
 */
export const functionModuleFormSchema: FormSchemaItem[] = [
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'fm-text', required: true },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'fm-text', required: true },
  { key: 'parentId', labelKey: 'form.parentId', label: 'Parent', type: 'fm-parent' },
  { key: 'order', labelKey: 'form.order', label: 'Order', type: 'number', min: 0 },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'fm-textarea' },
]
