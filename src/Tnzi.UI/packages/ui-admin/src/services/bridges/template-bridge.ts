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
 *   - templates.render(id, variables) → renders template content using preview endpoint.
 *     Backend has no ID-based render; this fetches the template by ID first, then calls
 *     preview with the content and provided variables.
 *   - templates.clone(id) → clones a template using the backend clone endpoint.
 *     Backend requires newName; bridge generates one as "{originalName}-copy".
 *
 * BACKEND GAPS SUMMARY:
 *   templates.render: backend has no GET /admin/templates/{id}/render endpoint;
 *     bridge fetches template by ID then calls POST /admin/templates/preview.
 *   templates.clone newName: backend clone requires newName param; bridge auto-generates
 *     "{templateName}-copy" since the UI does not prompt for a name.
 *   layouts.clone newName: same - bridge auto-generates "{layoutName}-copy".
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
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminTemplateApi>[0]

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
   * Render a template with given variables.
   * Fetches the template by ID, then calls the preview endpoint.
   * Returns the rendered content string.
   */
  render(id: string, variables: Record<string, unknown>): Promise<string>
  /**
   * Clone an existing template.
   * Backend requires newName; bridge auto-generates "{templateName}-copy".
   * Returns the cloned TemplateEntityDto.
   */
  clone(id: string): Promise<TemplateEntityDto>
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
    const result = unwrap<{ items: TemplateInfoDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
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
      return unwrap<TemplateInfoDto>(await ta.create(data as never))
    },
    update: async (id: string, data: Partial<TemplateInfoDto>) => {
      return unwrap<TemplateInfoDto>(await ta.update(id, data as never))
    },
    delete: async (ids: string[]) => {
      if (ids.length === 1 && ids[0]) {
        ensureOk(await ta.delete(ids[0]))
      } else {
        ensureOk(await ta.deleteBatch(ids))
      }
    },
    render: async (id: string, variables: Record<string, unknown>): Promise<string> => {
      // Backend has no ID-based render; fetch template content first, then preview
      const entity = unwrap<TemplateEntityDto>(await ta.getById(id))
      const rendered = unwrap<string>(
        await ta.preview({ content: entity.contentTemplate, model: variables }),
      )
      return rendered ?? ''
    },
    clone: async (id: string): Promise<TemplateEntityDto> => {
      // Backend clone requires newName; auto-generate based on original template name
      const entity = unwrap<TemplateEntityDto>(await ta.getById(id))
      const newName = `${entity.templateName}-copy`
      return unwrap<TemplateEntityDto>(await ta.clone(id, newName))
    },
  }

  // ---- layouts sub-contract ----

  async function fetchLayouts(query: CrudPageQuery): Promise<CrudPageResult<LayoutInfoDto>> {
    const params = { ...mapQueryToListRequest(query), includeFileSource: true } as unknown as LayoutQueryDto
    const result = unwrap<{ items: LayoutInfoDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
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
      return unwrap<LayoutInfoDto>(await la.create(data as never))
    },
    update: async (id: string, data: Partial<LayoutInfoDto>) => {
      return unwrap<LayoutInfoDto>(await la.update(id, data as never))
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
