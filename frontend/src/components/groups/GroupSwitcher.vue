<script setup lang="ts">
import { computed } from 'vue'
import { useGroupsStore } from '@/stores/groups'

/**
 * Which group the app is on.
 *
 * Most people have one group they use constantly and a few they barely touch, so
 * one group is the main one and the screens follow it rather than asking. This is
 * the way to change that answer.
 *
 * Hidden with a single group, where a chooser is only noise.
 */

const groups = useGroupsStore()

const choices = computed(() => groups.visibleGroups)

function choose(event: Event): void {
  groups.setMainGroup((event.target as HTMLSelectElement).value)
}
</script>

<template>
  <div v-if="choices.length > 1" class="flex items-center gap-2">
    <select
      :value="groups.mainGroupId ?? ''"
      aria-label="Which group the app is on"
      class="tap-target min-w-0 flex-1 rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
      style="border-color: var(--border)"
      @change="choose"
    >
      <option v-for="group in choices" :key="group.id" :value="group.id">
        {{ group.name }}
      </option>
    </select>
  </div>

  <p v-else-if="choices.length === 1" class="truncate text-sm text-[var(--text-muted)]">
    {{ choices[0].name }}
  </p>
</template>
