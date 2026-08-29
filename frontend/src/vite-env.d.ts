/// <reference types="vite/client" />
/// <reference types="vite-plugin-pwa/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  readonly VITE_GOOGLE_CLIENT_ID?: string
  readonly VITE_VAPID_PUBLIC_KEY?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

/** PDF.js ships its worker as a URL import, which needs declaring. */
declare module 'pdfjs-dist/build/pdf.worker.mjs?url' {
  const src: string
  export default src
}
