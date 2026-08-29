<script setup lang="ts">
import { RouterLink } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
</script>

<template>
  <AppShell title="Not found" :back-to="{ name: 'groups' }" back-label="Groups">
    <div class="py-12 text-center">
      <p class="font-medium">That page does not exist</p>
      <RouterLink
        :to="{ name: 'groups' }"
        class="tap-target mt-4 inline-flex items-center rounded-lg bg-brand-600 px-4 text-sm font-medium text-white"
      >
        Back to your groups
      </RouterLink>
    </div>
  </AppShell>
</template>
