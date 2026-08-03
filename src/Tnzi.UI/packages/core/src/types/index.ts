// Shared UI DTOs (re-exported for convenience via main entry)
export * from './shared-ui';

// API Types
export type {
  ApiResult,
  ApiResultEmpty,
  HttpMethod,
  RequestOptions,
  UploadProgressCallback,
  UploadOptions,
} from './api';

// Common Types
export type {
  Nullable,
  Optional,
  DeepPartial,
  DeepOptional,
  PickByType,
  OmitByType,
  StringLiteral,
  KeysOfType,
  RequireKeys,
  PartialKeys,
  Id,
  Timestamp,
  KeyValue,
  NameValue,
  SelectOption,
  TreeNode,
  Result,
  AsyncResult,
} from './common';

// Entity Types
export type {
  EntityKey,
  IEntity,
  CreationAuditedEntity,
  AuditedEntity,
  FullAuditedEntity,
  VersionedEntity,
  OrderedEntity,
  SoftDeletableEntity,
  TenantEntity,
} from './entities';

export {
  isCreationAudited,
  isAudited,
  isFullAudited,
} from './entities';

// Pagination Types
export type {
  PagedList,
  PagedQuery,
  PagedQueryDto,
  SortedPagedQueryDto,
  SearchPagedQueryDto,
  DateRangePagedQueryDto,
  FullPagedQueryDto,
  SortDirection,
} from './pagination';

export {
  createPagedQuery,
  updatePagedQuery,
  emptyPagedList,
  createPagedList,
} from './pagination';

// Theme Types
//
// Only the mode lives here. The colour model (`ThemeColors`, palettes, CSS
// vars) belongs to `@tnzi/ui`'s theme subsystem: core used to export its own
// 12-field `ThemeColors` plus `ThemeConfig` / `RgbColor` / `HslColor` /
// `ColorFormat` / light+dark colour tables, none of which had a single
// consumer, and whose `ThemeColors` collided by name with @tnzi/ui's real
// (5-role) one while being structurally incompatible with it.
export type { ThemeMode } from './theme';

export { normalizeThemeMode, THEME_MODES } from './theme';

// Re-export moved types for backward compatibility
export type { PaginationState } from '../headless/pagination';
export { initialPaginationState } from '../headless/pagination';
export type { NotificationState, ModalState } from '../state/types/app';
