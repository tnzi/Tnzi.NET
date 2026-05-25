/**
 * Device-info helpers re-export.
 *
 * Sunk in 0.2.x:
 *   - Pure UA parsing → `@tnzi/core/utils/device-parser`
 *   - Icon/colour mapping (brand-hex per OS family) → `@tnzi/ui/utils/device-icon`
 *
 * Admin call-sites keep their existing import path through this
 * barrel; new code should import directly from the lower packages.
 */
export { parseDeviceInfo } from '@tnzi/core'
export type { DeviceProfile, DeviceOsFamily } from '@tnzi/core'
export { deviceIconColor, DEFAULT_DEVICE_BRAND_COLORS } from '@tnzi/ui'
