/**
 * SHA-256, for when the platform will not do it.
 *
 * crypto.subtle exists only in a secure context, so it is missing when the app is
 * served over plain HTTP on a LAN address, which is how it gets used on a phone.
 * The statement importer fingerprints every row with SHA-256 and compares those
 * hashes with the server's, so there is no room for a different hash here: an
 * approximation would make duplicate detection quietly disagree.
 *
 * Straight from FIPS 180-4. Tested against the published vectors and, for a range
 * of inputs including random ones, against crypto.subtle itself.
 */

/** First thirty-two bits of the fractional parts of the cube roots of the first 64 primes. */
const K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
])

const rotr = (value: number, bits: number) => (value >>> bits) | (value << (32 - bits))

export function sha256Hex(text: string): string {
  const message = new TextEncoder().encode(text)

  // Padded to a whole number of 64-byte blocks: a single 1 bit, zeroes, then the
  // length in bits as a 64-bit big-endian integer.
  const blockCount = Math.floor((message.length + 8) / 64) + 1
  const padded = new Uint8Array(blockCount * 64)
  padded.set(message)
  padded[message.length] = 0x80

  const bits = message.length * 8
  const view = new DataView(padded.buffer)
  // Written as two 32-bit halves, because a JS number cannot hold 64 bits exactly
  // and the high half only matters above 512MB of input.
  view.setUint32(padded.length - 8, Math.floor(bits / 0x100000000))
  view.setUint32(padded.length - 4, bits >>> 0)

  // First thirty-two bits of the fractional parts of the square roots of the first
  // eight primes.
  const h = new Uint32Array([
    0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
    0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
  ])

  const w = new Uint32Array(64)

  for (let block = 0; block < blockCount; block++) {
    const offset = block * 64

    for (let i = 0; i < 16; i++) w[i] = view.getUint32(offset + i * 4)

    for (let i = 16; i < 64; i++) {
      const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >>> 3)
      const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >>> 10)
      w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0
    }

    let [a, b, c, d, e, f, g, hh] = h

    for (let i = 0; i < 64; i++) {
      const s1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25)
      const choose = (e & f) ^ (~e & g)
      const temp1 = (hh + s1 + choose + K[i] + w[i]) >>> 0

      const s0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22)
      const majority = (a & b) ^ (a & c) ^ (b & c)
      const temp2 = (s0 + majority) >>> 0

      hh = g
      g = f
      f = e
      e = (d + temp1) >>> 0
      d = c
      c = b
      b = a
      a = (temp1 + temp2) >>> 0
    }

    h[0] = (h[0] + a) >>> 0
    h[1] = (h[1] + b) >>> 0
    h[2] = (h[2] + c) >>> 0
    h[3] = (h[3] + d) >>> 0
    h[4] = (h[4] + e) >>> 0
    h[5] = (h[5] + f) >>> 0
    h[6] = (h[6] + g) >>> 0
    h[7] = (h[7] + hh) >>> 0
  }

  return [...h].map((word) => word.toString(16).padStart(8, '0')).join('')
}
