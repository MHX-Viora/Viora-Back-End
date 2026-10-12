export interface OAuthTransaction {
  clientId?: string;
  redirectUri: string;
  state: string;
  nonce: string;
  codeChallenge: string;
  codeChallengeMethod: 'S256';
  scopes: ('openid' | 'profile' | 'email' | 'phone')[];
}
export interface PartnerSession { authenticated: boolean; csrfToken: string; issuer: string; runtimeSessionId?: string; }
export interface PartnerProfile { sub: string; name?: string; picture?: string; email?: string; phone_number?: string; }
export interface SdkConfiguration { hostOrigin: string; bff?: boolean | { sessionPath?: string; startPath?: string; profilePath?: string; logoutPath?: string }; }
export interface AnktSdk {
  readonly version: string;
  configure(options: SdkConfiguration): void;
  initialize(options: SdkConfiguration): Promise<void>;
  getEnvironment(): { embedded: boolean; ready: boolean; transport: 'native' | 'web' | 'standalone' };
  ready(): Promise<void>;
  getPlatformInfo(): Promise<{ platform: string; version: string; theme: string }>;
  getAppInfo(): Promise<{ id: string; name: string; version: number; developer: string }>;
  requestOAuthLogin(params: OAuthTransaction): Promise<{ authorizeUrl: string }>;
  auth: {
    login(params?: OAuthTransaction): Promise<unknown>;
    autoLogin(): Promise<{ status: string }>;
    logout(): Promise<unknown>;
    getSession(): Promise<PartnerSession>;
    getPermissions(): Promise<{ permissions: string[] }>;
  };
  user: { getProfile(): Promise<PartnerProfile>; getBasicProfile(): Promise<PartnerProfile> };
  permissions: { request(permission: string): Promise<{ permissions: string[] }> };
  app: { close(): Promise<void> };
  navigation: { openExternal(url: string): Promise<{ opened: boolean }> };
  getGrantedPermissions(): Promise<{ permissions: string[] }>;
  requestPermission(permission: string): Promise<{ permissions: string[] }>;
  openExternalUrl(url: string): Promise<{ opened: boolean }>;
  closeMiniApp(): Promise<void>;
}
declare global { interface Window { ANKT: AnktSdk; } const ANKT: AnktSdk; }
