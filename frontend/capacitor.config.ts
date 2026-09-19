import type { CapacitorConfig } from '@capacitor/cli'

const config: CapacitorConfig = {
  appId: 'com.splitEverything.app',
  appName: 'Split Everything',
  webDir: 'dist',

  server: {
    androidScheme: 'https',
    iosScheme: 'https',
    hostname: 'localhost',
  },

  ios: {
    contentInset: 'always',
    limitsNavigationsToAppBoundDomains: true,
  },

  android: {
    allowMixedContent: false,
  },

  plugins: {
    PushNotifications: {
      presentationOptions: ['badge', 'sound', 'alert'],
    },
    SplashScreen: {
      launchAutoHide: true,
      launchShowDuration: 600,
      backgroundColor: '#0f172aff',
      showSpinner: false,
    },
  },
}

export default config
