<script setup lang="ts">
import { t } from '@/i18n'
import { computed, nextTick, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { resolveIcon } from '@/domain/icons'
import { useGroupsStore } from '@/stores/groups'

/**
 * Which group the app is on.
 *
 * The app shows one group at a time, so this is the only place the others are
 * reachable, and it has to work with one group as well as ten: with one, it is
 * still how you get to creating the next.
 *
 * Archived groups are listed and marked rather than hidden. Archiving freezes a
 * group without deleting it, so its history is still worth reading.
 */

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ close: [] }>()

const groups = useGroupsStore()
const dialog = ref<HTMLElement | null>(null)

// The store's ordering, which puts anything outstanding first: a settled group
// needs no attention, and this list is where attention gets directed.
const active = computed(() => groups.groups.filter((group) => !group.isArchived)
  .slice()
  .sort((left, right) => {
    const leftSettled = Math.abs(left.myNetBalance) > 0.005 ? 0 : 1
    const rightSettled = Math.abs(right.myNetBalance) > 0.005 ? 0 : 1
    if (leftSettled !== rightSettled) return leftSettled - rightSettled
    return left.name.localeCompare(right.name)
  }))
const archived = computed(() => groups.groups.filter((group) => group.isArchived))

watch(
  () => props.open,
  async (open) => {
    if (!open) return
    await nextTick()
    dialog.value?.focus()
  },
  { immediate: true },
)

function choose(groupId: string): void {
  groups.setMainGroup(groupId)
  emit('close')

  // At the top, like any other page arrived at. Left alone the screen opens
  // wherever the last group was being read, which is nowhere in this one, and a
  // browser holding a scroll position through a wholesale change of content lands
  // further down still. After the new screen is laid out, or there is nothing to
  // scroll to yet.
  void nextTick(() => {
    const page = document.querySelector<HTMLElement>('[data-app-page]')
    if (page) page.scrollTop = 0
  })
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.open"
      class="fixed inset-0 z-50 flex items-end justify-center bg-black/60 p-0 sm:items-center sm:p-4"
      @click.self="emit('close')"
    >
      <div
        ref="dialog"
        role="dialog"
        aria-modal="true"
        :aria-label="t('Choose the group the app is on')"
        tabindex="-1"
        class="flex max-h-[85vh] w-full max-w-md flex-col rounded-t-2xl bg-[var(--surface-raised)] outline-none sm:rounded-2xl"
        @keydown.esc.prevent="emit('close')"
      >
        <div class="flex items-baseline justify-between gap-3 border-b p-4" style="border-color: var(--border)">
          <h2 class="text-base font-semibold">{{ t('Your groups') }}</h2>
          <button type="button" class="btn btn-press btn-quiet min-h-0 px-2 py-1 text-xs" @click="emit('close')">{{ t('Close') }}
          </button>
        </div>

        <ul class="min-h-0 flex-1 overflow-y-auto p-2">
          <li v-for="group in active" :key="group.id">
            <button
              type="button"
              data-testid="group-option"
              :aria-current="group.id === groups.mainGroupId ? 'true' : 'false'"
              class="btn btn-press w-full justify-start gap-3 border-transparent bg-transparent text-left"
              :class="group.id === groups.mainGroupId ? 'bg-brand-600/15' : ''"
              @click="choose(group.id)"
            >
              <span
                class="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-white"
                :style="{ backgroundColor: group.colorHex }"
                aria-hidden="true"
              >
                <FontAwesomeIcon :icon="resolveIcon(group.iconName).definition" class="h-4 w-4" />
              </span>

              <span class="min-w-0 flex-1">
                <span class="block truncate">{{ group.name }}</span>
                <span class="block truncate text-xs text-[var(--text-muted)]">
                  {{ group.memberCount ?? group.members.length }} people
                </span>
              </span>

              <MoneyAmount
                :amount="group.myNetBalance"
                :currency="group.baseCurrency"
                signed
                size="sm"
              />
            </button>
          </li>

          <li v-if="archived.length > 0" class="px-3 pt-3 pb-1">
            <span class="text-xs text-[var(--text-muted)]">{{ t('Archived') }}</span>
          </li>
          <li v-for="group in archived" :key="group.id">
            <button
              type="button"
              data-testid="group-option"
              :aria-current="group.id === groups.mainGroupId ? 'true' : 'false'"
              class="btn btn-press w-full justify-start gap-3 border-transparent bg-transparent text-left opacity-60"
              @click="choose(group.id)"
            >
              <span class="min-w-0 flex-1 truncate">{{ group.name }}</span>
            </button>
          </li>
        </ul>

        <div class="border-t p-3" style="border-color: var(--border)">
          <RouterLink
            :to="{ name: 'new-group' }"
            class="btn btn-press btn-primary w-full"
            @click="emit('close')"
          >{{ t('New group') }}
          </RouterLink>
        </div>
      </div>
    </div>
  </Teleport>
</template>
