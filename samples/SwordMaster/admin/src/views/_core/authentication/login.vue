<script lang="ts" setup>
import type { FormInstance, FormRules } from 'element-plus';

import { reactive, ref } from 'vue';

import { ElButton, ElForm, ElFormItem, ElInput } from 'element-plus';

import { useAuthStore } from '#/store';

defineOptions({ name: 'Login' });

const authStore = useAuthStore();
const formRef = ref<FormInstance>();
const credentials = reactive({
  password: '',
  username: '',
});
const rules: FormRules<typeof credentials> = {
  password: [
    { message: 'Password is required.', required: true, trigger: 'blur' },
  ],
  username: [
    { message: 'Username is required.', required: true, trigger: 'blur' },
  ],
};

async function submit() {
  const valid = await formRef.value?.validate().catch(() => false);
  if (!valid) {
    return;
  }

  await authStore.authLogin({ ...credentials });
}
</script>

<template>
  <ElForm
    ref="formRef"
    class="login-form"
    :model="credentials"
    :rules="rules"
    label-position="top"
    @submit.prevent="submit"
  >
    <header class="login-form__header">
      <h1>Sign in</h1>
      <p>SwordMaster administrator access</p>
    </header>

    <ElFormItem label="Username" prop="username">
      <ElInput
        v-model="credentials.username"
        autocomplete="username"
        data-testid="login-username"
        name="username"
      />
    </ElFormItem>
    <ElFormItem label="Password" prop="password">
      <ElInput
        v-model="credentials.password"
        autocomplete="current-password"
        data-testid="login-password"
        name="password"
        show-password
        type="password"
      />
    </ElFormItem>
    <ElButton
      class="login-form__submit"
      data-testid="login-submit"
      :loading="authStore.loginLoading"
      native-type="submit"
      type="primary"
    >
      Sign in
    </ElButton>
  </ElForm>
</template>

<style scoped>
.login-form {
  width: min(100%, 360px);
}

.login-form__header {
  margin-bottom: 24px;
}

.login-form__header h1 {
  margin: 0;
  font-size: 24px;
  font-weight: 650;
  letter-spacing: 0;
  line-height: 32px;
}

.login-form__header p {
  margin: 6px 0 0;
  color: hsl(var(--muted-foreground));
  font-size: 14px;
  letter-spacing: 0;
}

.login-form__submit {
  width: 100%;
  min-height: 40px;
  margin-top: 4px;
}
</style>
