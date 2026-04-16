import type { CrudPageQuery, CrudPageResult } from '../headless/useCrudPage'
export type { CrudPageQuery, CrudPageResult }

export interface BridgeCrudContract<TDto, TCreateDto = Partial<TDto>, TUpdateDto = Partial<TDto>, TId = string> {
  fetch(query: CrudPageQuery): Promise<CrudPageResult<TDto>>
  create(data: TCreateDto): Promise<TDto>
  update(id: TId, data: TUpdateDto): Promise<TDto>
  delete(ids: TId[]): Promise<void>
  export?(query: CrudPageQuery): Promise<Blob>
  import?(file: File): Promise<void>
}
