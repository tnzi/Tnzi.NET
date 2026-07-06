/**
 * Menu config — form schema + row type for `system.menus`.
 *
 * Aligned with backend `MenuInfoDto`:
 *   id / parentId / name / icon / path / component / sortOrder /
 *   isHidden / permission / type / creationTime / lastModificationTime
 *
 * The backend has NO `displayName` on the stored menu — that field only exists
 * on the generated `MenuInfoDto` padding and deserialises to undefined, so the
 * page renders `name` as the primary label instead.
 *
 * `type` is the `MenuType` enum, serialized by the backend's global
 * JsonStringEnumConverter as its member name: "Directory" | "Menu" | "Button".
 *
 * The table columns live inline in Menus.vue (they need the runtime menu tree
 * for the parent-name lookup); this file only owns the form schema + row type.
 */
import type { FormSchemaItem } from '../_shared/form-schema'

export interface MenuRow {
  id?: string
  parentId?: string | null
  name?: string
  path?: string
  component?: string
  icon?: string
  sortOrder?: number
  isHidden?: boolean
  permission?: string
  type?: string
}

export const menuFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  {
    key: 'type',
    labelKey: 'form.type', label: 'Type',
    type: 'select',
    required: true,
    // MenuType member names (JsonStringEnumConverter wire shape).
    options: [
      { label: 'Directory', value: 'Directory' },
      { label: 'Menu', value: 'Menu' },
      { label: 'Button', value: 'Button' },
    ],
  },
  // `menu-parent` is a page-supplied custom field renderer (filterable +
  // clearable parent-menu picker built from the runtime menu tree) — see
  // Menus.vue `fieldRenderers`. Falls back to a read-only value if unrendered.
  { key: 'parentId', labelKey: 'form.parentId', label: 'Parent', type: 'menu-parent' },
  { key: 'path', labelKey: 'form.path', label: 'Path', type: 'text' },
  { key: 'component', labelKey: 'form.component', label: 'Component', type: 'text' },
  // `icon` renders the shared TIconPicker via the admin form-schema renderer.
  { key: 'icon', labelKey: 'form.icon', label: 'Icon', type: 'icon' },
  { key: 'permission', labelKey: 'form.permission', label: 'Permission Code', type: 'text' },
  { key: 'sortOrder', labelKey: 'form.sortOrder', label: 'Sort', type: 'number', min: 0 },
  { key: 'isHidden', labelKey: 'form.isHidden', label: 'Hidden', type: 'switch' },
]
