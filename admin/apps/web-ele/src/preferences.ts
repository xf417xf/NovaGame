import { defineOverridesPreferences } from '@vben/preferences';

/**
 * @description 项目配置文件
 * 只需要覆盖项目中的一部分配置，不需要的配置不用覆盖，会自动使用默认配置
 * !!! 更改配置后请清空缓存，否则可能不生效
 */
export const overridesPreferences = defineOverridesPreferences({
  app: {
    accessMode: 'frontend',
    contentCompact: 'wide',
    defaultAvatar: '',
    defaultHomePath: '/game-config',
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
    companyName: 'NovaGame',
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
    enable: true,
    persist: false,
    showIcon: true,
    showMaximize: false,
    showMore: false,
    showRefresh: true,
    styleType: 'plain',
    visitHistory: false,
  },
  theme: {
    colorPrimary: 'hsl(174 67% 32%)',
  },
});
