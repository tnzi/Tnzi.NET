/**
 * File-name rules shared by every surface that saves a stored file locally.
 */

/**
 * Name for a saved copy of one file version: the version goes BEFORE the
 * extension (`report.pdf` → `report.v2.pdf`), so the operating system still
 * recognises the type. Mirrors the `Content-Disposition` name the backend's
 * `GET files/{id}/versions/{version}/download` sends (`DefaultStorageController.
 * VersionedFileName`, written out by hand for the same reason), so a blob
 * download and a direct download land under the same name.
 *
 * The rule, identical on both sides: only the segment after the last `/` or
 * `\` counts. The extension starts at that segment's last dot, unless the dot
 * opens the segment (a dotfile such as `.gitignore`) or ends the name
 * (`notes.`). Without an extension the version is appended.
 */
export function versionedFileName(fileName: string, version: number): string {
  const separator = Math.max(fileName.lastIndexOf('/'), fileName.lastIndexOf('\\'))
  const dot = fileName.lastIndexOf('.')
  const hasExtension = dot > separator + 1 && dot < fileName.length - 1
  return hasExtension
    ? `${fileName.slice(0, dot)}.v${version}${fileName.slice(dot)}`
    : `${fileName}.v${version}`
}
