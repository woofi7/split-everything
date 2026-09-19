<script setup lang="ts">
import { computed, ref } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import GroupPicker from '@/components/groups/GroupPicker.vue'
import { resolveIcon } from '@/domain/icons'
import { groupColor } from '@/domain/themes'
import { useGroupsStore } from '@/stores/groups'

const groups = useGroupsStore()

const isPicking = ref(false)

const group = computed(() => groups.mainGroup)
const icon = computed(() => resolveIcon(group.value?.iconName ?? null))
</script>
<template>
  <button
    v-if="group"
    type="button"
    data-testid="group-mark"
    class="btn-press mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-white"
    :style="{ backgroundColor: groupColor(group) }"
    :aria-label="`Group: ${group.name}. Change group`"
    :title="`${group.name} - change group`"
    aria-haspopup="dialog"
    @click="isPicking = true"
  >
    <FontAwesomeIcon :icon="icon.definition" class="h-4 w-4" aria-hidden="true" />
  </button>
  <img
    v-else
    src="/icons/icon.svg"
    alt=""
    width="32"
    height="32"
    data-testid="app-icon"
    class="mt-0.5 h-8 w-8 shrink-0 rounded-lg"
  />
  <GroupPicker :open="isPicking" @close="isPicking = false" />
</template>
