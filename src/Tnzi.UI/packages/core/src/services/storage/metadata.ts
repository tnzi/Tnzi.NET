/**
 * Storage Module Metadata
 */

export enum StorageProviderType {
  Local = 'local',
  S3 = 's3',
  AzureBlob = 'azure',
  R2 = 'r2',
}

export function getStorageProviderLabel(type: StorageProviderType): string {
  switch (type) {
    case StorageProviderType.Local:
      return 'Local';
    case StorageProviderType.S3:
      return 'Amazon S3';
    case StorageProviderType.AzureBlob:
      return 'Azure Blob';
    case StorageProviderType.R2:
      return 'Cloudflare R2';
    default:
      return 'Unknown';
  }
}

export enum FileOperation {
  Upload = 'upload',
  Download = 'download',
  Delete = 'delete',
  Move = 'move',
  Copy = 'copy',
}
