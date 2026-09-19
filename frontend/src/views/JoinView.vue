<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useAuthStore } from '@/stores/auth'
import { report } from '@/ui/toasts'
import { useGroupsStore } from '@/stores/groups'
import { useApi } from '@/api/provider'

interface InvitePreview {
  groupId: string
  groupName: string
  iconName: string | null
  invitedByName: string
  memberCount: number
  isRedeemable: boolean
}

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const groups = useGroupsStore()

const token = computed(() => String(route.params.token))
const preview = ref<InvitePreview | null>(null)
const error = ref<string | null>(null)
const isJoining = ref(false)

const wantsToJoin = computed(() => route.query.join === '1')

onMounted(async () => {
  try {
    preview.value = await useApi().get<InvitePreview>(`/invites/${token.value}`)
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('That invite could not be found.')
    return
  }

  if (wantsToJoin.value && auth.isSignedIn && preview.value.isRedeemable) await redeem()
})

async function join(): Promise<void> {
  if (!auth.isSignedIn) {
    await router.push({
      name: 'sign-in',
      query: { redirect: `/join/${token.value}?join=1` },
    })
    return
  }

  await redeem()
}

async function redeem(): Promise<void> {
  isJoining.value = true
  error.value = null

  try {
    const result = await useApi().post<{ groupId: string }>(`/invites/${token.value}/redeem`)
    await groups.loadAll()
    await router.replace({ name: 'group', params: { groupId: result.groupId } })
  } catch (caught) {
    report(caught, t('Could not join that group.'))
  } finally {
    isJoining.value = false
  }
}
</script>
<template>
  <AppShell :title="t('Join a group')" :show-nav="false">
    <div v-if="preview" class="flex flex-col items-center gap-5 py-8 text-center">
      <span
        class="flex h-16 w-16 items-center justify-center rounded-2xl bg-brand-600 text-2xl text-white"
        aria-hidden="true"
      >
        {{ preview.iconName || preview.groupName.slice(0, 2).toUpperCase() }}
      </span>
      <div>
        <h2 class="text-xl font-semibold">{{ preview.groupName }}</h2>
        <p class="mt-1 text-sm text-[var(--text-muted)]">
          {{ preview.invitedByName }} invited you.
          {{ preview.memberCount }} {{ preview.memberCount === 1 ? 'person is' : 'people are' }} in
          this group.
        </p>
      </div>
      <button
        v-if="preview.isRedeemable"
        type="button"
        class="btn btn-press btn-primary"
        :disabled="isJoining"
        @click="join"
      >
        {{ auth.isSignedIn
          ? isJoining ? t('Joining') : t('Join this group')
          : t('Sign in with Google to join') }}
      </button>
      <p v-else class="text-sm text-owing">{{ t('This invite is no longer valid. Ask for a new link.') }}
      </p>
    </div>
    <p v-else-if="error" class="py-8 text-center text-sm text-owing" role="alert">{{ error }}</p>
    <p v-else class="py-8 text-center text-sm text-[var(--text-muted)]">{{ t('Checking that invite') }}</p>
  </AppShell>
</template>
