/**
 * The French strings, keyed by the English ones.
 *
 * Quebec French, because that is where this is used: "courriel" rather than
 * "e-mail", "virement Interac" for an e-transfer, and the app's own vocabulary kept
 * consistent - a group is a "groupe", settling up is "regler".
 *
 * Accented, unlike the rest of the project: French without its accents reads as
 * broken French. The plain-ASCII habit is about keeping typographic ornaments out
 * of the source - em dashes, arrows, curly quotes - not letters out of a language.
 */
export const fr: Record<string, string> = {
  // Conflicts
  'Edited on two devices at once': 'Modifiée sur deux appareils à la fois',
  'Both versions were kept. Pick the one to keep - nothing was overwritten.':
    "Les deux versions ont été conservées. Choisissez celle à garder : rien n'a été écrasé.",
  'On the server': 'Sur le serveur',
  'Your version': 'Votre version',
  'Keep the server version': 'Garder la version du serveur',
  'Keep mine': 'Garder la mienne',
  'Changes the server refused': 'Modifications refusées par le serveur',
  'Discard this change': 'Abandonner cette modification',
  'Changes waiting to be sent': "Modifications en attente d'envoi",
  'Nothing needs your attention.': 'Rien ne requiert votre attention.',
  'This device': 'Cet appareil',
  'If this device is showing something the others are not, it can throw away what it has stored and ask the server for all of it again.':
    "Si cet appareil affiche autre chose que les autres, il peut effacer ce qu'il a stocké et tout redemander au serveur.",
  'Reload everything from the server': 'Tout recharger depuis le serveur',
  "Everything stored on this device is replaced by the server's version.":
    'Tout ce qui est stocké sur cet appareil est remplacé par la version du serveur.',
  Cancel: 'Annuler',
  'Needs attention': 'À vérifier',

  // Invitations
  'This invite is no longer valid. Ask for a new link.':
    "Cette invitation n'est plus valide. Demandez un nouveau lien.",
  'Checking that invite': "Vérification de l'invitation",
  'Join a group': 'Rejoindre un groupe',

  // Activity
  View: 'Voir',
  'Loading activity': "Chargement de l'activité",
  'The activity feed needs a connection. Your groups and expenses still work offline.':
    "Le fil d'activité exige une connexion. Vos groupes et vos dépenses fonctionnent encore hors ligne.",
  'Nothing has happened yet.': "Rien ne s'est encore passé.",

  // Signing in
  'Shared expenses, settled properly': 'Des dépenses partagées, réglées comme il faut',
  'Sign in with Google to see your groups. There is no password to remember.':
    'Connectez-vous avec Google pour voir vos groupes. Aucun mot de passe à retenir.',
  'Development sign-in': 'Connexion de développement',
  'This server has no Google client configured, so it is letting you in with just an address. Use a different one to act as a second person and test sharing. Never enabled in production.':
    "Ce serveur n'a aucun client Google configuré : il vous laisse donc entrer avec une simple adresse. Utilisez-en une autre pour jouer une deuxième personne et tester le partage. Jamais actif en production.",
  Email: 'Courriel',
  Name: 'Nom',
  Continue: 'Continuer',
  'Signing you in': 'Connexion en cours',
  'Split Everything': 'Split Everything',
  Alice: 'Alice',

  // Not found
  'That page does not exist': "Cette page n'existe pas",
  'Back to your groups': 'Retour à vos groupes',
  'Not found': 'Introuvable',

  // Settling up
  'Suggested transfers': 'Virements suggérés',
  Use: 'Utiliser',
  'Who paid': 'Qui a payé',
  'Still to settle': 'Reste à régler',
  'Who received it': "Qui l'a reçu",
  Note: 'Note',
  'Settle up': 'Régler',
  Etransfer: 'Virement Interac',

  // Import
  'A bank or credit card statement': 'Un relevé bancaire ou de carte de crédit',
  'The file is read on this device and never uploaded. Only the transactions you confirm are sent, and everything else is discarded when you leave this screen.':
    'Le fichier est lu sur cet appareil et jamais téléversé. Seules les transactions que vous confirmez sont envoyées; tout le reste est jeté en quittant cet écran.',
  'Choose a CSV or PDF': 'Choisir un CSV ou un PDF',
  'That statement had no text layer, so it was read from the images. Check the amounts before importing.':
    "Ce relevé n'avait pas de couche de texte : il a été lu à partir des images. Vérifiez les montants avant d'importer.",
  'Personal, not split': 'Personnelle, non partagée',
  Import: 'Importer',
  'A Settle Up export': 'Un export Settle Up',
  'Export a group from Settle Up and choose the file here. Nothing is imported until you have seen the rows.':
    "Exportez un groupe depuis Settle Up et choisissez le fichier ici. Rien n'est importé avant que vous ayez vu les lignes.",
  'Choose the CSV': 'Choisir le CSV',
  'Import into': 'Importer dans',
  'A new group': 'Un nouveau groupe',
  'An existing group': 'Un groupe existant',
  'People in the export': "Personnes dans l'export",
  'Settle Up exports names, not accounts. Anyone left unmatched is added to the group under that name, and can claim it later from an invite.':
    "Settle Up exporte des noms, pas des comptes. Toute personne non associée est ajoutée au groupe sous ce nom et pourra le réclamer plus tard par une invitation.",
  'Add as a new person': 'Ajouter comme nouvelle personne',
  'Import all of these into': 'Tout importer dans',
  From: 'De',
  'Settlement, not an expense': 'Règlement, pas une dépense',
  'Already recorded': 'Déjà enregistrée',
  'See the rows': 'Voir les lignes',
  'Group name': 'Nom du groupe',

  // Profile
  'Display name': 'Nom affiché',
  'Your currency, used for totals across groups':
    'Votre devise, utilisée pour les totaux entre les groupes',
  'App colour': "Couleur de l'application",
  'Applies everywhere, and follows your account onto any device you sign in on.':
    "S'applique partout et suit votre compte sur chaque appareil où vous vous connectez.",
  Language: 'Langue',
  'Applies to the whole app, and follows your account.':
    "S'applique à toute l'application et suit votre compte.",
  'Light mode': 'Mode clair',
  'Import a Settle Up export or a statement': 'Importer un export Settle Up ou un relevé',
  'Changes needing attention': 'Modifications à vérifier',
  'Download all my data': 'Télécharger toutes mes données',
  'Disconnect this device': 'Déconnecter cet appareil',
  'Signs you out here and stops this device reconnecting on its own, so the next start asks for an account. Your data stays on the server.':
    'Vous déconnecte ici et empêche cet appareil de se reconnecter seul : le prochain démarrage demandera un compte. Vos données restent sur le serveur.',
  'Delete my account': 'Supprimer mon compte',
  "Your name stays on past expenses so other people's balances remain correct, but your account and sign-in are removed. This cannot be undone.":
    'Votre nom reste sur les dépenses passées afin que les soldes des autres restent justes, mais votre compte et votre connexion sont supprimés. Cette action est irréversible.',
  'Keep my account': 'Garder mon compte',
  'Delete it': 'Supprimer',
  Profile: 'Profil',

  // An expense
  Date: 'Date',
  'What was it': "C'était quoi",
  Group: 'Groupe',
  Split: 'Partage',
  Between: 'Entre',
  'Saved on this device straight away, and synced when you are back online.':
    'Enregistrée immédiatement sur cet appareil, puis synchronisée dès votre retour en ligne.',
  Groceries: 'Épicerie',
  'Waiting to sync': 'En attente de synchronisation',
  'Converted to the group currency when it syncs.':
    'Convertie dans la devise du groupe lors de la synchronisation.',
  Items: 'Articles',
  Delete: 'Supprimer',
  Post: 'Publier',
  'Edit this expense': 'Modifier cette dépense',
  'Delete this expense': 'Supprimer cette dépense',
  'Delete this expense?': 'Supprimer cette dépense?',
  'Keep it': 'La garder',
  'That expense is not on this device yet.': "Cette dépense n'est pas encore sur cet appareil.",
  'Add a comment': 'Ajouter un commentaire',

  // A group
  Icon: 'Icône',
  'How a new expense is split': 'Comment une nouvelle dépense est partagée',
  'Only an owner or an admin can change this.':
    'Seul un propriétaire ou un administrateur peut modifier ceci.',
  People: 'Personnes',
  You: 'Vous',
  Owner: 'Propriétaire',
  Remove: 'Retirer',
  'Taking a colour someone else has swaps the two, so nobody ends up without one.':
    "Prendre la couleur de quelqu'un d'autre échange les deux : personne ne se retrouve sans couleur.",
  'For one person who ended up in this group twice. Everything the first paid, owes and is owed moves to the second, and the first is removed.':
    'Pour une personne qui se retrouve deux fois dans ce groupe. Tout ce que la première a payé, doit et se fait devoir passe à la seconde, et la première est retirée.',
  'Merge this person': 'Fusionner cette personne',
  'Choose who goes': 'Choisir qui disparaît',
  'Into this person': 'Dans cette personne',
  'Choose who stays': 'Choisir qui reste',
  'Choose both. This cannot be undone.': 'Choisissez les deux. Cette action est irréversible.',
  'Merge two people': 'Fusionner deux personnes',
  'Invite someone': "Inviter quelqu'un",
  'They sign in with Google to join, so the link alone gives no access.':
    'La personne se connecte avec Google pour rejoindre le groupe : le lien seul ne donne aucun accès.',
  Invite: 'Inviter',
  'Copy the invite link': "Copier le lien d'invitation",
  'Archive this group': 'Archiver ce groupe',
  'Reopen this group': 'Rouvrir ce groupe',
  'Archiving freezes a group without deleting anything. Balances and history stay readable.':
    "L'archivage gèle un groupe sans rien supprimer. Les soldes et l'historique restent consultables.",
  'Group settings': 'Paramètres du groupe',
  'Email, or leave blank for a link': 'Courriel, ou laisser vide pour un lien',
  'Group icon': 'Icône du groupe',
  Currency: 'Devise',
  'People. Search anyone who already has an account, or invite them once the group exists.':
    "Personnes. Cherchez quelqu'un qui a déjà un compte, ou invitez-le une fois le groupe créé.",
  Roommates: 'Colocataires',
  'People with an account, added so far': "Personnes avec un compte, ajoutées jusqu'ici",
  'Your groups': 'Vos groupes',
  Archived: 'Archivés',
  'Choose the group the app is on': "Choisir le groupe affiché par l'application",
  'Search by name or email': 'Chercher par nom ou courriel',

  // The dashboard
  Balances: 'Soldes',
  'Everyone is settled up.': 'Tout le monde est à jour.',
  Expenses: 'Dépenses',
  Waiting: 'En attente',
  'No expenses yet. Add the first one with the button below.':
    'Aucune dépense pour le moment. Ajoutez la première avec le bouton ci-dessous.',
  'Loading your groups': 'Chargement de vos groupes',
  'No groups yet': 'Aucun groupe pour le moment',
  'Create one for the people you share costs with, or open an invite someone sent you.':
    'Créez-en un pour les personnes avec qui vous partagez des frais, ou ouvrez une invitation reçue.',
  'New group': 'Nouveau groupe',
  'Saved on this device, waiting to sync':
    'Enregistrée sur cet appareil, en attente de synchronisation',

  // Stats
  'All groups': 'Tous les groupes',
  Daily: 'Par jour',
  Weekly: 'Par semaine',
  Monthly: 'Par mois',
  Total: 'Total',
  'Your share': 'Votre part',
  'You paid': 'Vous avez payé',
  'Spending over time': 'Dépenses dans le temps',
  'Who owes whom': 'Qui doit à qui',
  'Loading stats': 'Chargement des statistiques',
  'Stats need a connection. Your groups and expenses still work offline.':
    'Les statistiques exigent une connexion. Vos groupes et vos dépenses fonctionnent encore hors ligne.',
  'Nothing spent yet.': 'Rien de dépensé pour le moment.',

  // Furniture
  Close: 'Fermer',
  'Search icons': 'Chercher une icône',
  'No icon matches that. Try a plainer word, like food or travel.':
    'Aucune icône ne correspond. Essayez un mot plus simple, comme nourriture ou voyage.',
  'Remove icon': "Retirer l'icône",
  'Search: rent, groceries, uber, wifi': 'Chercher : loyer, épicerie, uber, wifi',
  Icons: 'Icônes',
  Loading: 'Chargement',
  'A new version is ready.': 'Une nouvelle version est prête.',
  Later: 'Plus tard',
  Reload: 'Recharger',
  'Ready to work offline.': 'Prêt à fonctionner hors ligne.',
  Main: 'Navigation principale',
  'Add an expense': 'Ajouter une dépense',
  // Saying what went wrong
  '1 change needs attention': '1 modification à vérifier',
  '{count} changes need attention': '{count} modifications à vérifier',
  'All synced': 'Tout est synchronisé',
  Syncing: 'Synchronisation',
  Offline: 'Hors ligne',
  'Offline, {count} waiting': 'Hors ligne, {count} en attente',
  '{count} waiting to sync': '{count} en attente de synchronisation',
  'Saved.': 'Enregistré.',
  'No date': 'Sans date',
  'Assign at least one transaction to a group first.':
    "Associez d'abord au moins une transaction à un groupe.",
  'Copying needs a secure connection. The link above can be selected instead.':
    'La copie exige une connexion sécurisée. Le lien ci-dessus peut être sélectionné à la place.',
  'Could not add that person.': "Impossible d'ajouter cette personne.",
  'Could not archive the group.': "Impossible d'archiver le groupe.",
  'Could not copy the link. It can be selected above instead.':
    'Impossible de copier le lien. Il peut être sélectionné ci-dessus à la place.',
  'Could not create an invite.': 'Impossible de créer une invitation.',
  'Could not create the group.': 'Impossible de créer le groupe.',
  'Could not delete that comment.': 'Impossible de supprimer ce commentaire.',
  'Could not delete the expense.': 'Impossible de supprimer la dépense.',
  'Could not delete your account.': 'Impossible de supprimer votre compte.',
  'Could not export your data.': "Impossible d'exporter vos données.",
  'Could not import that export.': "Impossible d'importer cet export.",
  'Could not import those transactions.': "Impossible d'importer ces transactions.",
  'Could not join that group.': 'Impossible de rejoindre ce groupe.',
  'Could not merge those two.': 'Impossible de fusionner ces deux personnes.',
  'Could not post the comment.': 'Impossible de publier le commentaire.',
  'Could not read that export.': 'Impossible de lire cet export.',
  'Could not read that file.': 'Impossible de lire ce fichier.',
  'Could not read those rows.': 'Impossible de lire ces lignes.',
  'Could not record the settlement.': "Impossible d'enregistrer le règlement.",
  'Could not reload from the server.': 'Impossible de recharger depuis le serveur.',
  'Could not remove that person.': 'Impossible de retirer cette personne.',
  'Could not reopen the group.': 'Impossible de rouvrir le groupe.',
  'Could not resolve that conflict.': 'Impossible de régler ce conflit.',
  'Could not save the expense.': "Impossible d'enregistrer la dépense.",
  'Could not save the group.': "Impossible d'enregistrer le groupe.",
  'Could not save your profile.': "Impossible d'enregistrer votre profil.",
  'Could not sign you in.': 'Impossible de vous connecter.',
  'Give the group a name.': 'Donnez un nom au groupe.',
  'Google did not return a credential. Try again.':
    "Google n'a renvoyé aucune identification. Essayez de nouveau.",
  'Google sign-in is unavailable. Check your connection and try again.':
    'La connexion Google est indisponible. Vérifiez votre connexion et essayez de nouveau.',
  'Invite link copied.': "Lien d'invitation copié.",
  'Pick a group first.': "Choisissez d'abord un groupe.",
  'Saved, but the group default could not be changed.':
    'Enregistré, mais le partage par défaut du groupe est resté inchangé.',
  'That expense is not on this device.': "Cette dépense n'est pas sur cet appareil.",
  'That invite could not be found.': 'Cette invitation est introuvable.',
  'That split does not add up.': 'Ce partage ne tombe pas juste.',
  'You are not a member of this group.': "Vous n'êtes pas membre de ce groupe.",

  // How an expense is split
  Equally: 'Également',
  Percent: 'Pourcentage',
  Shares: 'Parts',
  Exact: 'Exact',
  'By shares': 'Par parts',
  'By percentage': 'Par pourcentage',
  'Everyone taking part pays the same.': 'Chaque participant paie la même chose.',
  'Two shares against one pays twice as much.': 'Deux parts contre une paie deux fois plus.',
  'Has to add up to 100.': 'Doit totaliser 100.',
  // Screens and actions
  Activity: 'Activité',
  Dashboard: 'Tableau de bord',
  Stats: 'Statistiques',
  'Add someone to this group': "Ajouter quelqu'un à ce groupe",
  'Already in this group': 'Déjà dans ce groupe',
  'Everyone here': 'Tout le monde ici',
  Everyone: 'Tout le monde',
  Choose: 'Choisir',
  'Choose an icon': 'Choisir une icône',
  'Create group': 'Créer le groupe',
  Creating: 'Création',
  Deleting: 'Suppression',
  Saving: 'Enregistrement',
  'Save changes': 'Enregistrer les modifications',
  'Save expense': 'Enregistrer la dépense',
  Ignore: 'Ignorer',
  Ignored: 'Ignorée',
  Restore: 'Rétablir',
  Importing: 'Importation',
  'Import {count}': 'Importer {count}',
  'Reading the file': 'Lecture du fichier',
  'Join this group': 'Rejoindre ce groupe',
  Joining: 'Adhésion',
  'Sign in with Google to join': 'Se connecter avec Google pour rejoindre',
  'Merge for good': 'Fusionner définitivement',
  Merging: 'Fusion',
  'Record settlement': 'Enregistrer le règlement',
  Recording: 'Enregistrement',
  'Reload from the server': 'Recharger depuis le serveur',
  Reloading: 'Rechargement',
  Simplify: 'Simplifier',
  'Show who owes whom': 'Montrer qui doit à qui',
  'Settle up in 1 transfer': 'Régler en 1 virement',
  'Settle up in {count} transfers': 'Régler en {count} virements',
  '{count} expenses': '{count} dépenses',
  '{count} shares': '{count} parts',
  'By item': 'Par article',
  'Exact amounts': 'Montants exacts',
  'Edit expense': 'Modifier la dépense',
  'Add expense': 'Ajouter une dépense',
  'Amount ({currency})': 'Montant ({currency})',
  'Nothing to add up yet. Add an expense and this fills in.':
    "Rien à additionner pour le moment. Ajoutez une dépense et ceci se remplira.",
  'No activity stored on this device yet. It fills in next time you are online.':
    "Aucune activité stockée sur cet appareil pour le moment. Elle se remplira à votre prochain passage en ligne.",
  'Send now': 'Envoyer maintenant',
  Sending: 'Envoi',
  'Could not send those changes.': "Impossible d'envoyer ces modifications.",
  // Notifications and installing
  'Notifications on this device': 'Notifications sur cet appareil',
  On: 'Activées',
  Off: 'Désactivées',
  Working: 'En cours',
  'Told about a new expense, a settlement or a comment while the app is closed.':
    "Être averti d'une nouvelle dépense, d'un règlement ou d'un commentaire même quand l'application est fermée.",
  'Notifications need the app served over https. On a plain address like a local network one, the browser turns them off entirely.':
    "Les notifications exigent que l'application soit servie en https. Sur une adresse ordinaire, comme celle d'un réseau local, le navigateur les désactive complètement.",
  'This browser is blocking notifications for this site. Allow them in its site settings, then come back.':
    'Ce navigateur bloque les notifications pour ce site. Autorisez-les dans ses paramètres de site, puis revenez.',
  'This browser cannot do notifications.': 'Ce navigateur ne gère pas les notifications.',
  'Notifications were not allowed.': "Les notifications n'ont pas été autorisées.",
  'This server has no notification keys yet, so it cannot send any. Whoever runs it has to add them.':
    "Ce serveur n'a pas encore de clés de notification, il ne peut donc rien envoyer. La personne qui l'administre doit les ajouter.",
  'Could not turn notifications on. Try again.':
    "Impossible d'activer les notifications. Réessayez.",
  'Could not change notifications.': 'Impossible de modifier les notifications.',
  'Install on this device': 'Installer sur cet appareil',
  Install: 'Installer',
  'Opens without browser chrome, keeps its own icon, and works offline.':
    "S'ouvre sans l'habillage du navigateur, garde sa propre icône et fonctionne hors ligne.",
  'In Safari: Share, then Add to Home Screen. It then opens like an app, and notifications become possible.':
    "Dans Safari : Partager, puis Sur l'écran d'accueil. L'application s'ouvre alors comme une application, et les notifications deviennent possibles.",
  'Installing needs the app served over https. A plain address like a local network one cannot be installed.':
    "L'installation exige que l'application soit servie en https. Une adresse ordinaire, comme celle d'un réseau local, ne peut pas être installée.",
}
