export { default as TPermissionMatrix } from './TPermissionMatrix.vue'
export { default as TMenuTree } from './TMenuTree.vue'
export { default as TDictSelector } from './TDictSelector.vue'
export { default as TRoleSelector } from './TRoleSelector.vue'
export { default as TUserSelector } from './TUserSelector.vue'
export { default as TTenantSelector } from './TTenantSelector.vue'
export { createSelectorComponent, type SelectorOption, type SelectorFetcher } from './_selector-factory'

// Form building blocks: a plain form, a search bar, the schema-driven form,
// image upload with cropping, captcha fields, and composite fields.
export { default as TForm } from './TForm.vue'
export { default as TSearchForm } from './TSearchForm.vue'
export { default as TSchemaForm } from './TSchemaForm'
export type {
  FormSchemaItem,
  FormSchemaOption,
  FormSchemaSection,
  FormSchemaFieldType,
  FormSchemaBuiltinFieldType,
  FieldRenderer,
  FieldRenderContext,
  ReadonlyLayout,
} from './TSchemaForm'
export { default as TImageUpload } from './TImageUpload.vue'
export { default as TImageCaptcha } from './TImageCaptcha.vue'
export { default as TCaptcha } from './TCaptcha.vue'
export * from './fields/index'
