import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'

export interface SelectOption<V extends string | null = string | null> {
  value: V
  label: string
  hint?: string
  keywords?: readonly string[]
  icon?: IconDefinition
  iconColor?: string
  group?: string
  disabled?: boolean
}
