<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const groups = useGroupsStore()
const expenses = useExpensesStore()
const auth = useAuthStore()

const groupId = computed(() => String(route.params.groupId))
const expenseId = computed(() => String(route.params.expenseId))
const commentDraft = ref('')
const error = ref<string | null>(null)

onMounted(async () => {
  await groups.get(groupId.value)
  await expenses.hydrate()
})

const expense = computed(() =>
  expenses.expenses.find((candidate) => candidate.id === expenseId.value),
)

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))

const comments = computed(() => expenses.commentsFor(expenseId.value))

const memberName = (memberId: string) =>
  groups.membersOf(groupId.value).find((member) => member.id === memberId)?.displayName ?? 'Unknown'

const myMemberId = computed(() =>
  auth.user ? groups.myMemberId(groupId.value, auth.user.id) : null,
)

async function postComment(): Promise<void> {
  error.value = null
  if (!myMemberId.value) {
    error.value = 'You are not a member of this group.'
    return
  }

  try {
    await expenses.comment(expenseId.value, commentDraft.value, myMemberId.value)
    commentDraft.value = ''
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not post the comment.'
  }
}

async function remove(): Promise<void> {
  error.value = null
  try {
    await expenses.remove(expenseId.value)
    await router.replace({ name: 'group', params: { groupId: groupId.value } })
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not delete the expense.'
  }
}
</script>

<template>
  <AppShell
    :title="expense?.description ?? 'Expense'"
    :back-to="{ name: 'group', params: { groupId } }"
    :back-label="group?.name ?? 'Group'"
  >
    <div v-if="expense" class="flex flex-col gap-5">
      <section class="surface-card p-4">
        <div class="flex items-baseline justify-between gap-3">
          <MoneyAmount :amount="expense.amount" :currency="expense.currency" size="lg" />
          <span
            v-if="expense.pending"
            class="rounded-full bg-brand-600/20 px-2 py-0.5 text-xs text-brand-400"
          >
            Waiting to sync
          </span>
        </div>
        <p class="mt-1 text-sm text-[var(--text-muted)]">
          {{ memberName(expense.paidByMemberId) }} paid on
          {{ new Date(expense.spentAt).toLocaleDateString() }}
        </p>
        <p v-if="expense.currency !== groups.groups.find((g) => g.id === groupId)?.baseCurrency"
           class="mt-1 text-xs text-[var(--text-muted)]">
          Converted to the group currency when it syncs.
        </p>
        <p v-if="expense.notes" class="mt-2 text-sm">{{ expense.notes }}</p>
      </section>

      <section class="surface-card p-4">
        <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">Split</h2>
        <ul class="flex flex-col gap-2 text-sm">
          <li
            v-for="split in expense.splits"
            :key="split.memberId"
            class="flex justify-between"
          >
            <span>{{ memberName(split.memberId) }}</span>
            <MoneyAmount :amount="split.amount" :currency="expense.currency" size="sm" />
          </li>
        </ul>
      </section>

      <section v-if="expense.items.length > 0" class="surface-card p-4">
        <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">Items</h2>
        <ul class="flex flex-col gap-2 text-sm">
          <li v-for="item in expense.items" :key="item.description" class="flex justify-between gap-3">
            <span class="min-w-0">
              <span class="block truncate">{{ item.description }}</span>
              <span class="block truncate text-xs text-[var(--text-muted)]">
                {{ item.memberIds.map(memberName).join(', ') || 'Everyone' }}
              </span>
            </span>
            <MoneyAmount :amount="item.amount * item.quantity" :currency="expense.currency" size="sm" />
          </li>
        </ul>
      </section>

      <section class="surface-card p-4">
        <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">
          Comments ({{ comments.length }})
        </h2>

        <ul v-if="comments.length > 0" class="mb-3 flex flex-col gap-3 text-sm">
          <li v-for="comment in comments" :key="comment.id">
            <p class="text-xs text-[var(--text-muted)]">
              {{ memberName(comment.authorMemberId) }}
            </p>
            <p>{{ comment.body }}</p>
          </li>
        </ul>

        <form class="flex gap-2" @submit.prevent="postComment">
          <input
            v-model="commentDraft"
            type="text"
            maxlength="4000"
            placeholder="Add a comment"
            class="tap-target flex-1 rounded-lg border bg-[var(--surface)] px-3"
            style="border-color: var(--border)"
          />
          <button
            type="submit"
            class="tap-target rounded-lg border px-3 text-sm"
            style="border-color: var(--border)"
          >
            Post
          </button>
        </form>
      </section>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>

      <button
        type="button"
        class="tap-target rounded-lg border text-sm text-owing"
        style="border-color: var(--border)"
        @click="remove"
      >
        Delete this expense
      </button>
    </div>

    <p v-else class="text-sm text-[var(--text-muted)]">
      That expense is not on this device yet.
    </p>
  </AppShell>
</template>
