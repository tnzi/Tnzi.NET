/**
 * Template bridge - full implementation (Phase 3 Task 3.35).
 *
 * Adapts the template backend API to BridgeCrudContract shapes used by
 * TCrudPage-based template pages.
 *
 * Sub-contracts:
 *   - templates → useAdminTemplateApi (query, create, update, delete, preview, clone)
 *   - layouts   → useAdminLayoutApi   (query, create, update, delete, clone)
 *
 * Special contracts:
 *   - templates.render(template, variables) → renders template content using the preview
 *     endpoint. Backend has no ID-based render: a stored row's body is read by id first,
 *     a FileSystem-source row's body is the one the list row carries.
 *   - templates.clone(template) → copies a template as "{templateName}-copy". A stored row
 *     goes through the backend clone endpoint; a FileSystem-source row is stored as a
 *     new row through create (the clone endpoint copies a stored row, and there is none).
 *
 * Both take the list row or a stored row's id. A FileSystem-source row (a .cshtml
 * shipped with the binaries) has no stored id - the list reports the empty Guid -
 * so only the row itself can render or clone it. Both throw the server's reason
 * on a refusal (a body that does not compile, a copy name already taken).
 *
 * BACKEND GAPS SUMMARY:
 *   templates.render: backend has no GET /admin/templates/{id}/render endpoint;
 *     bridge reads the body then calls POST /admin/templates/preview.
 *   templates.clone newName: backend clone requires newName param; bridge auto-generates
 *     "{templateName}-copy" since the UI does not prompt for a name.
 */
import {
  useAdminTemplateApi,
  useAdminLayoutApi,
  type TemplateInfoDto,
  type TemplateEntityDto,
  type LayoutInfoDto,
  type TemplateQueryDto,
  type LayoutQueryDto,
} from '@tnzi/core/services/template'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapResult as unwrap, unwrapOk } from '../_mappers'

type HttpClient = Parameters<typeof useAdminTemplateApi>[0]

/**
 * A template list row, or a stored template's id. A FileSystem-source row has no
 * stored id (the list reports the empty Guid) and nothing behind one to fetch:
 * its body travels on the row, so pass the row to render or clone it.
 */
export type TemplateRef = string | TemplateInfoDto

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

/** The stored id a list row stands for, or `null` for a FileSystem-source row. */
function storedIdOf(row: TemplateInfoDto): string | null {
  const id = String(row.id ?? '')
  return id && id !== EMPTY_GUID ? id : null
}

export interface TemplateBridgeDeps {
  /** Production path: provide HttpClient; bridge builds all APIs internally. */
  client?: HttpClient
  /** Test path: inject mock APIs directly. */
  adminTemplateApi?: ReturnType<typeof useAdminTemplateApi>
  adminLayoutApi?: ReturnType<typeof useAdminLayoutApi>
}

/** templates sub-contract with render + clone actions */
export interface TemplateBridgeTemplateContract extends BridgeCrudContract<TemplateInfoDto> {
  /**
   * Hydrate a list row to its full TemplateEntityDto. Use this when a
   * page needs the heavy text fields (`subjectTemplate`/`contentTemplate`)
   * that paged-list responses strip for transport efficiency.
   */
  getById(id: string): Promise<TemplateEntityDto>
  /**
   * Render a template with given variables and return the rendered content.
   * A stored row's body is read by id; a FileSystem-source row renders the body
   * it carries. Throws the server's reason when the body cannot be read or does
   * not render.
   */
  render(template: TemplateRef, variables: Record<string, unknown>): Promise<string>
  /**
   * Copy a template as "{templateName}-copy" and return the new row. A
   * FileSystem-source row becomes a new stored row with its shipped body.
   * Throws the server's reason on a refusal (the copy name already taken).
   */
  clone(template: TemplateRef): Promise<TemplateEntityDto>
}

export interface TemplateBridge {
  templates: TemplateBridgeTemplateContract
  layouts: BridgeCrudContract<LayoutInfoDto>
}

const backendGapReject = (name: string) => (): Promise<never> =>
  Promise.reject(new Error(`template-bridge: ${name} - backend gap, no endpoint available`))

export function createTemplateBridge(deps: TemplateBridgeDeps = {}): TemplateBridge {
  const templateApi = deps.adminTemplateApi ?? (deps.client ? useAdminTemplateApi(deps.client) : null)
  const layoutApi = deps.adminLayoutApi ?? (deps.client ? useAdminLayoutApi(deps.client) : null)

  if (!templateApi || !layoutApi) {
    const noFetch = backendGapReject('no deps provided')
    return {
      templates: {
        fetch: noFetch as never,
        getById: backendGapReject('templates.getById'),
        create: backendGapReject('templates.create'),
        update: backendGapReject('templates.update'),
        delete: backendGapReject('templates.delete'),
        render: backendGapReject('templates.render'),
        clone: backendGapReject('templates.clone'),
      },
      layouts: {
        fetch: noFetch as never,
        create: backendGapReject('layouts.create'),
        update: backendGapReject('layouts.update'),
        delete: backendGapReject('layouts.delete'),
      },
    }
  }

  // Narrowed references for closures
  const ta = templateApi
  const la = layoutApi

  // ---- templates sub-contract ----

  async function fetchTemplates(query: CrudPageQuery): Promise<CrudPageResult<TemplateInfoDto>> {
    const params = { ...mapQueryToListRequest(query), includeFileSource: true } as unknown as TemplateQueryDto
    const result = unwrapOk<{ items: TemplateInfoDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
      await ta.getList(params),
    )
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const templates: TemplateBridgeTemplateContract = {
    fetch: fetchTemplates,
    /**
     * Hydrate a list row to its full TemplateEntityDto - surfaces the
     * heavy `subjectTemplate` / `contentTemplate` / `metadata` fields the
     * paged list endpoint strips. Pages use this on open-for-view/edit so
     * the form modal renders real template body, not just the metadata
     * shape returned by `fetch`.
     */
    getById: async (id: string): Promise<TemplateEntityDto> => {
      return unwrap<TemplateEntityDto>(await ta.getById(id))
    },
    create: async (data: Partial<TemplateInfoDto>) => {
      return unwrapOk<TemplateInfoDto>(await ta.create(data as never))
    },
    update: async (id: string, data: Partial<TemplateInfoDto>) => {
      return unwrapOk<TemplateInfoDto>(await ta.update(id, data as never))
    },
    delete: async (ids: string[]) => {
      if (ids.length === 1 && ids[0]) {
        ensureOk(await ta.delete(ids[0]))
      } else {
        ensureOk(await ta.deleteBatch(ids))
      }
    },
    render: async (template: TemplateRef, variables: Record<string, unknown>): Promise<string> => {
      // Backend has no ID-based render: read the body, then preview it. Both are
      // checked, so a missing row or a body that does not compile reports why
      // instead of reading as an empty preview.
      const storedId = typeof template === 'string' ? template : storedIdOf(template)
      const content = storedId
        ? unwrapOk<TemplateEntityDto>(await ta.getById(storedId)).contentTemplate
        : ((template as TemplateInfoDto).contentTemplate ?? '')
      const rendered = unwrapOk<string>(await ta.preview({ content, model: variables }))
      return rendered ?? ''
    },
    clone: async (template: TemplateRef): Promise<TemplateEntityDto> => {
      if (typeof template !== 'string' && !storedIdOf(template)) {
        // FileSystem-source row: the clone endpoint copies a stored row and there
        // is none, so store the shipped body as a new row, named and described
        // the way the endpoint names and describes its copies.
        return unwrapOk<TemplateEntityDto>(
          await ta.create({
            templateName: `${template.templateName}-copy`,
            module: template.module,
            category: template.category,
            subjectTemplate: template.subjectTemplate ?? '',
            contentTemplate: template.contentTemplate ?? '',
            defaultLayoutName: template.defaultLayoutName ?? undefined,
            type: template.type,
            isActive: template.isActive,
            description: `Cloned from '${template.templateName}'`,
          }),
        )
      }
      // Backend clone requires newName: the row's name, or the stored row's when
      // only an id was given.
      const id = typeof template === 'string' ? template : String(template.id)
      const name =
        typeof template === 'string'
          ? unwrapOk<TemplateEntityDto>(await ta.getById(id)).templateName
          : template.templateName
      return unwrapOk<TemplateEntityDto>(await ta.clone(id, `${name}-copy`))
    },
  }

  // ---- layouts sub-contract ----

  async function fetchLayouts(query: CrudPageQuery): Promise<CrudPageResult<LayoutInfoDto>> {
    const params = { ...mapQueryToListRequest(query), includeFileSource: true } as unknown as LayoutQueryDto
    const result = unwrapOk<{ items: LayoutInfoDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
      await la.getList(params),
    )
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const layouts: BridgeCrudContract<LayoutInfoDto> = {
    fetch: fetchLayouts,
    create: async (data: Partial<LayoutInfoDto>) => {
      return unwrapOk<LayoutInfoDto>(await la.create(data as never))
    },
    update: async (id: string, data: Partial<LayoutInfoDto>) => {
      return unwrapOk<LayoutInfoDto>(await la.update(id, data as never))
    },
    delete: async (ids: string[]) => {
      if (ids.length === 1 && ids[0]) {
        ensureOk(await la.delete(ids[0]))
      } else {
        ensureOk(await la.deleteBatch(ids))
      }
    },
  }

  return { templates, layouts }
}
