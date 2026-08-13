import { defineOverridesPreferences } from '@vben/preferences';

export const overridesPreferences = defineOverridesPreferences({
  app: {
    accessMode: 'frontend',
    contentCompact: 'wide',
    defaultAvatar: '',
    defaultHomePath: '/player-equipment-grants',
    enableCheckUpdates: false,
    enableCopyPreferences: false,
    enablePreferences: false,
    enableRefreshToken: true,
    locale: 'en-US',
    loginExpiredMode: 'page',
    name: import.meta.env.VITE_APP_TITLE,
    watermark: false,
  },
  copyright: {
    companyName: 'SwordMaster',
    companySiteLink: '',
    date: '2026',
    enable: false,
    icp: '',
    icpLink: '',
    settingShow: false,
  },
  logo: {
    enable: false,
    source: '',
  },
  navigation: {
    accordion: true,
    split: false,
    styleType: 'plain',
  },
  shortcutKeys: {
    enable: true,
    globalLockScreen: false,
    globalLogout: true,
    globalPreferences: false,
    globalSearch: false,
  },
  sidebar: {
    collapsed: false,
    expandOnHover: false,
    width: 208,
  },
  tabbar: {
    draggable: false,
    enable: false,
    persist: false,
  },
  theme: {
    colorPrimary: 'hsl(174 67% 32%)',
  },
});
