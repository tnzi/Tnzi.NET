interface ResolverResult { name: string; from: string }
interface Resolver { type: 'component'; resolve(name: string): ResolverResult | undefined }

const LAYOUT_COMPONENTS = new Set([
  'TAdminShell', 'TAdminSidebar', 'TAdminHeader', 'TAdminTabs',
  'TAdminBreadcrumb', 'TAdminFooter', 'TAdminContent',
  'TThemeDrawer', 'TGlobalSearch',
])
const CRUD_COMPONENTS = new Set([
  'TCrudPage', 'TCrudColumnSetting', 'TFormModal',
  'TListShell', 'TCardPage',
])
// Renderers live in the `crud/renderers/` subdirectory (different from-path).
const CRUD_RENDERER_COMPONENTS = new Set(['TCardRenderer', 'TTableRenderer'])
const FORM_COMPONENTS = new Set([
  'TPermissionMatrix', 'TMenuTree',
  'TDictSelector', 'TRoleSelector', 'TUserSelector', 'TTenantSelector',
])
const DATA_COMPONENTS = new Set(['TChunkFileUpload'])

export function TnziUiAdminResolver(): Resolver {
  return {
    type: 'component',
    resolve(name: string) {
      if (LAYOUT_COMPONENTS.has(name)) return { name, from: `@tnzi/ui-admin/components/layout/${name}.vue` }
      if (CRUD_COMPONENTS.has(name))   return { name, from: `@tnzi/ui-admin/components/crud/${name}.vue` }
      if (CRUD_RENDERER_COMPONENTS.has(name)) return { name, from: `@tnzi/ui-admin/components/crud/renderers/${name}.vue` }
      if (FORM_COMPONENTS.has(name))   return { name, from: `@tnzi/ui-admin/components/forms/${name}.vue` }
      if (DATA_COMPONENTS.has(name))   return { name, from: `@tnzi/ui-admin/components/data/${name}.vue` }
      return undefined
    },
  }
}
