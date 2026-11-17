/**
 * The pictures that are browsed together in the full-size viewer: those of one message's
 * attachments, or those one tool call returned. A `TAttachmentImage` joins the group its nearest
 * ancestor provides (`provideImageGroup`), in the order they mount, and opens the viewer on its own
 * place in it; with no group around it, it is a set of one.
 */
import { inject, provide, shallowRef, type InjectionKey, type ShallowRef } from 'vue'

/** One picture as the viewer shows it: its source (null while it cannot be shown) and its name. */
export interface ViewerImage {
  src: string | null
  alt: string
}

/** A member of a group: read when the viewer opens, so a picture that loaded since is there. */
export interface ImageGroupMember {
  image: () => ViewerImage
}

export interface ImageGroup {
  readonly members: Readonly<ShallowRef<readonly ImageGroupMember[]>>
  /** Joins the group; returns the leave. */
  join: (member: ImageGroupMember) => () => void
}

const IMAGE_GROUP_KEY: InjectionKey<ImageGroup> = Symbol('tnzi-ai-image-group')

export function createImageGroup(): ImageGroup {
  const members = shallowRef<readonly ImageGroupMember[]>([])
  return {
    members,
    join(member) {
      members.value = [...members.value, member]
      return () => {
        members.value = members.value.filter((m) => m !== member)
      }
    },
  }
}

/** Makes the pictures rendered below this component one set in the viewer. */
export function provideImageGroup(): ImageGroup {
  const group = createImageGroup()
  provide(IMAGE_GROUP_KEY, group)
  return group
}

export function useImageGroup(): ImageGroup | null {
  return inject(IMAGE_GROUP_KEY, null)
}
