import * as client from 'openid-client';

export async function discoverClient(settings) {
  const config = await client.discovery(new URL(settings.issuer), settings.clientId,
    { client_secret: settings.clientSecret, id_token_signed_response_alg: 'RS256' },
    client.ClientSecretPost(settings.clientSecret),
    settings.localDevelopment && new URL(settings.issuer).protocol === 'http:'
      ? { execute: [client.allowInsecureRequests] } : undefined);
  // Verify asymmetric ID-token signatures through the discovered JWKS, in addition
  // to the library's issuer, audience, lifetime, state, nonce and PKCE checks.
  client.enableNonRepudiationChecks(config);
  return oidcAdapter(config, settings.redirectUri);
}

export function oidcAdapter(config, redirectUri) {
  return {
    async start() {
      const state = client.randomState();
      const nonce = client.randomNonce();
      const verifier = client.randomPKCECodeVerifier();
      const challenge = await client.calculatePKCECodeChallenge(verifier);
      const authorizeUrl = client.buildAuthorizationUrl(config, {
        redirect_uri: redirectUri, response_type: 'code', scope: 'openid profile',
        state, nonce, code_challenge: challenge, code_challenge_method: 'S256',
      }).href;
      return { state, nonce, verifier, challenge, authorizeUrl };
    },
    complete(url, transaction) {
      return client.authorizationCodeGrant(config, url, {
        expectedState: transaction.state, expectedNonce: transaction.nonce,
        pkceCodeVerifier: transaction.verifier, idTokenExpected: true,
      });
    },
    profile(accessToken, subject) { return client.fetchUserInfo(config, accessToken, subject); },
    revoke(accessToken) { return client.tokenRevocation(config, accessToken, { token_type_hint: 'access_token' }); },
  };
}
