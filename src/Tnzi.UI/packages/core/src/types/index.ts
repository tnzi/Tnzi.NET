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

// Store Types
export type {
  AsyncStatus,
  AsyncState,
  PaginationState,
  FormFieldState,
  FormState,
  NotificationState,
  ModalState,
} from './stores';

export {
  initialAsyncState,
  loadingAsyncState,
  successAsyncState,
  errorAsyncState,
  initialPaginationState,
} from './stores';
