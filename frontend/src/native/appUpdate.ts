export async function checkForAppUpdate(): Promise<void> {
  if (!('serviceWorker' in navigator)) return

  try {
    const registration = await navigator.serviceWorker.getRegistration()

    await registration?.update()
  } catch {
  }
}
