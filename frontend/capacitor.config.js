/**
 * The native shells.
 *
 * Capacitor wraps the same Vue app rather than duplicating it, which is what the
 * spec settled on: one codebase, with real APNs and FCM push and proper
 * background execution hooks that iOS denies a plain web app.
 *
 * The shells load the app from the deployed origin rather than from a bundled
 * copy, so a fix ships without an App Store review; the service worker still
 * provides the offline shell, and the sync engine already treats being offline as
 * the normal case.
 */
const config = {
    appId: 'com.splitEverything.app',
    appName: 'Split Everything',
    webDir: 'dist',
    server: {
        // Set to the deployed origin for a hosted shell. Left unset here so a debug
        // build runs against the bundled dist and works with no server at all.
        androidScheme: 'https',
        iosScheme: 'https',
        hostname: 'localhost',
    },
    ios: {
        contentInset: 'always',
        // The bottom tab bar handles its own safe-area padding.
        limitsNavigationsToAppBoundDomains: true,
    },
    android: {
        allowMixedContent: false,
    },
    plugins: {
        PushNotifications: {
            // A push is a nudge to open the app; the sync engine pulls the actual
            // change, so the payload never has to be trusted.
            presentationOptions: ['badge', 'sound', 'alert'],
        },
        SplashScreen: {
            launchAutoHide: true,
            launchShowDuration: 600,
            backgroundColor: '#0f172aff',
            showSpinner: false,
        },
    },
};
export default config;
