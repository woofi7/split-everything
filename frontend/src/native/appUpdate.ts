/**
 * Asking whether there is a newer build.
 *
 * A service worker checks for one on its own schedule - on navigation, and not more
 * than once a day - which on a phone is almost never: a home-screen app is not
 * navigated, it is resumed. So an install could sit on an old version for weeks
 * while the API moved on underneath it.
 *
 * Pulling down already means "get me the latest", so it means this too. If a new
 * build is out there the browser installs it and UpdatePrompt offers it; nothing
 * reloads under anybody's feet.
 */
export async function checkForAppUpdate(): Promise<void> {
  if (!('serviceWorker' in navigator)) return

  try {
    const registration = await navigator.serviceWorker.getRegistration()

    // Fetches the worker script past the HTTP cache and installs it if it differs.
    // Silent when it is the same, which is the usual answer.
    await registration?.update()
  } catch {
    // Offline, or a browser that will not have one. A refresh that syncs is still a
    // refresh; there is nothing here worth failing it for.
  }
}
