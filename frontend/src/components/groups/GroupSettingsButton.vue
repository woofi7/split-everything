<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faGear } from '@fortawesome/free-solid-svg-icons'
import { useGroupsStore } from '@/stores/groups'

/**
 * The way into the group's settings, from any screen about that group.
 *
 * A gear that opens the settings, rather than a menu of two things. Changing
 * group moved to the mark on the left, which is the group's own icon and so was
 * already the thing on screen that stood for "which group": one press, one
 * destination, on both sides of the title.
 */
const groups = useGroupsStore()

const group = computed(() => groups.mainGroup)
</script>

<template>
  <RouterLink
    v-if="group"
    :to="{ name: 'group-settings', params: { groupId: group.id } }"
    data-testid="group-settings-link"
    class="btn btn-press btn-secondary h-11 w-11 shrink-0 rounded-full px-0"
    style="border-color: var(--border)"
    aria-label="Group settings"
    title="Group settings"
  >
    <FontAwesomeIcon :icon="faGear" class="h-4 w-4" />
  </RouterLink>
</template>
