import { onMounted, onUnmounted, ref, type Ref } from 'vue'

export function useFitText(target: Ref<HTMLElement | null>, sizes: readonly number[] = [24, 20, 18, 16]) {
  const size = ref(sizes[0])
  const overflows = ref(false)

  function fit(): void {
    const element = target.value
    if (!element) return

    for (const candidate of sizes) {
      element.style.fontSize = `${candidate}px`
      if (element.scrollWidth <= element.clientWidth) {
        size.value = candidate
        overflows.value = false
        return
      }
    }

    size.value = sizes[sizes.length - 1]
    overflows.value = true
  }

  onMounted(() => {
    window.addEventListener('resize', fit)
    fit()
  })
  onUnmounted(() => window.removeEventListener('resize', fit))

  return { fit, size, overflows }
}
