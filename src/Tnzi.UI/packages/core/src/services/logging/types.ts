/**
 * Logging Module Types - mirrors `Tnzi.Logging.Dtos` on the .NET side.
 *
 * Models the on-disk Serilog file outputs surfaced by the admin
 * `LogFileService` (per-level rolling-day files under `LoggingOptions.BasePath`).
 * All endpoints are read-only - the log files are runtime artefacts
 * owned by Serilog (retention controlled by `RetainedFileCountLimit`).
 */

/**
 * Single log level summary - one of the per-level directories that
 * `LoggingModule` creates (Information / Warning / Error / Fatal / Debug).
 * Drives the left-side level navigator in the admin log viewer.
 */
export interface LogLevelInfoDto {
  /** Level name. */
  level: string;
  /** Whether the level's file sink is enabled in configuration. */
  isEnabled: boolean;
  /** Number of log files currently present under this level. */
  fileCount: number;
  /** Total bytes of all files under this level. */
  totalSize: number;
  /** UTC timestamp of the most recently modified file (null if no files). */
  lastModifiedUtc: string | null;
}

/**
 * Individual log file metadata for a single level.
 */
export interface LogFileInfoDto {
  /** Owning level. */
  level: string;
  /** File name (e.g. `log-20260518.txt`). */
  fileName: string;
  /** File size in bytes. */
  size: number;
  /** UTC last-write timestamp. */
  lastModifiedUtc: string;
  /** Parsed date if the file name follows the day-rolling convention. */
  rollingDate: string | null;
}

/**
 * Tail-read result - last N lines of a single log file.
 */
export interface LogTailResultDto {
  /** Resolved level. */
  level: string;
  /** Resolved file name. */
  fileName: string;
  /** Total file size in bytes (UI shows "last N of X bytes"). */
  totalSize: number;
  /** Lines returned (oldest first within the tail window). */
  lines: string[];
  /** True if the file was truncated to satisfy the line limit. */
  truncated: boolean;
}

/**
 * Single search-hit row in a `LogSearchResultDto`.
 */
export interface LogSearchHitDto {
  /** Level where this hit was found. */
  level: string;
  /** File where this hit lives. */
  fileName: string;
  /** 1-based line number inside the file. */
  lineNumber: number;
  /** The matching line content (verbatim). */
  line: string;
}

/**
 * Aggregate result of a keyword search across log files.
 */
export interface LogSearchResultDto {
  /** Levels that were actually scanned. */
  levels: string[];
  /** Start of the search window (inclusive, UTC) - null = open-ended. */
  fromUtc: string | null;
  /** End of the search window (inclusive, UTC) - null = open-ended. */
  toUtc: string | null;
  /** The keyword that was searched (verbatim - match is case-insensitive). */
  keyword: string;
  /** Matching lines, capped by the request's `maxResults` parameter. */
  hits: LogSearchHitDto[];
  /** True when result was truncated to satisfy the cap. */
  truncated: boolean;
  /** Total milliseconds spent scanning files server-side. */
  elapsedMs: number;
}

/**
 * Query parameters for `useAdminLogFileApi().search`.
 */
export interface LogSearchQueryDto {
  /** Keyword (required, case-insensitive). */
  keyword: string;
  /** Restrict to a single level (omit to search every enabled level). */
  level?: string;
  /** UTC lower bound on file modification time. */
  fromUtc?: string;
  /** UTC upper bound on file modification time. */
  toUtc?: string;
  /** Cap on returned hits (default 200, server clamps to `[1, 1000]`). */
  maxResults?: number;
}

/**
 * Query parameters for `useAdminLogFileApi().tail`.
 */
export interface LogTailQueryDto {
  /** Level directory (Information / Warning / Error / Fatal / Debug). */
  level: string;
  /** File name within that level. */
  file: string;
  /** How many lines from the end (default 500, clamped to `[1, 5000]`). */
  lines?: number;
}
