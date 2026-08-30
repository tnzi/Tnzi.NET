/**
 * TYPE FIXTURE - compiled by `pnpm typecheck`, never executed.
 *
 * `TFileImage.objectFit` has to be a DECLARED prop, not a fallthrough attribute.
 * At runtime the fallthrough form happens to work (naive's `NImage` declares
 * `objectFit`, so it is extracted before `$attrs`), which is why in-tree call
 * sites wrote `object-fit="cover"` for a long time without anyone noticing the
 * gap. A consumer on `vueCompilerOptions.strictTemplates` cannot write it that
 * way - vue-tsc rejects any attribute that is not a declared prop - and this
 * repo does NOT turn strictTemplates on, so nothing here would ever go red.
 *
 * Assigning an object literal to the component's `$props` is the equivalent
 * check that works under our own settings: excess property checking rejects a
 * key that is not part of the declared props.
 *
 * ## Mutation verification
 *
 * Remove `objectFit` from `TFileImage`'s `defineProps` and `pnpm typecheck`
 * must report, for both constants below:
 *
 *   Object literal may only specify known properties, and 'objectFit' does not
 *   exist in type ...
 *
 * Verified 2026-08-27. A fixture that still compiles after the revert is
 * testing nothing.
 */
import TFileImage from '../../src/components/display/TFileImage.vue'

type FileImageProps = InstanceType<typeof TFileImage>['$props']

/** The consumer shape this exists for: a square gallery tile with a lightbox. */
export const coverTile: FileImageProps = {
  fileId: 'file-1',
  lightbox: true,
  objectFit: 'cover',
  // The lightbox branch puts fallthrough attrs on NImage's wrapper, so the
  // picture itself is sized through `imgProps`.
  imgProps: { style: { width: '100%', height: '100%' } },
}

/** And the plain-img branch, where `objectFit` lands on the `<img>` directly. */
export const containThumb: FileImageProps = {
  fileId: 'file-2',
  objectFit: 'contain',
}
