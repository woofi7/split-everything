<script setup lang="ts">
import { ref } from 'vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faChevronRight } from '@fortawesome/free-solid-svg-icons'

/**
 * A settings section that keeps itself out of the way.
 *
 * Some of these are lists - fourteen categories, each with a name, an icon, a
 * colour and a line of keywords - and a screen that shows them all at once buries
 * everything underneath them. They are also the settings somebody changes twice a
 * year, so closed is the right resting state: the heading says what is in there
 * and how much of it, which is all a reader scrolling past needs.
 *
 * The same shape as the month headings on the group screen, down to the chevron
 * that turns, because it is the same gesture doing the same thing.
 */

const props = withDefaults(
  defineProps<{
    title: string
    /** Said on the heading, so a closed section is not silent about its contents. */
    count?: number | string | null
    open?: boolean
    testid?: string
  }>(),
  { count: null, open: false, testid: undefined },
)

const isOpen = ref(props.open)
</script>

<template>
  <section class="surface-card mb-4">
    <button
      type="button"
      :data-testid="props.testid ? `${props.testid}-toggle` : 'section-toggle'"
      class="flex w-full items-center gap-2 p-4 text-left"
      :aria-expanded="isOpen"
      @click="isOpen = !isOpen"
    >
      <FontAwesomeIcon
        :icon="faChevronRight"
        class="h-3 w-3 shrink-0 text-[var(--text-muted)] transition-transform"
        :class="isOpen ? 'rotate-90' : ''"
        aria-hidden="true"
      />
      <span class="min-w-0 flex-1 truncate text-sm font-medium text-[var(--text-muted)]">
        {{ title }}
      </span>
      <span
        v-if="count !== null && count !== ''"
        data-testid="section-count"
        class="shrink-0 text-xs text-[var(--text-muted)]"
      >{{ count }}
      </span>
    </button>

    <div v-if="isOpen" :data-testid="props.testid" class="px-4 pb-4">
      <slot />
    </div>
  </section>
</template>
