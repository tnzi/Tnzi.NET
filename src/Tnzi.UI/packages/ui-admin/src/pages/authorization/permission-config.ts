import type { FormSchemaItem } from '../_shared/form-schema'

/**
 * Create/edit form schema for a permission point (ModuleFunction). The text
 * and category fields use page-supplied custom renderers (`perm-text` /
 * `perm-textarea` / `perm-category` — see `Permissions.vue` `fieldRenderers`)
 * so they can lock when the row is code-managed (`isSystemManaged`): the
 * backend rejects Code/ModuleId changes on system rows and always keeps
 * their code-declared Category, so editable fields there would only bounce
 * off the API (or be reverted by the seeder on next startup). `order` stays
 * a plain builtin number field — always editable.
 */
export const permissionFormSchema: FormSchemaItem[] = [
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'perm-text', required: true },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'perm-text', required: true },
  { key: 'category', labelKey: 'form.category', label: 'Category', type: 'perm-category' },
  { key: 'order', labelKey: 'form.order', label: 'Order', type: 'number', min: 0 },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'perm-textarea' },
]
