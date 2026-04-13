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
export type {
  ThemeMode,
  ColorFormat,
  RgbColor,
  HslColor,
  ThemeColors,
  ThemeConfig,
} from './theme';

export {
  lightThemeColors,
  darkThemeColors,
  defaultThemeConfig,
} from './theme';

// Async State Types
export type {
  AsyncStatus,
  AsyncState,
} from './async';

export {
  initialAsyncState,
  loadingAsyncState,
  successAsyncState,
  errorAsyncState,
} from './async';

// Re-export moved types for backward compatibility
export type { PaginationState } from '../headless/pagination';
export { initialPaginationState } from '../headless/pagination';
export type { FormFieldState, FormState } from '../headless/form';
export type { NotificationState, ModalState } from '../state/types/app';
