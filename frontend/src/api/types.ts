/** Shapes the API returns that more than one layer needs. */

/**
 * Someone with an account who could be added to a group.
 *
 * The email travels with it because two people can share a display name, and
 * whoever is choosing has to be able to tell them apart.
 */
export interface AddableUser {
  id: string
  displayName: string
  email: string
  avatarUrl: string | null
}
