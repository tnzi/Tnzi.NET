import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Personas page config (sibling of Personas.vue).
 *
 * Persona shape derives from @tnzi/core/services/ai AgentPersonaDto:
 *   { id, name, slug, content, description, scope (ResourceScope: System=0 shared / Tenant=1 private) }
 * `content` IS the soul/system-prompt body injected as a `<soul>` block by the
 * backend ContextInjectionMiddleware. Personas render as a TCardPage grid; the
 * card + detail drawer own presentation, so there are no table columns — only
 * the create/edit form schema + the keyword search field.
 */
export const personaFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  {
    key: 'slug',
    labelKey: 'form.slug',
    label: 'Slug',
    type: 'text',
    required: true,
    placeholder: 'lower-kebab-case, unique within tenant',
  },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'content',
    labelKey: 'form.content',
    label: 'Persona Content',
    type: 'textarea',
    required: true,
    placeholder: 'The soul/system-prompt body injected as a <soul> block before every conversation.',
  },
  {
    key: 'scope',
    labelKey: 'form.scope',
    label: 'Scope',
    type: 'select',
    placeholder: 'Inferred from session when omitted (tenant -> Tenant, host -> System)',
    options: [
      { label: 'System (shared)', value: 'System' },
      { label: 'Tenant (private)', value: 'Tenant' },
    ],
  },
]

/**
 * Normalise a persona/provider `scope` to its canonical ResourceScope member
 * name. The backend serialises the enum as the PascalCase member name
 * (JsonStringEnumConverter); a numeric ordinal is still accepted for backward
 * compatibility. Tenant is the safe default (private, not the shared System row).
 */
export function resourceScopeName(scope: unknown): 'System' | 'Tenant' | 'User' {
  const map: Record<string, 'System' | 'Tenant' | 'User'> = {
    '0': 'System',
    '1': 'Tenant',
    '2': 'User',
    System: 'System',
    Tenant: 'Tenant',
    User: 'User',
  }
  return map[String(scope)] ?? 'Tenant'
}
