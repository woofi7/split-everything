<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faGear } from '@fortawesome/free-solid-svg-icons'
import GroupPicker from '@/components/groups/GroupPicker.vue'
import { resolveIcon } from '@/domain/icons'
import { useGroupsStore } from '@/stores/groups'

/**
 * The two things that are about the group rather than about what is in it.
 *
 * Behind one icon, because neither is something you do often: changing group and
 * opening its settings were both competing for the corner with the page title.
 * Shared by every screen that is scoped to a group, so those screens agree on
 * where the group is changed rather than each offering its own way.
 */
const groups = useGroupsStore()

const isMenuOpen = ref(false)
const isPickingGroup = ref(false)

const group = () => groups.mainGroup

function closeMenu(): void {
  isMenuOpen.value = false
}

function openGroupPicker(): void {
  closeMenu()
  isPickingGroup.value = true
}

// Anywhere else, and Escape. A menu that only closes by choosing something is a
// trap on a phone, where there is no obvious way to dismiss it.
onMounted(() => {
  window.addEventListener('click', onDocumentClick, true)
  window.addEventListener('keydown', onKeydown)
})

onUnmounted(() => {
  window.removeEventListener('click', onDocumentClick, true)
  window.removeEventListener('keydown', onKeydown)
})

function onDocumentClick(event: MouseEvent): void {
  if (!isMenuOpen.value) return

  const target = event.target as HTMLElement | null
  if (target?.closest('[data-menu-root]')) return

  closeMenu()
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') closeMenu()
}
</script>

<template>
  <div data-menu-root class="relative">
    <button
      type="button"
      data-testid="group-menu"
      class="btn btn-press btn-secondary h-11 w-11 shrink-0 rounded-full px-0"
      style="border-color: var(--border)"
      aria-haspopup="menu"
      :aria-expanded="isMenuOpen"
      aria-label="Group options"
      title="Group options"
      @click="isMenuOpen = !isMenuOpen"
    >
      <FontAwesomeIcon :icon="faGear" class="h-4 w-4" />
    </button>

    <div
      v-if="isMenuOpen"
      data-testid="group-menu-items"
      role="menu"
      class="surface-card absolute right-0 z-40 mt-2 flex w-56 flex-col overflow-hidden p-1 shadow-lg"
    >
      <!-- Always here, even with one group: it is also the way to the next one. -->
      <button
        type="button"
        role="menuitem"
        data-testid="change-group"
        class="btn btn-press btn-quiet w-full justify-start gap-2 text-sm"
        @click="openGroupPicker"
      >
        <span
          class="flex h-5 w-5 items-center justify-center rounded-md text-white"
          :style="{ backgroundColor: group()?.colorHex ?? '#4f46e5' }"
          aria-hidden="true"
        >
          <FontAwesomeIcon :icon="resolveIcon(group()?.iconName ?? null).definition" class="h-3 w-3" />
        </span>
        Change group
      </button>

      <RouterLink
        v-if="group()"
        role="menuitem"
        data-testid="menu-group-settings"
        :to="{ name: 'group-settings', params: { groupId: group()!.id } }"
        class="btn btn-press btn-quiet w-full justify-start gap-2 text-sm"
        @click="closeMenu"
      >
        <span class="flex h-5 w-5 items-center justify-center" aria-hidden="true">
          <FontAwesomeIcon :icon="faGear" class="h-3 w-3" />
        </span>
        Group settings
      </RouterLink>
    </div>

    <GroupPicker :open="isPickingGroup" @close="isPickingGroup = false" />
  </div>
</template>
