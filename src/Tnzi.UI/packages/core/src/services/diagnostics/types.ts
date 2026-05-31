/**
 * Diagnostics Module Types — mirrors `Tnzi.AspNetCore.Dtos.*` on the .NET side.
 *
 * Models the read-only-ish diagnostics surface exposed by
 * `Tnzi.AspNetCore/Controllers/DefaultDiagnosticsAdminController`
 * (`/admin/diagnostics/*`). Bundled with every HostingModule app, so this
 * contract works on any stack that loads `Tnzi.AspNetCore`.
 */

/** Mirror of Tnzi.AspNetCore.Dtos.ExceptionTypeCountDto (top-exceptions ranking row). */
export interface ExceptionTypeCountDto {
  exceptionType: string;
  count: number;
}

/** Mirror of Tnzi.AspNetCore.Dtos.ExceptionSummaryDto. */
export interface ExceptionSummaryDto {
  totalCount: number;
  /** Counts grouped by exception type (C# Dictionary<string,int>). */
  byType: Record<string, number>;
  /** Counts grouped by HTTP status code (C# Dictionary<int,int> — keys serialize as strings). */
  byStatusCode: Record<string, number>;
  /** Counts grouped by business error code (C# Dictionary<string,int>). */
  byErrorCode: Record<string, number>;
  /** Top N exceptions ranked by occurrence count. */
  topExceptions: ExceptionTypeCountDto[];
  /** Most recent exceptions captured in the ring buffer. */
  recentExceptions: ExceptionEntryDto[];
  /** Start of the statistics time window (UTC, ISO 8601). */
  since: string;
}

/** Mirror of Tnzi.AspNetCore.Dtos.ExceptionEntryDto. */
export interface ExceptionEntryDto {
  exceptionType: string;
  message?: string | null;
  statusCode?: number | null;
  errorCode?: string | null;
  requestId?: string | null;
  /** Timestamp when the exception occurred (UTC, ISO 8601). */
  timestamp: string;
}

/** Single controller row inside a `ControllerDiagnosticsResultDto`. */
export interface ControllerInfoDto {
  type: string;
  route: string;
  module: string;
  isDefault: boolean;
  methods: string[];
}

/** Mirror of Tnzi.AspNetCore.Dtos.ControllerDiagnosticsResultDto. */
export interface ControllerDiagnosticsResultDto {
  totalCount: number;
  controllers: ControllerInfoDto[];
}

/** Mirror of Tnzi.AspNetCore.Dtos.ModuleDiagnosticsDto. */
export interface ModuleDiagnosticsDto {
  type: string;
  assembly: string;
  isEnabled: boolean;
  initializationState: string;
  dependencyCount: number;
  manifest: {
    serviceCount: number;
    controllers: string[];
    events: string[];
    backgroundTasks: string[];
    options: string[];
  };
}
