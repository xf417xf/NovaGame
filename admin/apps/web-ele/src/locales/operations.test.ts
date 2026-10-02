import { flushPromises, mount } from '@vue/test-utils';
import { createApp, defineComponent, h, nextTick } from 'vue';

import { i18n, loadLocaleMessages } from '@vben/locales';
import { preferences, updatePreferences } from '@vben/preferences';

import { ElDropdown } from 'element-plus';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NovaGameApiError } from '#/api/api-result';
import OperationsLanguageToggle from '#/layouts/operations-language-toggle.vue';
import adminRoutes from '#/router/routes/modules/admin-access';
import configRoutes from '#/router/routes/modules/game-config';
import DraftEditorDrawer from '#/views/game-config/components/draft-editor-drawer.vue';

import { setupI18n } from './index';
import english from './langs/en-US/operations.json';
import chinese from './langs/zh-CN/operations.json';
import {
  operationsCodeMessage,
  operationsErrorMessage,
  operationsPermissionText,
  tOperations,
  useOperationsMessage,
} from './operations';

describe('operations localization', () => {
  beforeEach(async () => {
    updatePreferences({ app: { enablePreferences: false, locale: 'en-US' } });
    await setupI18n(createApp(defineComponent({ render: () => h('div') })), {
      defaultLocale: 'en-US',
      missingWarn: false,
    });
  });

  afterEach(async () => {
    updatePreferences({ app: { locale: 'en-US' } });
    await loadLocaleMessages('en-US');
  });

  it('keeps both catalogs complete with matching interpolation parameters', async () => {
    expect(Object.keys(chinese).toSorted()).toEqual(
      Object.keys(english).toSorted(),
    );
    await loadLocaleMessages('zh-CN');
    for (const [key, text] of Object.entries(english)) {
      const translation = chinese[key as keyof typeof chinese];
      expect(translation.trim()).not.toBe('');
      expect(translation.match(/\{\w+\}/g)?.toSorted() ?? []).toEqual(
        text.match(/\{\w+\}/g)?.toSorted() ?? [],
      );
      expect(i18n.global.te(`operations.${key}`)).toBe(true);
      expect(
        tOperations(key as keyof typeof english, {
          value0: '123',
          value1: '456',
        }),
      ).not.toBe(`operations.${key}`);
    }
  });

  it('switches the real control even when preferences are disabled', async () => {
    const wrapper = mount(OperationsLanguageToggle, {
      attachTo: document.body,
      global: { plugins: [i18n] },
    });
    try {
      expect(preferences.app.enablePreferences).toBe(false);
      expect(wrapper.get('button').attributes('aria-label')).toBe('Language');
      wrapper.getComponent(ElDropdown).vm.$emit('command', 'zh-CN');
      await flushPromises();
      await nextTick();
      await vi.waitFor(() => expect(preferences.app.locale).toBe('zh-CN'));
      await nextTick();
      expect(wrapper.get('button').attributes('aria-label')).toBe('语言');
      expect(document.documentElement.lang).toBe('zh-CN');
      wrapper.getComponent(ElDropdown).vm.$emit('command', 'en-US');
      await flushPromises();
      await nextTick();
      await vi.waitFor(() =>
        expect(wrapper.get('button').text()).toContain('Language'),
      );
    } finally {
      wrapper.unmount();
    }
  });

  it('translates all navigation titles and retained feedback', async () => {
    const feedback = useOperationsMessage();
    feedback.value = () =>
      tOperations('passwordMustContainParameterToParameterCharacters', {
        value0: 12,
        value1: 256,
      });
    expect(feedback.value).toBe('Password must contain 12 to 256 characters.');
    const routes = [
      ...adminRoutes,
      ...(adminRoutes[0]?.children ?? []),
      ...configRoutes,
    ];
    const titles = routes.map((route) => String(route.meta?.title));
    expect(titles.every((title) => i18n.global.te(title))).toBe(true);
    await loadLocaleMessages('zh-CN');
    expect(feedback.value).toBe('密码长度必须为 12 至 256 个字符。');
    expect(
      titles.every((title) => /[\u4E00-\u9FFF]/.test(i18n.global.t(title))),
    ).toBe(true);
    await loadLocaleMessages('en-US');
    expect(feedback.value).toBe('Password must contain 12 to 256 characters.');
  });

  it('preserves an open editor and updates its JSON diagnostics on locale change', async () => {
    const wrapper = mount(DraftEditorDrawer, {
      attachTo: document.body,
      // The base-version select is outside the editor preservation contract.
      global: { plugins: [i18n], stubs: { ElSelect: true, teleport: true } },
      props: {
        canManage: true,
        configurationSet: 'default',
        draftId: null,
        modelValue: true,
        versionOptions: [],
      },
    });
    try {
      await flushPromises();
      const name = wrapper.get('[data-testid="draft-name"]');
      const content = wrapper.get('[data-testid="draft-content"]');
      await name.setValue('Unsubmitted custom name');
      await content.setValue('{ invalid');
      expect(wrapper.text()).toContain('JSON syntax: InvalidSymbol');
      await loadLocaleMessages('zh-CN');
      await nextTick();
      expect(wrapper.text()).toContain('JSON 语法：包含无效符号');
      expect((name.element as HTMLInputElement).value).toBe(
        'Unsubmitted custom name',
      );
      expect((content.element as HTMLTextAreaElement).value).toBe('{ invalid');
      expect(wrapper.emitted('update:modelValue')).toBeUndefined();
      await loadLocaleMessages('en-US');
      await nextTick();
      expect(wrapper.text()).toContain('JSON syntax: InvalidSymbol');
      expect((content.element as HTMLTextAreaElement).value).toBe('{ invalid');
    } finally {
      wrapper.unmount();
    }
  });

  it('translates known API codes and permissions while preserving unknown diagnostics', async () => {
    const error = new NovaGameApiError(
      'The role was not found.',
      'NovaGame.AdminAccess.RoleNotFound',
      404,
    );
    expect(operationsErrorMessage(error)).toBe(error.message);
    await loadLocaleMessages('zh-CN');
    expect(operationsErrorMessage(error)).toBe('角色不存在，请刷新列表。');
    expect(error.code).toBe('NovaGame.AdminAccess.RoleNotFound');
    expect(error.message).toBe('The role was not found.');
    expect(
      operationsPermissionText(
        'Operations.AdminAccess.Roles.Read',
        'title',
        'Read roles',
      ),
    ).toBe('查看管理员角色');
    expect(
      operationsPermissionText(
        'Custom.Permission',
        'title',
        'Custom permission',
      ),
    ).toBe('Custom permission');
    expect(operationsCodeMessage('Custom.Error', 'Original diagnostic')).toBe(
      'Original diagnostic',
    );
    expect(
      operationsCodeMessage(
        'NovaGame.GameConfig.Validation.DuplicateRecordId',
        'Duplicate record',
      ),
    ).toBe('记录 ID 重复。');
  });
});
