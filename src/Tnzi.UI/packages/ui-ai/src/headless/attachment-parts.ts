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
 *   - **PDFs** inline, as base64 (`DocumentContentPartDto`): an agent bound to an
 *     external CLI runtime receives them as document blocks, the built-in engine
 *     hands them to the provider as inline data;
 *   - **anything else** only by reference - `FileContentPartDto.fileId` is a
 *     Storage record id, so a non-image file has to be uploaded first. That
 *     upload is the consumer's call (`uploadFile`): which storage, which
 *     folder, public or not. Without it a non-image file is REFUSED, loudly,
 *     rather than sent as a name the model never sees.
 *
 * The framework's default backend resolver does NOT load file contents: a
 * turn carrying a `file` part fails with 501 (`AI_UNSUPPORTED_MEDIA_TYPE`)
 * instead of handing the model a file name it cannot read. Pass `uploadFile`
 * only to a backend whose `IAgentResolver` resolves file references; against
 * the default backend, leave it out so non-image files are refused here,
 * before anything is uploaded.
 */
import type {
  DocumentContentPartDto,
  ImageContentPartDto,
  FileContentPartDto,
  TextContentPartDto,
} from '@tnzi/core/services/ai';
import type { MessageAttachment } from './useChat';

/** The shapes `ChatRequestDto.content` accepts, as a union. */
export type ChatContentPart = TextContentPartDto | ImageContentPartDto | DocumentContentPartDto | FileContentPartDto;

/** A non-image file uploaded to storage; what `FileContentPartDto` references. */
export interface UploadedAttachment {
  id: string;
  fileName?: string | null;
}

export interface FilesToContentPartsOptions {
  /**
   * Upload a non-image file and return its storage id. Typically
   * `useStorageApi(http).upload` unwrapped. Throw to refuse. Only useful
   * against a backend that resolves file references: the default one answers
   * a `file` part with 501.
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

/** Why an attachment could not be sent. */
export type AttachmentRefusalReason = 'too-large' | 'unsupported-type' | 'upload-failed';

export interface AttachmentRefusedErrorOptions {
  reason?: AttachmentRefusalReason;
  /** The file it was about. */
  fileName?: string;
  /** What `uploadFile` threw, when the upload failed (an `HttpError` carries the server's error code). */
  cause?: unknown;
}

/**
 * Raised when an attachment cannot be sent. `message` is user-facing: the
 * transports report it through their `onError` and leave the turn unsent.
 * `reason` and `fileName` say which refusal it was, so a product can word it
 * in its own copy; an upload that failed keeps what was thrown as `cause`.
 */
export class AttachmentRefusedError extends Error {
  readonly reason: AttachmentRefusalReason | null;
  readonly fileName: string | null;

  constructor(message: string, options: AttachmentRefusedErrorOptions = {}) {
    super(message, options.cause === undefined ? undefined : { cause: options.cause });
    this.name = 'AttachmentRefusedError';
    this.reason = options.reason ?? null;
    this.fileName = options.fileName ?? null;
  }
}

const DEFAULT_MAX_BYTES = 10 * 1024 * 1024;

const isImage = (file: File): boolean => file.type.startsWith('image/');
const isPdf = (file: File): boolean => file.type === 'application/pdf' || /\.pdf$/i.test(file.name);

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
      parts.push({
        type: 'image',
        base64Data: attachment.base64Data ?? '',
        mediaType: attachment.mediaType ?? '',
        ...(attachment.fileName ? { fileName: attachment.fileName } : {}),
      });
    } else if (attachment.base64Data) {
      parts.push({ type: 'document', base64Data: attachment.base64Data, mediaType: attachment.mediaType, fileName: attachment.fileName });
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
      throw new AttachmentRefusedError(`"${file.name}" is larger than ${describeSize(maxBytes)} and cannot be sent.`, {
        reason: 'too-large',
        fileName: file.name,
      });
    }
    if (!isImage(file) && !isPdf(file) && !options.uploadFile) {
      throw new AttachmentRefusedError(`"${file.name}" cannot be sent: only images and PDFs can be attached here.`, {
        reason: 'unsupported-type',
        fileName: file.name,
      });
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
      parts.push({ type: 'image', base64Data, mediaType, fileName: file.name });
      attachments.push({ type: 'image', fileName: file.name, mediaType, base64Data, size: file.size });
      continue;
    }
    if (isPdf(file)) {
      const base64Data = await toBase64(file);
      const mediaType = 'application/pdf';
      parts.push({ type: 'document', base64Data, mediaType, fileName: file.name });
      attachments.push({ type: 'file', fileName: file.name, mediaType, base64Data, size: file.size });
      continue;
    }
    let uploaded: UploadedAttachment;
    try {
      uploaded = await options.uploadFile!(file);
    } catch (err) {
      throw new AttachmentRefusedError(err instanceof Error ? err.message : `"${file.name}" could not be uploaded.`, {
        reason: 'upload-failed',
        fileName: file.name,
        cause: err,
      });
    }
    const fileName = uploaded.fileName ?? file.name;
    parts.push({ type: 'file', fileId: uploaded.id, fileName });
    attachments.push({ type: 'file', fileId: uploaded.id, fileName });
  }

  return { parts, message, attachments };
}
