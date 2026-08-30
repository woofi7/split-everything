import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { db, type LocalCategory } from '@/offline/db'
import type { ApiClient } from '@/api/client'

/**
 * The categories an expense can be filed under.
 *
 * The API has served these from the start and nothing ever asked, so every
 * expense the app created was uncategorised and the by-category breakdown in
 * stats read "Uncategorised, 100%".
 *
 * Cached locally, because choosing a category is part of adding an expense and
 * adding an expense works offline.
 */
export const useCategoriesStore = defineStore('categories', () => {
  const categories = ref<LocalCategory[]>([])
  let api: ApiClient | null = null

  function attachApi(client: ApiClient): void {
    api = client
  }

  const all = computed(() => categories.value)

  const byId = (categoryId: string | null | undefined) =>
    categoryId ? categories.value.find((category) => category.id === categoryId) : undefined

  async function load(): Promise<void> {
    // Cache first, so the picker is populated before the network answers.
    categories.value = (await db.categories.toArray()).sort(
      (left, right) => left.sortOrder - right.sortOrder,
    )

    if (!api) return

    try {
      const fetched = await api.get<LocalCategory[]>('/categories')
      if (!Array.isArray(fetched)) return

      // Replaced rather than merged: the server's list is the whole truth, and a
      // category removed there should stop being offered here.
      await db.categories.clear()
      await db.categories.bulkPut(fetched)
      categories.value = fetched
    } catch {
      // The cached list is still usable, and an empty one is a fair answer on a
      // first run with no connection.
    }
  }

  return { all, byId, attachApi, load }
})
