import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

/**
 * RoleFunction page config — aligned with the canonical paged
 * GET /admin/role-functions endpoint (2026-04-14 Plan C unstub).
 *
 * Backend fields (RoleFunctionDto, denormalized on the function side):
 *   id, roleId, functionId, functionCode, functionName, moduleId,
 *   isEnabled, creationTime
 *
 * The page is read-only — mutation happens via the bulk assign/remove/set
 * endpoints (POST /admin/role-functions/role/{roleId}/assign etc.).
 */
export const roleFunctionColumns: ColumnDef[] = [
  { key: 'roleId',       title: 'Role' },
  { key: 'functionCode', title: 'Permission Code' },
  { key: 'functionName', title: 'Permission Name' },
  { key: 'moduleId',     title: 'Module', visible: false },
  { key: 'isEnabled',    title: 'Enabled' },
  { key: 'creationTime', title: 'Assigned At', visible: false },
]

export const roleFunctionFormSchema: FormSchemaItem[] = [
  { key: 'roleId',       label: 'Role',            type: 'text', required: true },
  { key: 'functionCode', label: 'Permission Code', type: 'text' },
  { key: 'functionName', label: 'Permission Name', type: 'text' },
]
