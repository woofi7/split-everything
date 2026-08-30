<script setup lang="ts">
import { computed } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { resolveIcon } from '@/domain/icons'
import { useGroupsStore } from '@/stores/groups'

/**
 * The mark in the corner of a screen that is about a group.
 *
 * The group's own icon and colour, because that is what tells two groups apart at
 * a glance and it is the same figure shown beside its name everywhere else. The
 * app's mark stands in when there is no group yet, so the corner is never empty
 * and no caller has to decide.
 */
const groups = useGroupsStore()

const group = computed(() => groups.mainGroup)
const icon = computed(() => resolveIcon(group.value?.iconName ?? null))
</script>

<template>
  <span
    v-if="group"
    data-testid="group-mark"
    class="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-white"
    :style="{ backgroundColor: group.colorHex || '#4f46e5' }"
    :title="group.name"
  >
    <FontAwesomeIcon :icon="icon.definition" class="h-4 w-4" aria-hidden="true" />
  </span>

  <img
    v-else
    src="/icons/icon.svg"
    alt=""
    width="32"
    height="32"
    data-testid="app-icon"
    class="mt-0.5 h-8 w-8 shrink-0 rounded-lg"
  />
</template>
