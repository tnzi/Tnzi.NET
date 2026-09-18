/**
 * `File[]` from a composer to `ContentPartDto[]` for the chat endpoint.
 *
 * Both built-in transports (`useChatThreads`, `createTnziChat`) send through
 * this. A composer collects files; the backend takes `ChatRequestDto.content`
 * as text / image / file parts, and the two shapes met nowhere - each transport
 * took `(content)` only, so a chip that vanished on Send was a file that never
 * left the browser.
 *
 * What the endpoint can carry:
 *   - **images** inline, as base64 (`ImageContentPartDto`);
 *   - **anything else** only by reference - `FileContentPartDto.fileId` is a
 *     Storage record id, so a non-image file has to be uploaded first. That
 *     upload is the consumer's call (`uploadFile`): which storage, which
 *     folder, public or not. Without it a non-image file is REFUSED, loudly,
 *     rather than sent as a name the model never sees.
 */
import type { ImageContentPartDto, FileContentPartDto, TextContentPartDto } from '@tnzi/core/services/ai';
import type { MessageAttachment } from './useChat';

/** The three shapes `ChatRequestDto.content` accepts, as a union. */
export type ChatContentPart = TextContentPartDto | ImageContentPartDto | FileContentPartDto;

/** A non-image file uploaded to storage; what `FileContentPartDto` references. */
export interface UploadedAttachment {
  id: string;
  fileName?: string | null;
}

export interface FilesToContentPartsOptions {
  /**
   * Upload a non-image file and return its storage id. Typically
   * `useStorageApi(http).upload` unwrapped. Throw to refuse.
   */
  uploadFile?: (file: File) => Promise<UploadedAttachment>;
  /** Per-file ceiling in bytes. Default 10 MB. */
  maxBytes?: number;
}

export interface ContentPartsResult {
  /** Request parts, or `null` for a text-only turn (send `message` alone). */
  parts: ChatContentPart[] | null;
  /**
   * What to send as `ChatRequestDto.message`: the trimmed text, or when the
   * user typed nothing, a line naming the attachments. The backend adds the
   * user turn to the model input and persists it ONLY when `message` is
   * non-blank; it never reads the parts on their own. A files-only turn sent
   * with a blank message is therefore an image the model never sees, under a
   * bubble that says it was sent.
   */
  message: string;
  /** The same attachments as the user row should render them. */
  attachments: MessageAttachment[];
}

/**
 * Raised when an attachment cannot be sent. `message` is user-facing: the
 * transports report it through their `onError` and leave the turn unsent.
 */
export class AttachmentRefusedError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'AttachmentRefusedError';
  }
}

const DEFAULT_MAX_BYTES = 10 * 1024 * 1024;

const isImage = (file: File): boolean => file.type.startsWith('image/');

/** The message a files-only turn is sent and persisted with; user-facing. */
export function describeAttachments(files: readonly File[]): string {
  return `[Attached: ${files.map((f) => f.name).join(', ')}]`;
}

function describeSize(bytes: number): string {
  return `${Math.max(1, Math.round(bytes / (1024 * 1024)))} MB`;
}

/** Base64 without a data-URL prefix; chunked so large images do not blow the call stack. */
async function toBase64(file: File): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  const CHUNK = 0x8000;
  for (let i = 0; i < bytes.length; i += CHUNK) {
    binary += String.fromCharCode(...bytes.subarray(i, i + CHUNK));
  }
  return btoa(binary);
}

/**
 * The parts and message for re-sending a user row as it was rendered
 * (`regenerate`): images carry their bytes in the attachment, files their
 * storage id, so nothing has to be read or uploaded again.
 */
export function attachmentsToContentParts(
  text: string,
  attachments: readonly MessageAttachment[],
): Pick<ContentPartsResult, 'parts' | 'message'> {
  if (attachments.length === 0) return { parts: null, message: text.trim() };
  const message = text.trim() || `[Attached: ${attachments.map((a) => a.fileName).join(', ')}]`;
  const parts: ChatContentPart[] = [{ type: 'text', text: message }];
  for (const attachment of attachments) {
    if (attachment.type === 'image') {
      parts.push({ type: 'image', base64Data: attachment.base64Data ?? '', mediaType: attachment.mediaType ?? '' });
    } else {
      parts.push({ type: 'file', fileId: attachment.fileId ?? '', fileName: attachment.fileName });
    }
  }
  return { parts, message };
}

export async function filesToContentParts(
  text: string,
  files: readonly File[],
  options: FilesToContentPartsOptions = {},
): Promise<ContentPartsResult> {
  if (files.length === 0) return { parts: null, message: text.trim(), attachments: [] };
  const maxBytes = options.maxBytes ?? DEFAULT_MAX_BYTES;

  // Validate everything before reading or uploading anything: a refusal must
  // leave no half-uploaded state behind.
  for (const file of files) {
    if (file.size > maxBytes) {
      throw new AttachmentRefusedError(`"${file.name}" is larger than ${describeSize(maxBytes)} and cannot be sent.`);
    }
    if (!isImage(file) && !options.uploadFile) {
      throw new AttachmentRefusedError(`"${file.name}" cannot be sent: only images can be attached here.`);
    }
  }

  // The text part always leads: on a files-only turn it is the attachment
  // names, so the model sees what was attached and a provider that rejects
  // image-only input still gets text.
  const message = text.trim() || describeAttachments(files);
  const parts: ChatContentPart[] = [{ type: 'text', text: message }];
  const attachments: MessageAttachment[] = [];

  for (const file of files) {
    if (isImage(file)) {
      const base64Data = await toBase64(file);
      const mediaType = file.type;
      parts.push({ type: 'image', base64Data, mediaType });
      attachments.push({ type: 'image', fileName: file.name, mediaType, base64Data });
      continue;
    }
    let uploaded: UploadedAttachment;
    try {
      uploaded = await options.uploadFile!(file);
    } catch (err) {
      throw new AttachmentRefusedError(err instanceof Error ? err.message : `"${file.name}" could not be uploaded.`);
    }
    const fileName = uploaded.fileName ?? file.name;
    parts.push({ type: 'file', fileId: uploaded.id, fileName });
    attachments.push({ type: 'file', fileId: uploaded.id, fileName });
  }

  return { parts, message, attachments };
}
