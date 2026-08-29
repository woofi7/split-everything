<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'
import { ApiClient } from '@/api/client'

interface InvitePreview {
  groupId: string
  groupName: string
  emojiIcon: string | null
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

/**
 * The invite landing page is public: someone who has never opened the app has to
 * see which group they were invited to before deciding to sign in. Joining still
 * requires Google, so the link alone grants nothing.
 */
const anonymousApi = new ApiClient({
  baseUrl: import.meta.env.VITE_API_BASE_URL ?? '/api',
  getAccessToken: () => auth.accessToken,
  getDeviceId: () => null,
  onUnauthorized: () => {},
})

onMounted(async () => {
  try {
    preview.value = await anonymousApi.get<InvitePreview>(`/invites/${token.value}`)
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'That invite could not be found.'
  }
})

async function join(): Promise<void> {
  if (!auth.isSignedIn) {
    await router.push({ name: 'sign-in', query: { redirect: route.fullPath } })
    return
  }

  isJoining.value = true
  error.value = null

  try {
    const result = await anonymousApi.post<{ groupId: string }>(`/invites/${token.value}/redeem`)
    await groups.loadAll()
    await router.replace({ name: 'group', params: { groupId: result.groupId } })
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not join that group.'
  } finally {
    isJoining.value = false
  }
}
</script>

<template>
  <AppShell title="Join a group" :show-nav="false">
    <div v-if="preview" class="flex flex-col items-center gap-5 py-8 text-center">
      <span
        class="flex h-16 w-16 items-center justify-center rounded-2xl bg-brand-600 text-2xl text-white"
        aria-hidden="true"
      >
        {{ preview.emojiIcon || preview.groupName.slice(0, 2).toUpperCase() }}
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
        class="tap-target rounded-lg bg-brand-600 px-6 font-medium text-white disabled:opacity-60"
        :disabled="isJoining"
        @click="join"
      >
        {{ auth.isSignedIn ? (isJoining ? 'Joining' : 'Join this group') : 'Sign in with Google to join' }}
      </button>

      <p v-else class="text-sm text-owing">
        This invite is no longer valid. Ask for a new link.
      </p>
    </div>

    <p v-else-if="error" class="py-8 text-center text-sm text-owing" role="alert">{{ error }}</p>

    <p v-else class="py-8 text-center text-sm text-[var(--text-muted)]">Checking that invite</p>
  </AppShell>
</template>
