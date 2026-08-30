#!/usr/bin/env node
/*
 * Generates the VAPID pair the API signs Web Push messages with.
 *
 *   node infra/vapid/generate.mjs
 *
 * Node's own crypto rather than `npx web-push`, so a server with no npm registry
 * reachable can still produce a pair, and so the private key never leaves the
 * machine that will use it.
 *
 * The format is not ours to choose: Web Push wants the P-256 public key as the
 * uncompressed point (0x04 then x then y, 65 bytes) and the private key as the
 * 32-byte scalar, both base64url without padding. A key in any other shape is
 * accepted by the API and then rejected by every push service.
 */
import { generateKeyPairSync } from 'node:crypto'

const { publicKey, privateKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' })

const base64url = (buffer) => buffer.toString('base64url')
const jwk = publicKey.export({ format: 'jwk' })
const priv = privateKey.export({ format: 'jwk' })

const point = Buffer.concat([
  Buffer.from([0x04]),
  Buffer.from(jwk.x, 'base64url'),
  Buffer.from(jwk.y, 'base64url'),
])

if (point.length !== 65) throw new Error(`public key is ${point.length} bytes, expected 65`)

console.log('VAPID_PUBLIC_KEY=' + base64url(point))
console.log('VAPID_PRIVATE_KEY=' + priv.d)
console.log('VAPID_SUBJECT=mailto:you@example.com')
console.log()
console.log('Put these three in .env, then: docker compose up -d api')
console.log('Changing them later invalidates every existing subscription.')
