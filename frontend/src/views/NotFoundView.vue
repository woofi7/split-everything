<script setup lang="ts">
import { RouterLink } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
</script>

<template>
  <AppShell title="Not found" :back-to="{ name: 'dashboard' }" back-label="Dashboard">
    <div class="py-12 text-center">
      <p class="font-medium">That page does not exist</p>
      <RouterLink
        :to="{ name: 'dashboard' }"
        class="btn btn-press btn-primary mt-4"
      >
        Back to your groups
      </RouterLink>
    </div>
  </AppShell>
</template>
