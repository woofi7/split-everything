<script setup lang="ts">
import { t } from '@/i18n'
import { RouterLink } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
</script>

<template>
  <AppShell :title="t('Not found')" :back-to="{ name: 'dashboard' }" :back-label="t('Dashboard')">
    <div class="py-12 text-center">
      <p class="font-medium">{{ t('That page does not exist') }}</p>
      <RouterLink
        :to="{ name: 'dashboard' }"
        class="btn btn-press btn-primary mt-4"
      >{{ t('Back to your groups') }}
      </RouterLink>
    </div>
  </AppShell>
</template>
