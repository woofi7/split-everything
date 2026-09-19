import { t } from '@/i18n'

export interface NavTab {
  name: string
  label: string
  to: { name: string }
  owns: string[]
  icon: string
}

export const navTabs: NavTab[] = [
  {
    name: 'dashboard',
    label: t('Dashboard'),
    to: { name: 'dashboard' },
    owns: ['dashboard', 'group'],
    icon: 'M4 6h16M4 12h16M4 18h10',
  },
  {
    name: 'activity',
    label: t('Activity'),
    to: { name: 'activity' },
    owns: ['activity'],
    icon: 'M12 8v4l3 2M3 12a9 9 0 1 0 18 0a9 9 0 0 0-18 0',
  },
  {
    name: 'stats',
    label: t('Stats'),
    to: { name: 'stats' },
    owns: ['stats'],
    icon: 'M4 20V10M10 20V4M16 20v-7M22 20H2',
  },
  {
    name: 'profile',
    label: t('Profile'),
    to: { name: 'profile' },
    owns: ['profile'],
    icon: 'M5 20a7 7 0 0 1 14 0M12 3a4 4 0 1 1 0 8a4 4 0 0 1 0-8',
  },
]
